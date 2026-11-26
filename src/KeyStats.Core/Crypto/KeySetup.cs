using System.Security.Cryptography;
using KeyStats.Core.Storage;

namespace KeyStats.Core.Crypto;

/// <summary>Création de la clé Windows Hello de KeyStats, fournie par le tableau de bord (WinRT).</summary>
public interface IHelloEnroller
{
    /// <summary>Crée (ou remplace) la clé <paramref name="credentialId"/>, puis lui fait signer le défi.</summary>
    /// <returns>La signature, ou null si l'utilisateur annule ou si Hello n'est pas disponible.</returns>
    Task<byte[]?> CreateAndSignAsync(string credentialId, byte[] challenge);
}

/// <param name="Signature">Effacée par l'appelant une fois la clé enveloppée.</param>
public sealed record HelloEnrollment(string CredentialId, byte[] Challenge, byte[] Signature);

/// <summary>
/// Premier passage de l'assistant : clé maîtresse créée, partie publique écrite dans <c>master_pub</c>,
/// partie privée enveloppée par la phrase de secours et, si demandé, par Windows Hello.
/// </summary>
public static class KeySetup
{
    /// <summary>Longueur minimale de la phrase, une fois normalisée (<see cref="KeyWrap.NormalizePassphrase"/>).</summary>
    public const int MinPassphraseLength = 12;

    public static bool IsSetUp(KeyStatsDb db)
    {
        ArgumentNullException.ThrowIfNull(db);
        return db.GetMeta(MetaKeys.MasterPublicKey) is { Length: > 0 };
    }

    public static bool IsAcceptable(string phrase) =>
        phrase is not null && KeyWrap.NormalizePassphrase(phrase).Length >= MinPassphraseLength;

    /// <summary>Nom propre à cette base : une autre installation ne remplace pas sa clé Hello.</summary>
    public static string NewCredentialId() => "KeyStats-" + Convert.ToHexString(RandomNumberGenerator.GetBytes(8));

    /// <summary>
    /// Écrit la clé maîtresse et ses enveloppes en une seule transaction. Le calcul des enveloppes
    /// (PBKDF2) se fait avant : l'agent, qui écrit dans la même base, n'attend pas le verrou pendant ce temps.
    /// </summary>
    /// <returns>La clé maîtresse, à la charge de l'appelant ; null si le chiffrement était déjà configuré.</returns>
    /// <exception cref="ArgumentException">Phrase trop courte, ou signature Hello inutilisable.</exception>
    public static MasterKey? TryWrite(KeyStatsDb db, string passphrase, HelloEnrollment? hello, long nowMs,
        int iterations = KeyWrap.PassphraseIterations)
    {
        ArgumentNullException.ThrowIfNull(db);
        if (!IsAcceptable(passphrase)) throw new ArgumentException("Phrase de secours trop courte.", nameof(passphrase));
        var master = MasterKey.Generate();
        byte[] pkcs8 = master.ExportPkcs8();
        try
        {
            var byPhrase = KeyWrap.WrapWithPassphrase(pkcs8, passphrase, nowMs, iterations);
            var byHello = hello is null ? null
                : KeyWrap.WrapWithHello(pkcs8, hello.Challenge, hello.Signature, hello.CredentialId, nowMs);
            using (var tx = db.BeginTransaction())
            {
                if (IsSetUp(db))
                {
                    master.Dispose();
                    return null;
                }
                db.SetMeta(MetaKeys.MasterPublicKey, master.PublicKey);
                db.AddKeyStore(byPhrase);
                if (byHello is not null) db.AddKeyStore(byHello);
                tx.Commit();
            }
            return master;
        }
        catch
        {
            master.Dispose();
            throw;
        }
        finally
        {
            CryptographicOperations.ZeroMemory(pkcs8);
        }
    }
}
