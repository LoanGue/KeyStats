using System.Text;
using KeyStats.Core.Crypto;
using KeyStats.Core.Storage;
using KeyStats.Core.Web;

namespace KeyStats.Core.Tests;

public sealed class KeySetupTests : IDisposable
{
    private const string Phrase = "cheval agrafe batterie";
    private const int Iterations = 100_000; // minimum accepté par KeyWrap
    private static readonly byte[] Signature = [.. Enumerable.Range(0, 256).Select(i => (byte)(i * 3))];

    private readonly TempDb _temp = new();

    /// <summary>Hello simulé : même signature pour la création et pour les déverrouillages suivants.</summary>
    private sealed class FakeHello(byte[]? signature) : IHelloEnroller, IHelloSigner
    {
        public List<(string Id, byte[] Challenge)> Created { get; } = [];

        public Task<byte[]?> CreateAndSignAsync(string credentialId, byte[] challenge)
        {
            Created.Add((credentialId, challenge));
            return Task.FromResult(signature?.ToArray());
        }

        public Task<byte[]?> SignAsync(string credentialId, byte[] challenge) => Task.FromResult(signature?.ToArray());

        public Task DeleteAsync(string credentialId) => Task.CompletedTask;
    }

    [Fact]
    public async Task Ecrit_la_cle_et_ses_deux_enveloppes()
    {
        var hello = new HelloEnrollment("KeyStats-test", KeyWrap.NewHelloChallenge(), Signature.ToArray());
        using var key = KeySetup.TryWrite(_temp.Db, Phrase, hello, 5, Iterations);
        Assert.NotNull(key);
        Assert.Equal(key.PublicKey, _temp.Db.GetMeta(MetaKeys.MasterPublicKey));
        var entries = _temp.Db.GetKeyStore();
        Assert.Equal([KeyStoreKind.Passphrase, KeyStoreKind.WindowsHello], entries.Select(e => e.Kind));
        Assert.Equal("KeyStats-test", entries[1].CredentialId);

        using var session = new KeySession();
        Assert.True(session.TryUnlockWithPassphrase(_temp.Db, Phrase));
        session.Lock();
        Assert.True(await session.TryUnlockWithHelloAsync(_temp.Db, new FakeHello(Signature)));
    }

    [Fact]
    public void Deja_configure_rien_n_est_remplace()
    {
        using var first = KeySetup.TryWrite(_temp.Db, Phrase, null, 1, Iterations);
        Assert.Null(KeySetup.TryWrite(_temp.Db, "une autre phrase longue", null, 2, Iterations));
        Assert.Equal(first!.PublicKey, _temp.Db.GetMeta(MetaKeys.MasterPublicKey));
        Assert.Single(_temp.Db.GetKeyStore());
    }

    [Fact]
    public void Phrase_courte_ou_signature_inutilisable_n_ecrit_rien()
    {
        // 11 caractères une fois les espaces réduits.
        Assert.False(KeySetup.IsAcceptable("  abc   defg hi "));
        Assert.True(KeySetup.IsAcceptable("abcd efgh ijk"));
        Assert.Throws<ArgumentException>(() => KeySetup.TryWrite(_temp.Db, "trop court", null, 1, Iterations));
        var bad = new HelloEnrollment("KeyStats-test", KeyWrap.NewHelloChallenge(), new byte[16]);
        Assert.Throws<ArgumentException>(() => KeySetup.TryWrite(_temp.Db, Phrase, bad, 1, Iterations));
        Assert.False(KeySetup.IsSetUp(_temp.Db));
        Assert.Empty(_temp.Db.GetKeyStore());
    }

    [Fact]
    public async Task Route_de_l_assistant()
    {
        using var session = new KeySession();
        int restarts = 0;
        var hello = new FakeHello(null);
        var router = new WebRouter(Path.GetTempPath());
        SetupApi.Register(router, _temp.Db, session, hello, () => Task.FromResult(++restarts > 0), () => 7, Iterations);
        string url = WebRouter.Origin + SetupApi.Path;

        Assert.Equal(400, (await Post(router, url, "{")).Status);
        Assert.Equal(400, (await Post(router, url, """{"hello":false}""")).Status);
        Assert.Equal("""{"outcome":"phrase","agentRestarted":false}""", Body(await Post(router, url, """{"passphrase":"court"}""")));
        Assert.Equal("""{"outcome":"hello","agentRestarted":false}""",
            Body(await Post(router, url, """{"passphrase":"cheval agrafe batterie","hello":true}""")));
        Assert.StartsWith("KeyStats-", Assert.Single(hello.Created).Id);
        Assert.False(KeySetup.IsSetUp(_temp.Db));
        Assert.Equal(0, restarts);

        Assert.Equal("""{"outcome":"done","agentRestarted":true}""",
            Body(await Post(router, url, """{"passphrase":"cheval agrafe batterie"}""")));
        Assert.True(session.IsUnlocked);
        Assert.Equal(_temp.Db.GetMeta(MetaKeys.MasterPublicKey), session.Use(k => k.PublicKey));
        Assert.Equal(1, restarts);
        Assert.Equal("""{"outcome":"already","agentRestarted":false}""",
            Body(await Post(router, url, """{"passphrase":"cheval agrafe batterie"}""")));
        Assert.Equal(1, restarts);
    }

    [Fact]
    public async Task Route_avec_hello_enveloppe_aussi_par_hello()
    {
        using var session = new KeySession();
        var hello = new FakeHello(Signature);
        var router = new WebRouter(Path.GetTempPath());
        SetupApi.Register(router, _temp.Db, session, hello, () => Task.FromResult(false), () => 7, Iterations);
        var response = await Post(router, WebRouter.Origin + SetupApi.Path, """{"passphrase":"cheval agrafe batterie","hello":true}""");

        Assert.Equal("""{"outcome":"done","agentRestarted":false}""", Body(response));
        var entry = _temp.Db.GetKeyStore().Single(e => e.Kind == KeyStoreKind.WindowsHello);
        Assert.Equal(hello.Created[0].Id, entry.CredentialId);
        Assert.Equal(hello.Created[0].Challenge, entry.Salt);
        session.Lock();
        Assert.True(await session.TryUnlockWithHelloAsync(_temp.Db, hello));
    }

    [Fact]
    public async Task Route_signature_hello_inutilisable_repond_hello()
    {
        using var session = new KeySession();
        var router = new WebRouter(Path.GetTempPath());
        SetupApi.Register(router, _temp.Db, session, new FakeHello(new byte[16]), () => Task.FromResult(false), () => 7, Iterations);
        var response = await Post(router, WebRouter.Origin + SetupApi.Path, """{"passphrase":"cheval agrafe batterie","hello":true}""");

        Assert.Equal("""{"outcome":"hello","agentRestarted":false}""", Body(response));
        Assert.False(KeySetup.IsSetUp(_temp.Db));
        Assert.False(session.IsUnlocked);
    }

    private static Task<WebResponse> Post(WebRouter router, string url, string json) =>
        router.HandleAsync("POST", url, Encoding.UTF8.GetBytes(json));

    private static string Body(WebResponse response) => Encoding.UTF8.GetString(response.Body);

    public void Dispose() => _temp.Dispose();
}
