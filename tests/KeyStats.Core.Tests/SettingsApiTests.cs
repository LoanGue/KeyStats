using System.Text;
using System.Text.Json;
using KeyStats.Core.Analysis;
using KeyStats.Core.Settings;
using KeyStats.Core.Web;

namespace KeyStats.Core.Tests;

public sealed class SettingsApiTests : IDisposable
{
    private readonly string _dir = Directory.CreateTempSubdirectory("keystats-settings-api-").FullName;

    private string Path_ => Path.Combine(_dir, SettingsStore.FileName);

    public void Dispose() => Directory.Delete(_dir, recursive: true);

    private sealed class FakeAgent : IAgentControl
    {
        public int Reloads;
        public int Restarts;

        public Task<bool> ReloadAsync()
        {
            Reloads++;
            return Task.FromResult(true);
        }

        public Task<bool> RestartAsync()
        {
            Restarts++;
            return Task.FromResult(true);
        }
    }

    private static SettingsPatch Patch(int? active = null, int? session = null, string[]? apps = null, bool? live = null) =>
        new(active, session, apps, live);

    [Fact]
    public void Fichier_absent_vue_par_defaut_avec_liste_integree()
    {
        var view = SettingsApi.View(SettingsStore.Load(Path_));
        Assert.True(view.Readable);
        Assert.Equal(view.Defaults, view.Thresholds);
        Assert.Equal(Focus.ExclusionList.Defaults, view.ExcludedApps);
        Assert.True(view.LiveCounter);
    }

    [Fact]
    public void Seuils_enregistres_demandent_un_redemarrage_et_gardent_les_autres_cles()
    {
        File.WriteAllText(Path_, """{ "backupFolder": "D:\\Sauvegardes", "analysis": { "minSpaces": 5 } }""");
        var (outcome, restart, view) = SettingsApi.Save(Path_, Patch(active: 1500, session: 9000));
        Assert.Equal("saved", outcome);
        Assert.True(restart);
        Assert.Equal(new Thresholds(1500, 9000), view.Thresholds);
        var load = SettingsStore.Load(Path_);
        Assert.Equal(@"D:\Sauvegardes", load.Settings.BackupFolder);
        Assert.Equal(5, load.Settings.Analysis.MinSpaces);
        Assert.Equal(9000, load.Settings.Analysis.SessionGapMs);
    }

    [Fact]
    public void Cles_inconnues_et_commentaires_disparaissent_a_l_enregistrement()
    {
        File.WriteAllText(Path_, "{ // note\n \"optionFuture\": true }");
        Assert.Equal("saved", SettingsApi.Save(Path_, Patch(active: 1500)).Outcome);
        string text = File.ReadAllText(Path_);
        Assert.DoesNotContain("optionFuture", text, StringComparison.Ordinal);
        Assert.DoesNotContain("note", text, StringComparison.Ordinal);
    }

    [Fact]
    public void Seuils_inchanges_ou_liste_seule_pas_de_redemarrage()
    {
        var d = SettingsApi.View(SettingsStore.Load(Path_)).Defaults;
        Assert.False(SettingsApi.Save(Path_, Patch(active: d.ActiveGapMs, session: d.SessionGapMs)).Restart);
        var (outcome, restart, view) = SettingsApi.Save(Path_, Patch(apps: [" KeePassXC.exe ", "keepassxc.EXE", "mstsc.exe"]));
        Assert.Equal("saved", outcome);
        Assert.False(restart);
        Assert.Equal(["KeePassXC.exe", "mstsc.exe"], view.ExcludedApps);
    }

    [Theory]
    [InlineData(0, 5000)]
    [InlineData(6000, 5000)]
    [InlineData(1000, 600_001)]
    public void Seuils_hors_bornes_rien_n_est_ecrit(int active, int session)
    {
        var (outcome, _, _) = SettingsApi.Save(Path_, Patch(active: active, session: session));
        Assert.Equal("invalid", outcome);
        Assert.False(File.Exists(Path_));
    }

    [Theory]
    [InlineData("notepad.txt")]
    [InlineData(@"C:\Windows\notepad.exe")]
    [InlineData("../notepad.exe")]
    [InlineData(".exe")]
    [InlineData("")]
    [InlineData("a|b.exe")]
    public void Nom_d_application_refuse(string name)
    {
        Assert.Null(SettingsApi.CleanApps([name]));
        Assert.Equal("invalid", SettingsApi.Save(Path_, Patch(apps: [name])).Outcome);
        Assert.False(File.Exists(Path_));
    }

    [Fact]
    public void Liste_trop_longue_refusee()
    {
        var apps = Enumerable.Range(0, SettingsApi.MaxExcludedApps + 1).Select(i => $"app{i}.exe").ToArray();
        Assert.Null(SettingsApi.CleanApps(apps));
    }

    [Fact]
    public void Fichier_refuse_laisse_tel_quel()
    {
        const string text = """{ "analysis": { "sessionGapMs": -1 } }""";
        File.WriteAllText(Path_, text);
        var (outcome, _, view) = SettingsApi.Save(Path_, Patch(live: false));
        Assert.Equal("unreadable", outcome);
        Assert.False(view.Readable);
        Assert.Equal(text, File.ReadAllText(Path_));
    }

    [Fact]
    public async Task Ecriture_impossible_echec_sans_appel_a_l_agent()
    {
        File.WriteAllText(Path_, "{}");
        // Windows refuses to replace a read-only file; on Linux the rename would still succeed.
        File.SetAttributes(Path_, FileAttributes.ReadOnly);
        try
        {
            var router = new WebRouter(_dir);
            var agent = new FakeAgent();
            SettingsApi.Register(router, Path_, agent);
            var response = await router.HandleAsync("POST", WebRouter.Origin + SettingsApi.Path, """{ "activeGapMs": 1500 }"""u8.ToArray());
            var root = JsonDocument.Parse(response.Body).RootElement;
            Assert.Equal(SettingsOutcome.Failed, root.GetProperty("outcome").GetString());
            Assert.Equal(AnalysisSettings.Default.ActiveGapMs, root.GetProperty("settings").GetProperty("thresholds").GetProperty("activeGapMs").GetInt32());
            Assert.Equal((0, 0), (agent.Reloads, agent.Restarts));
            Assert.Equal("{}", File.ReadAllText(Path_));
        }
        finally
        {
            File.SetAttributes(Path_, FileAttributes.Normal);
        }
    }

    [Fact]
    public async Task Route_post_relance_ou_relit_l_agent_selon_le_changement()
    {
        var router = new WebRouter(_dir);
        var agent = new FakeAgent();
        SettingsApi.Register(router, Path_, agent);

        async Task<JsonElement> Post(string json)
        {
            var response = await router.HandleAsync("POST", WebRouter.Origin + SettingsApi.Path, Encoding.UTF8.GetBytes(json));
            Assert.Equal(200, response.Status);
            return JsonDocument.Parse(response.Body).RootElement;
        }

        var saved = await Post("""{ "liveCounter": false }""");
        Assert.Equal("saved", saved.GetProperty("outcome").GetString());
        Assert.True(saved.GetProperty("agentApplied").GetBoolean());
        Assert.False(saved.GetProperty("restarted").GetBoolean());
        Assert.Equal((1, 0), (agent.Reloads, agent.Restarts));

        Assert.True((await Post("""{ "activeGapMs": 1234 }""")).GetProperty("restarted").GetBoolean());
        Assert.Equal((1, 1), (agent.Reloads, agent.Restarts));

        var refused = await Post("""{ "excludedApps": ["C:\\x.exe"] }""");
        Assert.Equal("invalid", refused.GetProperty("outcome").GetString());
        Assert.False(refused.GetProperty("agentApplied").GetBoolean());
        Assert.Equal((1, 1), (agent.Reloads, agent.Restarts));

        var bad = await router.HandleAsync("POST", WebRouter.Origin + SettingsApi.Path, "{ pas du json"u8.ToArray());
        Assert.Equal(400, bad.Status);
        Assert.Equal(400, (await router.HandleAsync("POST", WebRouter.Origin + SettingsApi.Path, "null"u8.ToArray())).Status);
        var get = router.Handle("GET", WebRouter.Origin + SettingsApi.Path);
        Assert.False(JsonDocument.Parse(get.Body).RootElement.GetProperty("liveCounter").GetBoolean());
    }
}
