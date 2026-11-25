using System.ComponentModel;
using System.Runtime.InteropServices;

namespace KeyStats.Agent.Interop;

/// <summary>DPAPI du compte courant : protège une petite clé locale sans demander de mot de passe.</summary>
internal static unsafe partial class Dpapi
{
    private const uint CRYPTPROTECT_UI_FORBIDDEN = 0x1;

    // Distingue nos blobs de ceux d'autres programmes du même compte.
    private static ReadOnlySpan<byte> Entropy => "KeyStats title key v1"u8;

    [StructLayout(LayoutKind.Sequential)]
    private struct DATA_BLOB
    {
        public uint cbData;
        public byte* pbData;
    }

    [LibraryImport("crypt32.dll", SetLastError = true)]
    private static partial int CryptProtectData(DATA_BLOB* dataIn, char* description, DATA_BLOB* entropy,
        nint reserved, nint prompt, uint flags, DATA_BLOB* dataOut);
    [LibraryImport("crypt32.dll", SetLastError = true)]
    private static partial int CryptUnprotectData(DATA_BLOB* dataIn, char** description, DATA_BLOB* entropy,
        nint reserved, nint prompt, uint flags, DATA_BLOB* dataOut);
    [LibraryImport("kernel32.dll")]
    private static partial nint LocalFree(nint mem);

    public static byte[] Protect(ReadOnlySpan<byte> data) => Run(data, protect: true);

    /// <summary>Lève <see cref="Win32Exception"/> si le blob vient d'un autre compte ou d'un profil réinitialisé.</summary>
    public static byte[] Unprotect(ReadOnlySpan<byte> blob) => Run(blob, protect: false);

    private static byte[] Run(ReadOnlySpan<byte> input, bool protect)
    {
        fixed (byte* pIn = input)
        fixed (byte* pEntropy = Entropy)
        {
            var dataIn = new DATA_BLOB { cbData = (uint)input.Length, pbData = pIn };
            var entropy = new DATA_BLOB { cbData = (uint)Entropy.Length, pbData = pEntropy };
            DATA_BLOB dataOut = default;
            int ok = protect
                ? CryptProtectData(&dataIn, null, &entropy, 0, 0, CRYPTPROTECT_UI_FORBIDDEN, &dataOut)
                : CryptUnprotectData(&dataIn, null, &entropy, 0, 0, CRYPTPROTECT_UI_FORBIDDEN, &dataOut);
            if (ok == 0) throw new Win32Exception(Marshal.GetLastPInvokeError());
            try
            {
                return new ReadOnlySpan<byte>(dataOut.pbData, (int)dataOut.cbData).ToArray();
            }
            finally
            {
                // La clé en clair ne reste pas dans le tas natif.
                new Span<byte>(dataOut.pbData, (int)dataOut.cbData).Clear();
                LocalFree((nint)dataOut.pbData);
            }
        }
    }
}
