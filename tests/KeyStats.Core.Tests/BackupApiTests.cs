using System.Text;
using System.Text.Json;
using KeyStats.Core.Settings;
using KeyStats.Core.Web;

namespace KeyStats.Core.Tests;

public sealed class BackupApiTests : IDisposable
{
    private readonly TempDb _tmp = new();
    private readonly string _elsewhere = Directory.CreateTempSubdirectory("keystats-backup-api-").FullName;
    private readonly FakePicker _picker = new();
    private readonly FakeAgent _agent = new();
    private readonly WebRouter _router = new(Path.GetTempPath());

    public BackupApiTests() => BackupApi.Register(_router, _tmp.DbPath, _picker, _agent, () => 42_000);

    public void Dispose()
    {
        _tmp.Dispose();
        Directory.Delete(_elsewhere, recursive: true);
    }

    private string DataDir => Path.GetDirectoryName(_tmp.DbPath)!;

    private sealed class FakePicker : IFolderPicker
    {
        public string? Answer;
        public string? Initial;

        public Task<string?> PickAsync(string initialFolder)
        {
            Initial = initialFolder;
            return Task.FromResult(Answer);
        }
    }

    private sealed class FakeAgent : IAgentControl
    {
        public int Reloads;

        public Task<bool> ReloadAsync()
        {
            Reloads++;
            return Task.FromResult(true);
        }

        public Task<bool> RestartAsync() => Task.FromResult(false);
    }

    private async Task<JsonElement> Post(string path)
    {
        var response = await _router.HandleAsync("POST", WebRouter.Origin + path, "{}"u8.ToArray());
        Assert.Equal(200, response.Status);
        return JsonDocument.Parse(response.Body).RootElement;
    }

    private async Task<JsonElement> Get()
    {
        var response = await _router.HandleAsync("GET", WebRouter.Origin + BackupApi.Path, []);
        return JsonDocument.Parse(response.Body).RootElement;
    }

    [Fact]
    public async Task Dossier_par_defaut_a_cote_de_la_base_sans_sauvegarde()
    {
        var view = await Get();
        Assert.Equal(Path.Combine(DataDir, "backup"), view.GetProperty("folder").GetString());
        Assert.False(view.GetProperty("custom").GetBoolean());
        Assert.Equal(JsonValueKind.Null, view.GetProperty("lastMs").ValueKind);
    }

    [Fact]
    public async Task Sauvegarder_maintenant_cree_le_miroir_et_donne_sa_date()
    {
        var result = await Post(BackupApi.RunPath);
        Assert.Equal("done", result.GetProperty("outcome").GetString());
        Assert.Equal(42_000, result.GetProperty("backup").GetProperty("lastMs").GetInt64());
        Assert.True(File.Exists(Path.Combine(DataDir, "backup", "keystats.db")));
    }

    [Fact]
    public async Task Dossier_choisi_est_enregistre_et_l_agent_relit_ses_reglages()
    {
        _picker.Answer = _elsewhere;
        var result = await Post(BackupApi.FolderPath);
        Assert.Equal("saved", result.GetProperty("outcome").GetString());
        Assert.True(result.GetProperty("agentApplied").GetBoolean());
        Assert.Equal(Path.Combine(DataDir, "backup"), _picker.Initial);
        Assert.Equal(_elsewhere, result.GetProperty("backup").GetProperty("folder").GetString());
        Assert.True(result.GetProperty("backup").GetProperty("custom").GetBoolean());
        Assert.Equal(_elsewhere, SettingsStore.Load(SettingsStore.PathFor(_tmp.DbPath)).Settings.BackupFolder);
        Assert.Equal(1, _agent.Reloads);

        Assert.Equal("done", (await Post(BackupApi.RunPath)).GetProperty("outcome").GetString());
        Assert.True(File.Exists(Path.Combine(_elsewhere, "keystats.db")));
    }

    [Fact]
    public async Task Fenetre_fermee_sans_choix_ne_change_rien()
    {
        var result = await Post(BackupApi.FolderPath);
        Assert.Equal("cancelled", result.GetProperty("outcome").GetString());
        Assert.False(File.Exists(SettingsStore.PathFor(_tmp.DbPath)));
        Assert.Equal(0, _agent.Reloads);
    }

    [Theory]
    [InlineData("data")]
    [InlineData("missing")]
    [InlineData("relative")]
    public void Dossier_refuse_sans_rien_ecrire(string kind)
    {
        string folder = kind switch
        {
            // Le miroir y aurait le nom de la base elle-même.
            "data" => DataDir,
            "missing" => Path.Combine(_elsewhere, "absent"),
            _ => "backup",
        };
        string settings = SettingsStore.PathFor(_tmp.DbPath);
        Assert.Equal("invalid", BackupApi.SaveFolder(_tmp.DbPath, settings, folder));
        Assert.False(File.Exists(settings));
    }

    [Fact]
    public void Reglages_illisibles_laisses_tels_quels()
    {
        string settings = SettingsStore.PathFor(_tmp.DbPath);
        File.WriteAllText(settings, "{ pas du json");
        Assert.Equal("unreadable", BackupApi.SaveFolder(_tmp.DbPath, settings, _elsewhere));
        Assert.Equal("{ pas du json", File.ReadAllText(settings, Encoding.UTF8));
    }
}
