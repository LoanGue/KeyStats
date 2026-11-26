using System.Runtime.InteropServices;
using KeyStats.Core.Crypto;
using Windows.Security.Credentials;
using Windows.Security.Cryptography;

namespace KeyStats.Dashboard;

/// <summary>
/// Signature Windows Hello par la clé liée au TPM que l'assistant a créée (<c>KeyCredentialManager</c>).
/// Annulation, clé absente ou Hello indisponible : null, jamais d'exception.
/// </summary>
internal sealed partial class HelloSigner : IHelloSigner
{
    // Fenêtre de l'invite Windows Hello. Ouverte pour une application de bureau, elle reste souvent
    // derrière la fenêtre active : on la ramène devant pendant quelques secondes. Elle appartient à
    // CredentialUIBroker.exe, pas à ce processus : la recherche porte sur la classe seule, et une invite
    // ouverte au même moment par une autre application pourrait passer devant à sa place (limite acceptée).
    private const string PromptClass = "Credential Dialog Xaml Host";

    public async Task<byte[]?> SignAsync(string credentialId, byte[] challenge)
    {
        try
        {
            var opened = await KeyCredentialManager.OpenAsync(credentialId);
            if (opened.Status != KeyCredentialStatus.Success) return null;
            using var focus = BringPromptForward();
            var signed = await opened.Credential.RequestSignAsync(CryptographicBuffer.CreateFromByteArray(challenge));
            if (signed.Status != KeyCredentialStatus.Success) return null;
            CryptographicBuffer.CopyToByteArray(signed.Result, out byte[] signature);
            return signature;
        }
        catch (Exception)
        {
            // CsWinRT traduit les HRESULT en exceptions variées (COM, argument, annulation…) : toutes valent un refus.
            return null;
        }
    }

    private static System.Windows.Forms.Timer BringPromptForward()
    {
        var timer = new System.Windows.Forms.Timer { Interval = 100 };
        int ticks = 0;
        timer.Tick += (_, _) =>
        {
            nint prompt = FindWindowW(PromptClass, null);
            if (prompt != 0 && SetForegroundWindow(prompt) || ++ticks >= 50) timer.Stop();
        };
        timer.Start();
        return timer;
    }

    [LibraryImport("user32.dll", StringMarshalling = StringMarshalling.Utf16)]
    private static partial nint FindWindowW(string? className, string? windowName);

    [LibraryImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool SetForegroundWindow(nint window);
}
