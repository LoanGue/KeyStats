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

    /// <summary>
    /// Utilise la clé sous le verrou de la session : un <see cref="Lock"/> concurrent attend la fin de
    /// <paramref name="use"/>, qui ne doit pas garder la clé au-delà. <see cref="InvalidOperationException"/>
    /// si la session est verrouillée.
    /// </summary>
    public T Use<T>(Func<MasterKey, T> use)
    {
        ArgumentNullException.ThrowIfNull(use);
        lock (_gate) return use(_key ?? throw new InvalidOperationException("Session verrouillée."));
    }

    public bool TryUnlockWithPassphrase(KeyStatsDb db, string phrase)
    {
        ArgumentNullException.ThrowIfNull(db);
        var entry = Entry(db, KeyStoreKind.Passphrase);
        return entry is not null && Accept(db, Unwrap(() => KeyWrap.UnwrapWithPassphrase(entry, phrase)));
    }

    public async Task<bool> TryUnlockWithHelloAsync(KeyStatsDb db, IHelloSigner signer)
    {
        ArgumentNullException.ThrowIfNull(db);
        ArgumentNullException.ThrowIfNull(signer);
        var entry = Entry(db, KeyStoreKind.WindowsHello);
        if (entry?.CredentialId is null) return false;
        // Reprise sur le thread appelant (celui de l'interface dans le tableau de bord), voulue.
        byte[]? signature = await signer.SignAsync(entry.CredentialId, entry.Salt).ConfigureAwait(true);
        if (signature is null) return false;
        try
        {
            return Accept(db, Unwrap(() => KeyWrap.UnwrapWithHello(entry, signature)));
        }
        finally
        {
            CryptographicOperations.ZeroMemory(signature);
        }
    }

    /// <summary>Prend la clé que l'assistant vient de créer (<see cref="KeySetup"/>) : pas de déverrouillage à refaire.</summary>
    internal void Adopt(MasterKey key)
    {
        ArgumentNullException.ThrowIfNull(key);
        lock (_gate)
        {
            _key?.Dispose();
            _key = key;
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

    /// <summary>Une signature trop courte ou une entrée altérée (itérations, sel) vaut un échec, pas une erreur.</summary>
    private static byte[]? Unwrap(Func<byte[]?> unwrap)
    {
        try
        {
            return unwrap();
        }
        catch (Exception e) when (e is ArgumentException or CryptographicException)
        {
            return null;
        }
    }

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
