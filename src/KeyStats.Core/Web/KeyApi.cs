using System.Text.Json;
using KeyStats.Core.Crypto;
using KeyStats.Core.Storage;

namespace KeyStats.Core.Web;

/// <param name="Setup">Clé maîtresse en place.</param>
/// <param name="Unlocked">Clé privée déverrouillée dans cette fenêtre.</param>
/// <param name="Hello">Déverrouillage par Windows Hello possible.</param>
/// <param name="Passphrase">Déverrouillage par la phrase de secours possible.</param>
public sealed record KeyState(bool Setup, bool Unlocked, bool Hello, bool Passphrase);

/// <param name="Method"><c>hello</c> ou <c>passphrase</c>.</param>
public sealed record UnlockRequest(string Method, string? Passphrase);

/// <summary>
/// Routes de la clé maîtresse : <c>GET /api/key</c> (état), <c>POST /api/key/unlock</c> et
/// <c>POST /api/key/lock</c>. Chacune répond par l'état ; un échec de déverrouillage le laisse verrouillé.
/// </summary>
public static class KeyApi
{
    public const string Path = "/api/key";

    public static void Register(WebRouter router, KeyStatsDb db, KeySession session, IHelloSigner hello)
    {
        ArgumentNullException.ThrowIfNull(router);
        router.Get(Path, () => Respond(db, session));
        router.Post(Path + "/unlock", async body =>
        {
            switch (Parse(body))
            {
                case { Method: "hello" }:
                    // Reprise sur le thread de l'interface : la session y est lue et modifiée.
                    await session.TryUnlockWithHelloAsync(db, hello).ConfigureAwait(true);
                    break;
                case { Method: "passphrase", Passphrase: { } phrase }:
                    session.TryUnlockWithPassphrase(db, phrase);
                    break;
                default:
                    return WebResponse.BadRequest();
            }
            return Respond(db, session);
        });
        router.Post(Path + "/lock", _ =>
        {
            session.Lock();
            return Task.FromResult(Respond(db, session));
        });
    }

    public static KeyState Read(KeyStatsDb db, KeySession session)
    {
        ArgumentNullException.ThrowIfNull(db);
        ArgumentNullException.ThrowIfNull(session);
        var kinds = db.GetKeyStore().Select(e => e.Kind).ToHashSet();
        return new KeyState(db.GetMeta(MetaKeys.MasterPublicKey) is { Length: > 0 }, session.IsUnlocked,
            kinds.Contains(KeyStoreKind.WindowsHello), kinds.Contains(KeyStoreKind.Passphrase));
    }

    private static WebResponse Respond(KeyStatsDb db, KeySession session) =>
        WebResponse.Json(Read(db, session), WebJson.Default.KeyState);

    private static UnlockRequest? Parse(byte[] body)
    {
        try
        {
            return JsonSerializer.Deserialize(body, WebJson.Default.UnlockRequest);
        }
        catch (JsonException)
        {
            return null;
        }
    }
}
