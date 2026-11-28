using System.Text;
using System.Text.Json;
using KeyStats.Core.Analysis;
using KeyStats.Core.Crypto;
using KeyStats.Core.Focus;
using KeyStats.Core.Recording;
using KeyStats.Core.Settings;
using KeyStats.Core.Stats;
using KeyStats.Core.Storage;
using KeyStats.Core.Web;

namespace KeyStats.Core.Tests;

public sealed class RecomputeApiTests : IDisposable
{
    private const string Phrase = "cheval agrafe batterie";
    private const string Text = "bonjour tout le monde, ceci est un essai de frappe assez long ";

    // 27 novembre 2026, midi UTC.
    private static readonly long Now = new DateTimeOffset(2026, 11, 27, 12, 0, 0, TimeSpan.Zero).ToUnixTimeMilliseconds();
    private readonly TempDb _tmp = new();
    private readonly KeySession _session = new();
    private readonly WebRouter _router = new(Path.GetTempPath());
    private readonly RecomputeJob _job;

    public RecomputeApiTests()
    {
        _job = new RecomputeJob(_tmp.DbPath, _session, () => Now);
        RecomputeApi.Register(_router, new StatsContext(_tmp.Db, () => Now, TimeZoneInfo.Utc), _tmp.DbPath, _session, _job);
    }

    public void Dispose()
    {
        _job.Completion.Wait();
        _session.Dispose();
        _tmp.Dispose();
    }

    /// <summary>Deux phrases séparées de 3 s, le 2 novembre : une session avec l'écart par défaut (5 s).</summary>
    private void Record()
    {
        using var key = KeySetup.TryWrite(_tmp.Db, Phrase, null, 1, 100_000)!;
        var clock = new LocalDayClock(TimeZoneInfo.Utc);
        var rec = new Recorder(_tmp.Db, new RawArchiveWriter(_tmp.Db, key.PublicKey, new TitleHasher(TitleHasher.NewKey())), clock, Typist.Start);
        rec.OnFocus(new FocusChange { Ms = Typist.Start, Exe = "winword.exe", Title = "Brouillon", Flags = FocusState.NewWindow });
        var typist = new Typist(Typist.Start + 10).Type(Text);
        typist.Ms += 3_000;
        typist.Type(Text);
        foreach (var e in typist.Events) rec.OnEvent(e);
        Assert.True(rec.Close(typist.Ms));
        SettingsStore.Save(SettingsStore.PathFor(_tmp.DbPath), new KeyStatsSettings { Analysis = new AnalysisSettings { SessionGapMs = 2_000 } });
    }

    private async Task<WebResponse> Post(string path, string json) =>
        await _router.HandleAsync("POST", WebRouter.Origin + path, Encoding.UTF8.GetBytes(json));

    private async Task<JsonElement> Start(string from, string to)
    {
        var response = await Post(RecomputeApi.Path, $$"""{"from":"{{from}}","to":"{{to}}"}""");
        Assert.Equal(200, response.Status);
        return JsonDocument.Parse(response.Body).RootElement;
    }

    private JsonElement Status() => JsonDocument.Parse(_router.Handle("GET", WebRouter.Origin + RecomputeApi.Path).Body).RootElement;

    [Theory]
    [InlineData("""{"from":"2026-11-02","to":"2026-11-27"}""")]
    [InlineData("""{"from":"2026-11-05","to":"2026-11-02"}""")]
    [InlineData("""{"from":"2026-11-02"}""")]
    [InlineData("""{"from":"02/11/2026","to":"2026-11-03"}""")]
    [InlineData("pas du json")]
    public async Task Periode_invalide_ou_jusqu_a_aujourd_hui_refusee(string json)
    {
        Assert.Equal(400, (await Post(RecomputeApi.Path, json)).Status);
        Assert.Equal("idle", Status().GetProperty("state").GetString());
    }

    [Fact]
    public async Task Session_verrouillee_demande_le_deverrouillage()
    {
        Record();
        var start = await Start("2026-11-02", "2026-11-02");
        Assert.Equal("locked", start.GetProperty("outcome").GetString());
        Assert.Equal("idle", start.GetProperty("job").GetProperty("state").GetString());
    }

    [Fact]
    public async Task Seuils_de_settings_json_appliques_a_la_periode()
    {
        Record();
        Assert.True(_session.TryUnlockWithPassphrase(_tmp.Db, Phrase));
        Assert.Equal(1, _tmp.Count("SELECT COUNT(*) FROM session"));

        Assert.Equal("started", (await Start("2026-11-01", "2026-11-03")).GetProperty("outcome").GetString());
        await _job.Completion;

        var status = Status();
        Assert.Equal("done", status.GetProperty("state").GetString());
        // Période ramenée au premier jour enregistré, le 2.
        Assert.Equal(2, status.GetProperty("done").GetInt32());
        Assert.Equal(2, status.GetProperty("total").GetInt32());
        Assert.Equal(JsonValueKind.Null, status.GetProperty("day").ValueKind);
        Assert.Equal(2, _tmp.Count("SELECT COUNT(*) FROM session"));

        // Un recalcul terminé n'empêche pas d'en lancer un autre.
        Assert.Equal("started", (await Start("2026-11-02", "2026-11-02")).GetProperty("outcome").GetString());
        await _job.Completion;
        Assert.Equal("done", Status().GetProperty("state").GetString());
    }

    [Fact]
    public async Task Un_seul_recalcul_a_la_fois_et_annulation_entre_deux_jours()
    {
        Record();
        Assert.True(_session.TryUnlockWithPassphrase(_tmp.Db, Phrase));
        // Tant que le test tient la clé, le recalcul attend avant son premier jour.
        _session.Use(_ =>
        {
            Assert.Equal("started", Start("2026-11-02", "2026-11-03").GetAwaiter().GetResult().GetProperty("outcome").GetString());
            var busy = Start("2026-11-02", "2026-11-02").GetAwaiter().GetResult();
            Assert.Equal("busy", busy.GetProperty("outcome").GetString());
            Assert.Equal("2026-11-02", busy.GetProperty("job").GetProperty("day").GetString());
            Assert.Equal(200, Post(RecomputeApi.CancelPath, "").GetAwaiter().GetResult().Status);
            return 0;
        });
        await _job.Completion;

        var status = Status();
        Assert.Equal("cancelled", status.GetProperty("state").GetString());
        Assert.Equal(1, status.GetProperty("done").GetInt32());
        Assert.Equal(2, status.GetProperty("total").GetInt32());
    }

    [Fact]
    public async Task Verrouillage_en_cours_de_route_arrete_le_recalcul()
    {
        Record();
        Assert.True(_session.TryUnlockWithPassphrase(_tmp.Db, Phrase));
        // Verrouillée avant de rendre la main : le recalcul trouve la session fermée dès son premier jour.
        _session.Use(_ =>
        {
            Assert.Equal("started", Start("2026-11-02", "2026-11-03").GetAwaiter().GetResult().GetProperty("outcome").GetString());
            _session.Lock();
            return 0;
        });
        await _job.Completion;

        var status = Status();
        Assert.Equal("locked", status.GetProperty("state").GetString());
        Assert.Equal(0, status.GetProperty("done").GetInt32());
        Assert.Equal(1, _tmp.Count("SELECT COUNT(*) FROM session"));
    }
}
