using System.Security.Cryptography;
using KeyStats.Core.Storage;

namespace KeyStats.Core.Crypto;

/// <summary>
/// Nouvelle enveloppe de la clé maîtresse déverrouillée : la clé elle-même ne change pas, donc le brut déjà
/// chiffré reste lisible et l'agent garde sa clé publique. L'ancienne enveloppe du même type est remplacée.
/// </summary>
public static class KeyRewrap
{
    /// <exception cref="InvalidOperationException">Session verrouillée.</exception>
    /// <exception cref="ArgumentException">Phrase trop courte.</exception>
    public static void SetPassphrase(KeyStatsDb db, KeySession session, string passphrase, long nowMs,
        int iterations = KeyWrap.PassphraseIterations)
    {
        if (!KeySetup.IsAcceptable(passphrase)) throw new ArgumentException("Phrase de secours trop courte.", nameof(passphrase));
        Replace(db, session, pkcs8 => KeyWrap.WrapWithPassphrase(pkcs8, passphrase, nowMs, iterations));
    }

    /// <exception cref="InvalidOperationException">Session verrouillée.</exception>
    /// <summary>
    /// L'enveloppe est rouverte avec la signature avant d'être écrite : une fois cet appel revenu, la nouvelle
    /// clé Hello ouvre la clé maîtresse et l'ancienne peut être supprimée du magasin de Windows.
    /// </summary>
    /// <exception cref="ArgumentException">Signature Hello inutilisable : rien n'est écrit.</exception>
    public static void SetHello(KeyStatsDb db, KeySession session, HelloEnrollment hello, long nowMs)
    {
        ArgumentNullException.ThrowIfNull(hello);
        Replace(db, session, pkcs8 => KeyWrap.WrapWithHello(pkcs8, hello.Challenge, hello.Signature, hello.CredentialId, nowMs),
            entry => KeyWrap.UnwrapWithHello(entry, hello.Signature));
    }

    /// <summary>L'enveloppe est calculée hors transaction (PBKDF2) : l'agent n'attend pas le verrou de la base.</summary>
    /// <param name="unwrap">Rouvre l'enveloppe calculée ; elle n'est écrite que si elle redonne la clé.</param>
    private static void Replace(KeyStatsDb db, KeySession session, Func<byte[], KeyStoreEntry> wrap,
        Func<KeyStoreEntry, byte[]?>? unwrap = null)
    {
        ArgumentNullException.ThrowIfNull(db);
        ArgumentNullException.ThrowIfNull(session);
        byte[] pkcs8 = session.Use(key => key.ExportPkcs8());
        KeyStoreEntry entry;
        try
        {
            entry = wrap(pkcs8);
            if (unwrap is not null && !Opens(unwrap(entry), pkcs8))
                throw new ArgumentException("Enveloppe Windows Hello illisible.", nameof(unwrap));
        }
        finally
        {
            CryptographicOperations.ZeroMemory(pkcs8);
        }
        using var tx = db.BeginTransaction();
        db.DeleteKeyStore(entry.Kind);
        db.AddKeyStore(entry);
        tx.Commit();
    }

    private static bool Opens(byte[]? opened, byte[] pkcs8)
    {
        if (opened is null) return false;
        try
        {
            return CryptographicOperations.FixedTimeEquals(opened, pkcs8);
        }
        finally
        {
            CryptographicOperations.ZeroMemory(opened);
        }
    }
}
