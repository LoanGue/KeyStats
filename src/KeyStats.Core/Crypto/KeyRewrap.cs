using System.Security.Cryptography;
using KeyStats.Core.Storage;

namespace KeyStats.Core.Crypto;

/// <summary>
/// Nouvelle enveloppe de la clé maîtresse déverrouillée : la clé elle-même ne change pas, donc le brut déjà
/// chiffré reste lisible et l'agent garde sa clé publique. L'ancienne enveloppe du même type est remplacée et
/// effacée du fichier (<see cref="KeyStoreQueries.ReplaceKeyStore"/>). Les copies de la base faites avant,
/// sauvegarde comprise jusqu'à son prochain passage, s'ouvrent encore avec l'ancienne phrase.
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
    /// <exception cref="ArgumentException">Signature Hello inutilisable.</exception>
    public static void SetHello(KeyStatsDb db, KeySession session, HelloEnrollment hello, long nowMs)
    {
        ArgumentNullException.ThrowIfNull(hello);
        Replace(db, session, pkcs8 => KeyWrap.WrapWithHello(pkcs8, hello.Challenge, hello.Signature, hello.CredentialId, nowMs));
    }

    /// <summary>L'enveloppe est calculée hors transaction (PBKDF2) : l'agent n'attend pas le verrou de la base.</summary>
    private static void Replace(KeyStatsDb db, KeySession session, Func<byte[], KeyStoreEntry> wrap)
    {
        ArgumentNullException.ThrowIfNull(db);
        ArgumentNullException.ThrowIfNull(session);
        byte[] pkcs8 = session.Use(key => key.ExportPkcs8());
        KeyStoreEntry entry;
        try
        {
            entry = wrap(pkcs8);
        }
        finally
        {
            CryptographicOperations.ZeroMemory(pkcs8);
        }
        db.ReplaceKeyStore(entry);
    }
}
