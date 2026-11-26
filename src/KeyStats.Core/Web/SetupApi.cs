using System.Security.Cryptography;
using System.Text.Json;
using KeyStats.Core.Crypto;
using KeyStats.Core.Storage;

namespace KeyStats.Core.Web;

/// <param name="Hello">Activer aussi Windows Hello.</param>
public sealed record SetupRequest(string? Passphrase, bool Hello);

/// <param name="Outcome"><c>done</c>, <c>already</c> (déjà configuré), <c>phrase</c> (trop courte) ou <c>hello</c> (refusé).</param>
/// <param name="AgentRestarted">Un agent tourne avec la nouvelle clé publique.</param>
public sealed record SetupResult(string Outcome, bool AgentRestarted);

/// <summary>
/// <c>POST /api/setup</c> : l'assistant de première installation. En cas de succès, la clé reste
/// déverrouillée dans la session, et l'agent est relancé pour lire <c>master_pub</c>.
/// </summary>
public static class SetupApi
{
    public const string Path = "/api/setup";

    /// <param name="restartAgent">Relance l'agent de cette base ; vrai si un agent tourne ensuite.</param>
    public static void Register(WebRouter router, KeyStatsDb db, KeySession session, IHelloEnroller enroller,
        Func<Task<bool>> restartAgent, Func<long> nowMs, int iterations = KeyWrap.PassphraseIterations)
    {
        ArgumentNullException.ThrowIfNull(router);
        router.Post(Path, async body =>
        {
            if (Parse(body) is not { Passphrase: { } phrase } request) return WebResponse.BadRequest();
            if (KeySetup.IsSetUp(db)) return Result("already");
            if (!KeySetup.IsAcceptable(phrase)) return Result("phrase");
            HelloEnrollment? hello = null;
            if (request.Hello)
            {
                string id = KeySetup.NewCredentialId();
                byte[] challenge = KeyWrap.NewHelloChallenge();
                // Reprise sur le thread de l'interface : la base et la session y sont utilisées.
                if (await enroller.CreateAndSignAsync(id, challenge).ConfigureAwait(true) is not { } signature)
                    return Result("hello");
                hello = new HelloEnrollment(id, challenge, signature);
            }
            MasterKey? key;
            try
            {
                key = KeySetup.TryWrite(db, phrase, hello, nowMs(), iterations);
            }
            catch (ArgumentException) when (hello is not null)
            {
                // Signature Hello inutilisable (trop courte) : rien n'a été écrit. La clé Hello créée
                // reste dans le magasin de Windows, rattachée à rien (comme après un « already »).
                return Result("hello");
            }
            finally
            {
                if (hello is not null) CryptographicOperations.ZeroMemory(hello.Signature);
            }
            if (key is null) return Result("already");
            session.Adopt(key);
            return Result("done", await restartAgent().ConfigureAwait(true));
        });
    }

    private static WebResponse Result(string outcome, bool restarted = false) =>
        WebResponse.Json(new SetupResult(outcome, restarted), WebJson.Default.SetupResult);

    private static SetupRequest? Parse(byte[] body)
    {
        try
        {
            return JsonSerializer.Deserialize(body, WebJson.Default.SetupRequest);
        }
        catch (JsonException)
        {
            return null;
        }
    }
}
