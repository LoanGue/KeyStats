using System.Text;
using KeyStats.Core.Crypto;
using KeyStats.Core.Storage;
using KeyStats.Core.Web;

namespace KeyStats.Core.Tests;

public sealed class KeyChangeTests : IDisposable
{
    private const string Phrase = "cheval agrafe batterie";
    private const string NewPhrase = "lampe girafe parapluie";
    private const int Iterations = 100_000; // minimum accepté par KeyWrap
    private static readonly byte[] Signature = [.. Enumerable.Range(0, 256).Select(i => (byte)(i * 3))];
    private static readonly byte[] OtherSignature = [.. Enumerable.Range(0, 256).Select(i => (byte)(i * 7 + 1))];

    private readonly TempDb _temp = new();
    private readonly KeySession _session = new();
    private readonly WebRouter _router = new(Path.GetTempPath());
    private readonly FakeHello _hello = new(Signature);
    private byte[] _publicKey = [];

    public KeyChangeTests() =>
        KeyChangeApi.Register(_router, _temp.Db, _session, _hello, _hello, () => 9, Iterations);

    /// <summary>Hello simulé : la signature donnée, pour toute clé et tout défi ; null comme une annulation.</summary>
    private sealed class FakeHello(byte[]? signature) : IHelloEnroller, IHelloSigner
    {
        public byte[]? Signature { get; set; } = signature;
        public List<string> Created { get; } = [];
        public Action? DuringPrompt { get; set; }

        public Task<byte[]?> CreateAndSignAsync(string credentialId, byte[] challenge)
        {
            Created.Add(credentialId);
            DuringPrompt?.Invoke();
            return Task.FromResult(Signature?.ToArray());
        }

        public Task<byte[]?> SignAsync(string credentialId, byte[] challenge) => Task.FromResult(Signature?.ToArray());
    }

    private void SetUp(bool withHello)
    {
        var hello = withHello ? new HelloEnrollment("KeyStats-old", KeyWrap.NewHelloChallenge(), Signature.ToArray()) : null;
        using var key = KeySetup.TryWrite(_temp.Db, Phrase, hello, 1, Iterations)!;
        _publicKey = key.PublicKey;
    }

    [Fact]
    public void Nouvelle_phrase_remplace_l_ancienne_sans_changer_la_cle()
    {
        SetUp(withHello: true);
        Assert.Throws<InvalidOperationException>(() => KeyRewrap.SetPassphrase(_temp.Db, _session, NewPhrase, 2, Iterations));
        Assert.True(_session.TryUnlockWithPassphrase(_temp.Db, Phrase));
        Assert.Throws<ArgumentException>(() => KeyRewrap.SetPassphrase(_temp.Db, _session, "trop court", 2, Iterations));

        KeyRewrap.SetPassphrase(_temp.Db, _session, NewPhrase, 2, Iterations);
        Assert.Equal([KeyStoreKind.WindowsHello, KeyStoreKind.Passphrase], _temp.Db.GetKeyStore().Select(e => e.Kind));
        Assert.Equal(_publicKey, _temp.Db.GetMeta(MetaKeys.MasterPublicKey));
        using var other = new KeySession();
        Assert.False(other.TryUnlockWithPassphrase(_temp.Db, Phrase));
        Assert.True(other.TryUnlockWithPassphrase(_temp.Db, NewPhrase));
        Assert.Equal(_publicKey, other.Use(k => k.PublicKey));
    }

    [Fact]
    public async Task Nouvelle_cle_hello_remplace_l_ancienne()
    {
        SetUp(withHello: true);
        Assert.True(_session.TryUnlockWithPassphrase(_temp.Db, Phrase));
        KeyRewrap.SetHello(_temp.Db, _session, new HelloEnrollment("KeyStats-new", KeyWrap.NewHelloChallenge(), OtherSignature.ToArray()), 2);

        var hello = Assert.Single(_temp.Db.GetKeyStore(), e => e.Kind == KeyStoreKind.WindowsHello);
        Assert.Equal("KeyStats-new", hello.CredentialId);
        using var other = new KeySession();
        Assert.False(await other.TryUnlockWithHelloAsync(_temp.Db, new FakeHello(Signature)));
        Assert.True(await other.TryUnlockWithHelloAsync(_temp.Db, new FakeHello(OtherSignature)));
    }

    [Fact]
    public async Task Route_de_la_phrase()
    {
        string url = WebRouter.Origin + KeyChangeApi.PassphrasePath;
        Assert.Equal("setup", Outcome(await Post(url, Change("passphrase", Phrase, NewPhrase))));
        SetUp(withHello: true);

        Assert.Equal(400, (await Post(url, "{")).Status);
        Assert.Equal(400, (await Post(url, """{"method":"passphrase","newPassphrase":"lampe girafe parapluie"}""")).Status);
        Assert.Equal(400, (await Post(url, """{"method":"autre","newPassphrase":"lampe girafe parapluie"}""")).Status);
        Assert.Equal("phrase", Outcome(await Post(url, Change("passphrase", Phrase, "court"))));
        Assert.Equal("proof", Outcome(await Post(url, Change("passphrase", "mauvaise phrase longue", NewPhrase))));
        _hello.Signature = null;
        Assert.Equal("proof", Outcome(await Post(url, Change("hello", null, NewPhrase))));
        Assert.False(_session.IsUnlocked);
        Assert.True(new KeySession().TryUnlockWithPassphrase(_temp.Db, Phrase));

        var done = Body(await Post(url, Change("passphrase", Phrase, NewPhrase)));
        Assert.Equal("""{"outcome":"done","key":{"setup":true,"unlocked":true,"hello":true,"passphrase":true}}""", done);
        Assert.DoesNotContain("girafe", done, StringComparison.Ordinal);
        _hello.Signature = Signature;
        Assert.Equal("done", Outcome(await Post(url, Change("hello", null, "encore une autre phrase"))));
        using var other = new KeySession();
        Assert.True(other.TryUnlockWithPassphrase(_temp.Db, "encore une autre phrase"));
    }

    [Fact]
    public async Task Route_de_windows_hello()
    {
        string url = WebRouter.Origin + KeyChangeApi.HelloPath;
        Assert.Equal("setup", Outcome(await Post(url, "{}")));
        SetUp(withHello: false);
        Assert.Equal("locked", Outcome(await Post(url, "{}")));
        Assert.Empty(_hello.Created);

        // Session verrouillée pendant l'invite de Windows : rien n'est écrit.
        Assert.True(_session.TryUnlockWithPassphrase(_temp.Db, Phrase));
        _hello.DuringPrompt = _session.Lock;
        Assert.Equal("locked", Outcome(await Post(url, "{}")));
        Assert.DoesNotContain(_temp.Db.GetKeyStore(), e => e.Kind == KeyStoreKind.WindowsHello);
        _hello.DuringPrompt = null;

        Assert.True(_session.TryUnlockWithPassphrase(_temp.Db, Phrase));
        _hello.Signature = null;
        Assert.Equal("hello", Outcome(await Post(url, "{}")));
        _hello.Signature = new byte[16];
        Assert.Equal("hello", Outcome(await Post(url, "{}")));
        Assert.DoesNotContain(_temp.Db.GetKeyStore(), e => e.Kind == KeyStoreKind.WindowsHello);

        _hello.Signature = OtherSignature;
        Assert.Equal("""{"outcome":"done","key":{"setup":true,"unlocked":true,"hello":true,"passphrase":true}}""",
            Body(await Post(url, "{}")));
        var entry = Assert.Single(_temp.Db.GetKeyStore(), e => e.Kind == KeyStoreKind.WindowsHello);
        Assert.Equal(_hello.Created[^1], entry.CredentialId);
        Assert.StartsWith("KeyStats-", entry.CredentialId, StringComparison.Ordinal);
        using var other = new KeySession();
        Assert.True(await other.TryUnlockWithHelloAsync(_temp.Db, _hello));
    }

    private static string Change(string method, string? current, string next) =>
        $$"""{"method":"{{method}}","passphrase":{{(current is null ? "null" : $"\"{current}\"")}},"newPassphrase":"{{next}}"}""";

    private Task<WebResponse> Post(string url, string json) => _router.HandleAsync("POST", url, Encoding.UTF8.GetBytes(json));

    private static string Body(WebResponse response) => Encoding.UTF8.GetString(response.Body);

    private static string Outcome(WebResponse response) =>
        System.Text.Json.JsonDocument.Parse(response.Body).RootElement.GetProperty("outcome").GetString()!;

    public void Dispose()
    {
        _session.Dispose();
        _temp.Dispose();
    }
}
