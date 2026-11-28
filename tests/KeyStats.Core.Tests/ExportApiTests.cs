using System.Text;
using System.Text.Json;
using KeyStats.Core.Stats;
using KeyStats.Core.Web;

namespace KeyStats.Core.Tests;

public sealed class ExportApiTests : IDisposable
{
    // 27 novembre 2026, midi UTC.
    private static readonly long Now = new DateTimeOffset(2026, 11, 27, 12, 0, 0, TimeSpan.Zero).ToUnixTimeMilliseconds();
    private readonly TempDb _tmp = new();
    private readonly string _folder = Directory.CreateTempSubdirectory("keystats-export-api-").FullName;
    private readonly FakePicker _picker = new();
    private readonly WebRouter _router = new(Path.GetTempPath());

    public ExportApiTests()
    {
        _tmp.Db.Exec("INSERT INTO day_metric VALUES (20261101, 0, 1)");
        _picker.Answer = _folder;
        ExportApi.Register(_router, new StatsContext(_tmp.Db, () => Now, TimeZoneInfo.Utc), _tmp.DbPath, _picker, @"C:\Documents");
    }

    public void Dispose()
    {
        _tmp.Dispose();
        Directory.Delete(_folder, recursive: true);
    }

    private sealed class FakePicker : IFolderPicker
    {
        public string? Answer;
        public int Opened;
        public (string Initial, string Title) Last;

        public Task<string?> PickAsync(string initialFolder, string title)
        {
            Opened++;
            Last = (initialFolder, title);
            return Task.FromResult(Answer);
        }
    }

    private async Task<WebResponse> Send(string json) =>
        await _router.HandleAsync("POST", WebRouter.Origin + ExportApi.Path, Encoding.UTF8.GetBytes(json));

    private async Task<JsonElement> Export(string json)
    {
        var response = await Send(json);
        Assert.Equal(200, response.Status);
        return JsonDocument.Parse(response.Body).RootElement;
    }

    private static string[] Files(JsonElement outcome) =>
        [.. outcome.GetProperty("files").EnumerateArray().Select(f => f.GetString()!)];

    [Fact]
    public async Task Tout_l_historique_dans_le_dossier_choisi()
    {
        var outcome = await Export("""{"filters":"period=all","days":true,"sessions":true,"keys":false}""");
        Assert.Equal("done", outcome.GetProperty("outcome").GetString());
        Assert.Equal(_folder, outcome.GetProperty("folder").GetString());
        Assert.Equal(["keystats-jours-2026-11-01_2026-11-27.csv", "keystats-sessions-2026-11-01_2026-11-27.csv"], Files(outcome));
        Assert.Equal(Files(outcome).Order(), Directory.GetFiles(_folder).Select(Path.GetFileName).Order());
        Assert.Equal((@"C:\Documents", "Dossier de l'export CSV"), _picker.Last);
    }

    [Fact]
    public async Task Periode_des_filtres_ou_personnalisee()
    {
        var week = await Export("""{"filters":"period=week&kind=prose","days":true}""");
        Assert.Equal(["keystats-jours-2026-11-21_2026-11-27.csv"], Files(week));
        var custom = await Export("""{"filters":"period=week","from":"2026-11-03","to":"2026-11-05","keys":true}""");
        Assert.Equal(["keystats-touches-2026-11-03_2026-11-05.csv"], Files(custom));
    }

    [Theory]
    [InlineData("""{"filters":"period=all"}""")] // aucun fichier coché
    [InlineData("""{"filters":"period=decade","days":true}""")]
    [InlineData("""{"filters":"period=all&app=x","days":true}""")]
    [InlineData("""{"filters":"period=all","from":"2026-11-05","to":"2026-11-03","days":true}""")]
    [InlineData("""{"filters":"period=all","from":"2026-11-05","to":"2026-11-28","days":true}""")] // après aujourd'hui
    [InlineData("""{"filters":"period=all","from":"2026-11-05","days":true}""")]
    [InlineData("""{"filters":"period=all","from":"5/11/2026","to":"2026-11-06","days":true}""")]
    [InlineData("""{"days":"oui"}""")]
    [InlineData("pas du JSON")]
    public async Task Demande_invalide_refusee_sans_ouvrir_la_fenetre(string json)
    {
        Assert.Equal(400, (await Send(json)).Status);
        Assert.Equal(0, _picker.Opened);
        Assert.Empty(Directory.GetFiles(_folder));
    }

    [Fact]
    public async Task Fenetre_fermee_sans_choix()
    {
        _picker.Answer = null;
        var outcome = await Export("""{"filters":"period=all","days":true}""");
        Assert.Equal("cancelled", outcome.GetProperty("outcome").GetString());
        Assert.Empty(Files(outcome));
    }

    [Theory]
    [InlineData("relatif")]
    [InlineData(@"C:\keystats-absent-4f1c")]
    public async Task Dossier_inutilisable(string folder)
    {
        _picker.Answer = folder;
        var outcome = await Export("""{"filters":"period=all","days":true}""");
        Assert.Equal("invalid", outcome.GetProperty("outcome").GetString());
    }

    [Fact]
    public async Task Ecriture_impossible_repond_failed_sans_citer_l_erreur()
    {
        // Un dossier à la place du fichier temporaire : l'écriture échoue.
        Directory.CreateDirectory(Path.Combine(_folder, "keystats-jours-2026-11-01_2026-11-27.csv.tmp"));
        var response = await Send("""{"filters":"period=all","days":true}""");
        Assert.Equal("""{"outcome":"failed","folder":null,"files":[]}""", Encoding.UTF8.GetString(response.Body));
    }
}
