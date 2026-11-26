using System.Security.Cryptography;
using KeyStats.Core.Storage;

namespace KeyStats.Core.Crypto;

/// <summary>Signature Windows Hello d'un défi, fournie par le tableau de bord (WinRT).</summary>
public interface IHelloSigner
{
    /// <returns>La signature, ou null si l'utilisateur annule ou si Hello n'est pas disponible.</returns>
    Task<byte[]?> SignAsync(string credentialId, byte[] challenge);
}

/// <summary>
/// Clé privée maîtresse déverrouillée, en mémoire seulement, le temps que le tableau de bord reste ouvert.
/// Elle n'est acceptée que si sa partie publique est celle de la base (<c>master_pub</c>).
/// </summary>
public sealed class KeySession : IDisposable
{
    private readonly Lock _gate = new();
    private MasterKey? _key;

    public bool IsUnlocked
    {
        get
        {
            lock (_gate) return _key is not null;
        }
    }

    /// <summary>Clé déverrouillée ; <see cref="InvalidOperationException"/> si la session est verrouillée.</summary>
    public MasterKey Key
    {
        get
        {
            lock (_gate) return _key ?? throw new InvalidOperationException("Session verrouillée.");
        }
    }

    public bool TryUnlockWithPassphrase(KeyStatsDb db, string phrase)
    {
        ArgumentNullException.ThrowIfNull(db);
        var entry = Entry(db, KeyStoreKind.Passphrase);
        return entry is not null && Accept(db, KeyWrap.UnwrapWithPassphrase(entry, phrase));
    }

    public async Task<bool> TryUnlockWithHelloAsync(KeyStatsDb db, IHelloSigner signer)
    {
        ArgumentNullException.ThrowIfNull(db);
        ArgumentNullException.ThrowIfNull(signer);
        var entry = Entry(db, KeyStoreKind.WindowsHello);
        if (entry?.CredentialId is null) return false;
        byte[]? signature = await signer.SignAsync(entry.CredentialId, entry.Salt).ConfigureAwait(true);
        if (signature is null) return false;
        try
        {
            return Accept(db, KeyWrap.UnwrapWithHello(entry, signature));
        }
        finally
        {
            CryptographicOperations.ZeroMemory(signature);
        }
    }

    /// <summary>Efface la clé de la mémoire.</summary>
    public void Lock()
    {
        lock (_gate)
        {
            _key?.Dispose();
            _key = null;
        }
    }

    public void Dispose() => Lock();

    private static KeyStoreEntry? Entry(KeyStatsDb db, KeyStoreKind kind) =>
        db.GetKeyStore().LastOrDefault(e => e.Kind == kind);

    private bool Accept(KeyStatsDb db, byte[]? pkcs8)
    {
        if (pkcs8 is null) return false;
        MasterKey? key = null;
        try
        {
            key = MasterKey.FromPkcs8(pkcs8);
            if (db.GetMeta(MetaKeys.MasterPublicKey) is not { } expected
                || !CryptographicOperations.FixedTimeEquals(key.PublicKey, expected))
                return false;
            lock (_gate)
            {
                _key?.Dispose();
                _key = key;
            }
            key = null;
            return true;
        }
        finally
        {
            key?.Dispose();
            CryptographicOperations.ZeroMemory(pkcs8);
        }
    }
}
