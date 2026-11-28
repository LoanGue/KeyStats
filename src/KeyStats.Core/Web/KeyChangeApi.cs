using System.Security.Cryptography;
using System.Text.Json;
using KeyStats.Core.Crypto;
using KeyStats.Core.Storage;

namespace KeyStats.Core.Web;

/// <param name="Method">Preuve demandée avant le changement : <c>hello</c> ou <c>passphrase</c>.</param>
/// <param name="Passphrase">La phrase actuelle, avec <c>passphrase</c>.</param>
/// <param name="NewPassphrase">La nouvelle phrase.</param>
public sealed record PassphraseChange(string? Method, string? Passphrase, string? NewPassphrase);

/// <param name="Outcome">
/// <c>done</c> ; <c>setup</c> (chiffrement pas encore configuré) ; <c>phrase</c> (nouvelle phrase trop courte) ;
/// <c>proof</c> (phrase actuelle fausse ou Hello refusé) ; <c>locked</c> (session verrouillée) ; <c>hello</c>
/// (création de la clé Hello refusée).
/// </param>
/// <param name="Key">L'état de la clé après la demande.</param>
public sealed record KeyChangeResult(string Outcome, KeyState Key);

/// <summary>
/// Routes de la carte « Chiffrement » : <c>POST /api/key/passphrase</c> remplace la phrase de secours, après
/// la phrase actuelle ou Windows Hello ; <c>POST /api/key/hello</c> crée une nouvelle clé Windows Hello pour
/// la session déverrouillée. Aucune réponse ne contient de phrase ni de clé.
/// </summary>
public static class KeyChangeApi
{
    public const string PassphrasePath = KeyApi.Path + "/passphrase", HelloPath = KeyApi.Path + "/hello";

    public static void Register(WebRouter router, KeyStatsDb db, KeySession session, IHelloSigner signer,
        IHelloEnroller enroller, Func<long> nowMs, int iterations = KeyWrap.PassphraseIterations)
    {
        ArgumentNullException.ThrowIfNull(router);
        router.Post(PassphrasePath, async body =>
        {
            if (Parse(body) is not { NewPassphrase: { } phrase } change
                || change is not ({ Method: "hello" } or { Method: "passphrase", Passphrase: not null }))
                return WebResponse.BadRequest();
            if (!KeySetup.IsSetUp(db)) return Result(db, session, "setup");
            if (!KeySetup.IsAcceptable(phrase)) return Result(db, session, "phrase");
            // La preuve passe par le déverrouillage : la session reste ensuite déverrouillée. Reprise sur le
            // thread de l'interface : la base et la session y sont utilisées.
            bool proven = change.Method == "hello"
                ? await session.TryUnlockWithHelloAsync(db, signer).ConfigureAwait(true)
                : session.TryUnlockWithPassphrase(db, change.Passphrase!);
            if (!proven) return Result(db, session, "proof");
            KeyRewrap.SetPassphrase(db, session, phrase, nowMs(), iterations);
            return Result(db, session, "done");
        });
        router.Post(HelloPath, async _ =>
        {
            if (!KeySetup.IsSetUp(db)) return Result(db, session, "setup");
            if (!session.IsUnlocked) return Result(db, session, "locked");
            string id = KeySetup.NewCredentialId();
            byte[] challenge = KeyWrap.NewHelloChallenge();
            if (await enroller.CreateAndSignAsync(id, challenge).ConfigureAwait(true) is not { } signature)
                return Result(db, session, "hello");
            try
            {
                // Verrouillée pendant l'invite : rien n'est écrit. Reprise sur le thread de l'interface, la
                // session ne peut plus changer d'ici l'écriture.
                if (!session.IsUnlocked) return Result(db, session, "locked");
                KeyRewrap.SetHello(db, session, new HelloEnrollment(id, challenge, signature), nowMs());
            }
            catch (ArgumentException)
            {
                // Signature inutilisable : rien n'a été écrit.
                return Result(db, session, "hello");
            }
            finally
            {
                CryptographicOperations.ZeroMemory(signature);
            }
            return Result(db, session, "done");
        });
    }

    private static WebResponse Result(KeyStatsDb db, KeySession session, string outcome) =>
        WebResponse.Json(new KeyChangeResult(outcome, KeyApi.Read(db, session)), WebJson.Default.KeyChangeResult);

    private static PassphraseChange? Parse(byte[] body)
    {
        try
        {
            return JsonSerializer.Deserialize(body, WebJson.Default.PassphraseChange);
        }
        catch (JsonException)
        {
            return null;
        }
    }
}
