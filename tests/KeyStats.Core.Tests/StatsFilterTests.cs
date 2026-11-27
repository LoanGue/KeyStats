using System.Text;
using KeyStats.Core.Analysis;
using KeyStats.Core.Stats;
using KeyStats.Core.Web;

namespace KeyStats.Core.Tests;

public sealed class StatsFilterTests : IDisposable
{
    private const int Today = 20261127;
    private readonly TempDb _temp = new();

    [Fact]
    public void Parametres_decodes_premiere_valeur_gardee()
    {
        var q = WebQuery.Parse("?period=week&app=3&app=4&note=a%20b+c&vide");
        Assert.Equal("week", q["period"]);
        Assert.Equal("3", q["app"]);
        Assert.Equal("a b c", q["note"]);
        Assert.Equal("", q["vide"]);
        Assert.Null(q["absent"]);
        Assert.Null(WebQuery.Parse("?a=" + new string('x', WebQuery.MaxLength))["a"]);
    }

    [Theory]
    [InlineData("", StatsPeriod.Week, 20261121, 20261114, 20261120)]
    [InlineData("period=day", StatsPeriod.Day, 20261127, 20261126, 20261126)]
    [InlineData("period=month", StatsPeriod.Month, 20261029, 20260929, 20261028)]
    [InlineData("period=year", StatsPeriod.Year, 20251128, 20241128, 20251127)]
    public void Periode_glissante_et_precedente(string query, StatsPeriod period, int from, int prevFrom, int prevTo)
    {
        var f = StatsFilter.Parse(WebQuery.Parse(query), Today, 20200101)!;
        Assert.Equal(period, f.Period);
        Assert.Equal(new DayRange(from, Today), f.Range);
        Assert.Equal(new DayRange(prevFrom, prevTo), f.Previous);
        Assert.Equal(f.Range.Days, f.Previous!.Value.Days);
    }

    [Fact]
    public void Tout_part_du_premier_jour_sans_periode_precedente()
    {
        var f = StatsFilter.Parse(WebQuery.Parse("period=all"), Today, 20260301)!;
        Assert.Equal(new DayRange(20260301, Today), f.Range);
        Assert.Null(f.Previous);
        Assert.Equal(new DayRange(Today, Today), StatsFilter.Parse(WebQuery.Parse("period=all"), Today, null)!.Range);
    }

    [Theory]
    [InlineData("period=decade")]
    [InlineData("kind=poesie")]
    [InlineData("app=0")]
    [InlineData("app=-2")]
    [InlineData("device=1e3")]
    [InlineData("app=99999999999")]
    public void Parametre_invalide_refuse(string query) =>
        Assert.Null(StatsFilter.Parse(WebQuery.Parse(query), Today, null));

    [Fact]
    public void Condition_sur_les_sessions()
    {
        var f = StatsFilter.Parse(WebQuery.Parse("app=3&kind=code&device=2"), Today, null)!;
        Assert.True(f.HasSessionFilter);
        var (sql, values) = f.SessionWhere(f.Range);
        Assert.Equal("day >= $from AND day <= $to AND app_id = $app AND kind = $kind AND device_id = $device", sql);
        Assert.Equal([20261121, Today, 3, (int)SessionKind.Code, 2], values);
        Assert.False(StatsFilter.Parse(WebQuery.Empty, Today, null)!.HasSessionFilter);
    }

    [Fact]
    public void Route_des_filtres()
    {
        var db = _temp.Db;
        int word = db.GetOrAddApp("winword.exe"), code = db.GetOrAddApp("code.exe");
        db.GetOrAddApp("jamais.exe");
        db.GetOrAddDevice([1], "Clavier du portable", 1);
        db.GetOrAddDevice([2], "Souris", 2);
        db.Exec($"INSERT INTO app_hour VALUES (1, {code}, 1, 1, 0, 0), (2, {word}, 1, 1, 0, 0), (3, {word}, 1, 1, 0, 0)");
        db.Exec("INSERT INTO day_metric VALUES (20261030, 1, 5), (20261102, 1, 7)");
        long now = new DateTimeOffset(2026, 11, 27, 9, 0, 0, TimeSpan.Zero).ToUnixTimeMilliseconds();
        var stats = new StatsContext(db, () => now, TimeZoneInfo.Utc);
        var router = new WebRouter(Path.GetTempPath());
        FiltersApi.Register(router, stats);

        var response = router.Handle("GET", WebRouter.Origin + FiltersApi.Path);
        Assert.Equal(
            $$"""{"apps":[{"id":{{code}},"name":"code.exe"},{"id":{{word}},"name":"winword.exe"}],"devices":[{"id":1,"name":"Clavier du portable"}],"firstDay":"2026-10-30","today":"2026-11-27"}""",
            Encoding.UTF8.GetString(response.Body));
        Assert.Equal(new DayRange(20261030, Today), stats.Filter(WebQuery.Parse("period=all"))!.Range);
    }

    [Fact]
    public void Route_recoit_les_parametres_et_refuse_les_filtres_invalides()
    {
        var stats = new StatsContext(_temp.Db, () => 0, TimeZoneInfo.Utc);
        var router = new WebRouter(Path.GetTempPath());
        router.Get("/api/essai", q => stats.Respond(q, f => new FilterOptions([], [], null, StatsFilter.Iso(f.Range.FromDay)), StatsJson.Default.FilterOptions));

        Assert.Contains("\"today\":\"1969-12-26\"", Encoding.UTF8.GetString(router.Handle("GET", WebRouter.Origin + "/api/essai?period=week").Body));
        Assert.Equal(400, router.Handle("GET", WebRouter.Origin + "/api/essai?period=siecle").Status);
    }

    public void Dispose() => _temp.Dispose();
}
