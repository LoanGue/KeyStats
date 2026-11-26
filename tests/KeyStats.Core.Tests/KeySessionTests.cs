using System.Text;
using KeyStats.Core.Crypto;
using KeyStats.Core.Storage;
using KeyStats.Core.Web;

namespace KeyStats.Core.Tests;

public sealed class KeySessionTests : IDisposable
{
    private const string Phrase = "cheval agrafe batterie";
    private static readonly byte[] HelloSignature = [.. Enumerable.Range(0, 256).Select(i => (byte)i)];

    private readonly TempDb _temp = new();
    private readonly byte[] _publicKey;

    public KeySessionTests()
    {
        using var master = MasterKey.Generate();
        _publicKey = master.PublicKey;
        byte[] pkcs8 = master.ExportPkcs8();
        _temp.Db.SetMeta(MetaKeys.MasterPublicKey, _publicKey);
        _temp.Db.AddKeyStore(KeyWrap.WrapWithPassphrase(pkcs8, Phrase, 1, 100_000));
        _temp.Db.AddKeyStore(KeyWrap.WrapWithHello(pkcs8, KeyWrap.NewHelloChallenge(), HelloSignature, "cred", 1));
    }

    private sealed class FakeHello(byte[]? signature) : IHelloSigner
    {
        public List<(string Id, byte[] Challenge)> Calls { get; } = [];

        public Task<byte[]?> SignAsync(string credentialId, byte[] challenge)
        {
            Calls.Add((credentialId, challenge));
            return Task.FromResult(signature?.ToArray());
        }
    }

    [Fact]
    public void Phrase_juste_deverrouille_et_lock_efface()
    {
        using var session = new KeySession();
        Assert.False(session.TryUnlockWithPassphrase(_temp.Db, "cheval agrafe"));
        Assert.False(session.IsUnlocked);
        Assert.Throws<InvalidOperationException>(() => session.Key);
        Assert.True(session.TryUnlockWithPassphrase(_temp.Db, "  cheval  agrafe batterie "));
        Assert.Equal(_publicKey, session.Key.PublicKey);
        session.Lock();
        Assert.False(session.IsUnlocked);
    }

    [Fact]
    public async Task Hello_signe_le_defi_stocke()
    {
        using var session = new KeySession();
        var hello = new FakeHello(HelloSignature);
        Assert.True(await session.TryUnlockWithHelloAsync(_temp.Db, hello));
        var entry = _temp.Db.GetKeyStore().Single(e => e.Kind == KeyStoreKind.WindowsHello);
        Assert.Equal("cred", Assert.Single(hello.Calls).Id);
        Assert.Equal(entry.Salt, hello.Calls[0].Challenge);
        Assert.Equal(_publicKey, session.Key.PublicKey);
    }

    [Fact]
    public async Task Hello_annule_ou_autre_signature_reste_verrouille()
    {
        using var session = new KeySession();
        Assert.False(await session.TryUnlockWithHelloAsync(_temp.Db, new FakeHello(null)));
        byte[] other = HelloSignature.ToArray();
        other[0] ^= 1;
        Assert.False(await session.TryUnlockWithHelloAsync(_temp.Db, new FakeHello(other)));
        Assert.False(session.IsUnlocked);
    }

    [Fact]
    public void Cle_d_une_autre_base_refusee()
    {
        using var other = MasterKey.Generate();
        _temp.Db.SetMeta(MetaKeys.MasterPublicKey, other.PublicKey);
        using var session = new KeySession();
        Assert.False(session.TryUnlockWithPassphrase(_temp.Db, Phrase));
        Assert.False(session.IsUnlocked);
    }

    [Fact]
    public async Task Routes_de_la_cle()
    {
        using var session = new KeySession();
        var router = new WebRouter(Path.GetTempPath());
        KeyApi.Register(router, _temp.Db, session, new FakeHello(HelloSignature));
        string url = WebRouter.Origin + KeyApi.Path;

        Assert.Equal("""{"setup":true,"unlocked":false,"hello":true,"passphrase":true}""", Body(router.Handle("GET", url)));
        byte[] body = Encoding.UTF8.GetBytes("""{"method":"passphrase","passphrase":"cheval agrafe batterie"}""");
        Assert.Contains("\"unlocked\":true", Body(await router.HandleAsync("POST", url + "/unlock", body)));
        Assert.All(body, b => Assert.Equal(0, b));
        Assert.Contains("\"unlocked\":false", Body(await router.HandleAsync("POST", url + "/lock", [])));
        Assert.Contains("\"unlocked\":true", Body(await router.HandleAsync("POST", url + "/unlock", Json("""{"method":"hello"}"""))));

        Assert.Equal(400, (await router.HandleAsync("POST", url + "/unlock", Json("{"))).Status);
        Assert.Equal(400, (await router.HandleAsync("POST", url + "/unlock", Json("""{"method":"pin"}"""))).Status);
        Assert.Equal(400, (await router.HandleAsync("POST", url + "/unlock", Json("""{"method":"passphrase"}"""))).Status);
        session.Lock();
        string padded = """{"method":"passphrase","passphrase":"cheval agrafe batterie"}""".PadRight(WebRouter.MaxBody + 1);
        Assert.Equal(400, (await router.HandleAsync("POST", url + "/unlock", Json(padded))).Status);
        Assert.False(session.IsUnlocked);
        Assert.Equal(405, router.Handle("GET", url + "/unlock").Status);
        Assert.Equal(405, (await router.HandleAsync("POST", url, [])).Status);
    }

    [Fact]
    public async Task Exception_d_une_route_post_donne_un_message_fixe()
    {
        var router = new WebRouter(Path.GetTempPath());
        router.Post("/api/boum", _ => throw new InvalidOperationException("texte tapé"));
        var response = await router.HandleAsync("POST", WebRouter.Origin + "/api/boum", []);
        Assert.Equal(500, response.Status);
        Assert.Equal("Erreur interne", Body(response));
    }

    private static byte[] Json(string text) => Encoding.UTF8.GetBytes(text);

    private static string Body(WebResponse response) => Encoding.UTF8.GetString(response.Body);

    public void Dispose() => _temp.Dispose();
}
