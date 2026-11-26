using System.Text;
using KeyStats.Core.Storage;
using KeyStats.Core.Web;

namespace KeyStats.Core.Tests;

public sealed class WebRouterTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "keystats-tests", Guid.NewGuid().ToString("N"));
    private readonly string _root;

    public WebRouterTests()
    {
        _root = Path.Combine(_dir, "wwwroot");
        Directory.CreateDirectory(Path.Combine(_root, "css"));
        File.WriteAllText(Path.Combine(_root, "index.html"), "<h1>accueil</h1>");
        File.WriteAllText(Path.Combine(_root, "css", "app.css"), "body{}");
        File.WriteAllText(Path.Combine(_root, "notes.txt"), "pas servi");
        File.WriteAllText(Path.Combine(_dir, "secret.html"), "hors racine");
    }

    private WebRouter Router() => new(_root);

    private static string Body(WebResponse response) => Encoding.UTF8.GetString(response.Body);

    [Fact]
    public void Racine_sert_index_html()
    {
        var response = Router().Handle("GET", WebRouter.Origin + "/");
        Assert.Equal(200, response.Status);
        Assert.Equal("text/html; charset=utf-8", response.ContentType);
        Assert.Equal("<h1>accueil</h1>", Body(response));
    }

    [Fact]
    public void Fichier_d_un_sous_dossier_avec_son_type()
    {
        var response = Router().Handle("GET", WebRouter.Origin + "/css/app.css?v=2");
        Assert.Equal(200, response.Status);
        Assert.Equal("text/css; charset=utf-8", response.ContentType);
    }

    [Theory]
    [InlineData("/%2e%2e/secret.html")]
    [InlineData("/css%5c..%5c..%5csecret.html")]
    [InlineData("/C:%5cWindows%5cwin.ini")]
    [InlineData("/notes.txt")]
    [InlineData("/css")]
    public void Chemin_hors_racine_ou_inconnu_introuvable(string path)
    {
        var response = Router().Handle("GET", WebRouter.Origin + path);
        Assert.Equal(404, response.Status);
        Assert.DoesNotContain("hors racine", Body(response));
    }

    // Chemins bruts, sans normalisation d'URI : chaque garde-fou de Resolve est vérifié seul,
    // y compris quand le chemin retomberait dans la racine.
    [Theory]
    [InlineData("/css/../index.html")]
    [InlineData("/./index.html")]
    [InlineData("/css\\..\\index.html")]
    [InlineData("/../secret.html")]
    [InlineData("/css/../../secret.html")]
    [InlineData("/..\\secret.html")]
    [InlineData("/C:/Windows/win.ini")]
    [InlineData("iindex.html")]
    [InlineData("/index.html\0")]
    public void Resolve_refuse_les_chemins_detournes(string path) =>
        Assert.Null(StaticFiles.Resolve(_root, path));

    [Fact]
    public void Resolve_trouve_un_fichier_de_la_racine() =>
        Assert.Equal(Path.Combine(_root, "css", "app.css"), StaticFiles.Resolve(_root, "/css/app.css"));

    [Theory]
    [InlineData("https://example.com/")]
    [InlineData("http://keystats.app/")]
    [InlineData("https://keystats.app:8443/")]
    [InlineData("pas une adresse")]
    [InlineData("https://keystats.app.evil/")]
    [InlineData("https://user@keystats.app/")]
    public void Autre_origine_introuvable(string uri)
    {
        Assert.Equal(404, Router().Handle("GET", uri).Status);
        Assert.False(WebRouter.IsOwn(uri));
    }

    [Fact]
    public void Fichier_illisible_donne_une_reponse_et_non_une_exception()
    {
        using var locked = new FileStream(Path.Combine(_root, "index.html"), FileMode.Open, FileAccess.Read, FileShare.None);
        Assert.True(WebRouter.IsOwn(WebRouter.Origin + "/"));
        Assert.Equal(500, Router().Handle("GET", WebRouter.Origin + "/").Status);
    }

    [Fact]
    public void Route_api_appelee_et_methode_controlee()
    {
        var router = Router();
        int calls = 0;
        router.Get("/api/test", () =>
        {
            calls++;
            return WebResponse.Json(new DashboardStatus(true, false), WebJson.Default.DashboardStatus);
        });
        Assert.Equal(200, router.Handle("get", WebRouter.Origin + "/api/test").Status);
        Assert.Equal(405, router.Handle("POST", WebRouter.Origin + "/api/test").Status);
        Assert.Equal(404, router.Handle("GET", WebRouter.Origin + "/api/autre").Status);
        Assert.Equal(405, router.Handle("POST", WebRouter.Origin + "/index.html").Status);
        Assert.Equal(1, calls);
    }

    [Fact]
    public void Exception_d_une_route_donne_un_message_fixe()
    {
        var router = Router();
        router.Get("/api/boum", () => throw new InvalidOperationException("texte tapé confidentiel"));
        var response = router.Handle("GET", WebRouter.Origin + "/api/boum");
        Assert.Equal(500, response.Status);
        Assert.Equal("Erreur interne", Body(response));
    }

    [Fact]
    public void Etat_en_json_camel_case()
    {
        using var temp = new TempDb();
        Assert.Equal("""{"setup":false,"agentRunning":true}""", Body(StatusApi.Respond(temp.Db, agentRunning: true)));
        temp.Db.SetMeta(MetaKeys.MasterPublicKey, [1, 2, 3]);
        var status = StatusApi.Read(temp.Db, agentRunning: false);
        Assert.True(status.Setup);
        Assert.False(status.AgentRunning);
        Assert.Equal("application/json; charset=utf-8", StatusApi.Respond(temp.Db, false).ContentType);
    }

    public void Dispose()
    {
        try
        {
            Directory.Delete(_dir, recursive: true);
        }
        catch (IOException)
        {
            // Dossier temporaire nettoyé plus tard.
        }
    }
}
