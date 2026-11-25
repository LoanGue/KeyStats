using System.Security.Cryptography;
using System.Text;
using KeyStats.Core.Storage;

namespace KeyStats.Core.Crypto;

/// <summary>
/// Protection de la clé privée maîtresse (table <c>key_store</c>) : chiffrée en AES-256-GCM par une
/// clé dérivée soit de la phrase de secours (PBKDF2-SHA256), soit de la signature Windows Hello (HKDF).
/// </summary>
public static class KeyWrap
{
    public const int PassphraseIterations = 600_000;
    public const int SaltSize = 16, HelloChallengeSize = 32;
    private const int TagSize = 16;

    private static ReadOnlySpan<byte> HelloInfo => "keystats hello kek v1"u8;

    // ---- Phrase de secours ----

    /// <summary>
    /// Forme canonique de la phrase : Unicode NFC, espaces de bord retirés, suites d'espaces réduites
    /// à une seule. La casse est gardée.
    /// </summary>
    public static string NormalizePassphrase(string phrase)
    {
        ArgumentNullException.ThrowIfNull(phrase);
        string nfc = phrase.Normalize(NormalizationForm.FormC).Trim();
        var sb = new StringBuilder(nfc.Length);
        bool space = false;
        foreach (char c in nfc)
        {
            if (char.IsWhiteSpace(c))
            {
                space = true;
                continue;
            }
            if (space) sb.Append(' ');
            space = false;
            sb.Append(c);
        }
        return sb.ToString();
    }

    public static KeyStoreEntry WrapWithPassphrase(ReadOnlySpan<byte> pkcs8, string phrase, long nowMs, int iterations = PassphraseIterations)
    {
        byte[] salt = RandomNumberGenerator.GetBytes(SaltSize);
        byte[] kek = PassphraseKek(phrase, salt, iterations);
        try
        {
            var (nonce, wrapped) = Seal(kek, pkcs8, KeyStoreKind.Passphrase);
            return new KeyStoreEntry(0, KeyStoreKind.Passphrase, nowMs, salt, iterations, nonce, wrapped, null);
        }
        finally
        {
            CryptographicOperations.ZeroMemory(kek);
        }
    }

    /// <summary>Clé privée PKCS#8, ou null si la phrase est fausse.</summary>
    public static byte[]? UnwrapWithPassphrase(KeyStoreEntry entry, string phrase)
    {
        ArgumentNullException.ThrowIfNull(entry);
        if (entry.Kind != KeyStoreKind.Passphrase) throw new ArgumentException("Entrée d'un autre type.", nameof(entry));
        byte[] kek = PassphraseKek(phrase, entry.Salt, entry.Iterations);
        try
        {
            return Open(kek, entry);
        }
        finally
        {
            CryptographicOperations.ZeroMemory(kek);
        }
    }

    private static byte[] PassphraseKek(string phrase, byte[] salt, int iterations)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(iterations, 100_000);
        byte[] pwd = Encoding.UTF8.GetBytes(NormalizePassphrase(phrase));
        try
        {
            return Rfc2898DeriveBytes.Pbkdf2(pwd, salt, iterations, HashAlgorithmName.SHA256, 32);
        }
        finally
        {
            CryptographicOperations.ZeroMemory(pwd);
        }
    }

    // ---- Windows Hello ----

    /// <summary>Défi à faire signer par la clé Windows Hello (stocké comme sel de l'entrée).</summary>
    public static byte[] NewHelloChallenge() => RandomNumberGenerator.GetBytes(HelloChallengeSize);

    /// <param name="signature">Signature du défi par la clé Hello (RSA PKCS#1 v1.5 : toujours la même pour un même défi).</param>
    public static KeyStoreEntry WrapWithHello(ReadOnlySpan<byte> pkcs8, byte[] challenge, ReadOnlySpan<byte> signature, string credentialId, long nowMs)
    {
        ArgumentNullException.ThrowIfNull(challenge);
        byte[] kek = HelloKek(signature, challenge);
        try
        {
            var (nonce, wrapped) = Seal(kek, pkcs8, KeyStoreKind.WindowsHello);
            return new KeyStoreEntry(0, KeyStoreKind.WindowsHello, nowMs, challenge, 0, nonce, wrapped, credentialId);
        }
        finally
        {
            CryptographicOperations.ZeroMemory(kek);
        }
    }

    /// <summary>Clé privée PKCS#8, ou null si la signature ne correspond pas.</summary>
    public static byte[]? UnwrapWithHello(KeyStoreEntry entry, ReadOnlySpan<byte> signature)
    {
        ArgumentNullException.ThrowIfNull(entry);
        if (entry.Kind != KeyStoreKind.WindowsHello) throw new ArgumentException("Entrée d'un autre type.", nameof(entry));
        byte[] kek = HelloKek(signature, entry.Salt);
        try
        {
            return Open(kek, entry);
        }
        finally
        {
            CryptographicOperations.ZeroMemory(kek);
        }
    }

    private static byte[] HelloKek(ReadOnlySpan<byte> signature, ReadOnlySpan<byte> challenge)
    {
        if (signature.Length < 64) throw new ArgumentException("Signature trop courte.", nameof(signature));
        var kek = new byte[32];
        HKDF.DeriveKey(HashAlgorithmName.SHA256, signature, kek, challenge, HelloInfo);
        return kek;
    }

    // ---- AES-GCM ----

    private static (byte[] Nonce, byte[] Wrapped) Seal(byte[] kek, ReadOnlySpan<byte> pkcs8, KeyStoreKind kind)
    {
        byte[] nonce = RandomNumberGenerator.GetBytes(12);
        var wrapped = new byte[pkcs8.Length + TagSize];
        using var aes = new AesGcm(kek, TagSize);
        aes.Encrypt(nonce, pkcs8, wrapped.AsSpan(0, pkcs8.Length), wrapped.AsSpan(pkcs8.Length), Aad(kind));
        return (nonce, wrapped);
    }

    private static byte[]? Open(byte[] kek, KeyStoreEntry entry)
    {
        if (entry.Wrapped.Length < TagSize) return null;
        int n = entry.Wrapped.Length - TagSize;
        var plain = new byte[n];
        using var aes = new AesGcm(kek, TagSize);
        try
        {
            aes.Decrypt(entry.Nonce, entry.Wrapped.AsSpan(0, n), entry.Wrapped.AsSpan(n), plain, Aad(entry.Kind));
            return plain;
        }
        catch (AuthenticationTagMismatchException)
        {
            return null;
        }
    }

    private static byte[] Aad(KeyStoreKind kind) => [.. "KSK1"u8, (byte)kind];
}
