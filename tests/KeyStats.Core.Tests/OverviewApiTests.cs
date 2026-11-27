using KeyStats.Core.Analysis;
using KeyStats.Core.Stats;
using KeyStats.Core.Web;

namespace KeyStats.Core.Tests;

public sealed class OverviewApiTests : IDisposable
{
    private const int Today = 20261127; // un vendredi
    private readonly TempDb _temp = new();

    public OverviewApiTests()
    {
        var db = _temp.Db;
        int word = db.GetOrAddApp("winword.exe"), code = db.GetOrAddApp("code.exe");
        int keyboard = db.GetOrAddDevice([1], "Clavier", 1);
        // Jour, appli, nature, caractères nets, tapés, fautes, temps actif, touches ; chaque session dure 1 min par 250 caractères.
        Add(20261126, word, keyboard, SessionKind.Prose, 250, 260, 13, 50_000, 300, 60_000);
        Add(20261125, code, keyboard, SessionKind.Code, 150, 160, 0, 40_000, 200, 60_000);
        Add(20261118, word, keyboard, SessionKind.Prose, 500, 500, 25, 100_000, 600, 120_000);
        db.Exec("INSERT INTO day_metric VALUES (20261126, 1, 1000), (20261118, 1, 700), (20261118, 3, 9)");
    }

    private void Add(int day, int app, int device, SessionKind kind, int chars, int typed, int typos, long activeMs, int keys, long durationMs) =>
        _temp.Db.Exec($"""
            INSERT INTO session (day, start_ms, end_ms, app_id, device_id, kind, chars, typed, deleted, keys, text_keys, repeats,
                active_ms, gross_cpm, net_cpm, real_cpm, typos, collateral, corrections, unanalyzed, retyped, reformulations, overlaps, kspc)
            VALUES ({day}, 0, {durationMs}, {app}, {device}, {(int)kind}, {chars}, {typed}, 0, {keys}, {keys}, 0,
                {activeMs}, 0, 0, 0, {typos}, 0, 0, 0, 0, 0, 0, 1)
            """);

    private Overview Read(string query) => OverviewApi.Read(_temp.Db, StatsFilter.Parse(WebQuery.Parse(query), Today, 20261101)!);

    [Fact]
    public void Chiffres_de_la_semaine_et_de_la_precedente()
    {
        var o = Read("period=week");
        Assert.Equal(("2026-11-21", "2026-11-27"), (o.From, o.To));
        Assert.Equal(new OverviewTotals(1000, false, 2, 80, 90_000, 40, 0.95), o.Current);
        Assert.Equal(new OverviewTotals(700, false, 1, 100, 100_000, 50, 0.95), o.Previous);
    }

    [Fact]
    public void Filtre_de_session_compte_les_touches_des_sessions_et_sans_texte_pas_de_precision()
    {
        var o = Read("period=week&kind=code");
        Assert.Equal(new OverviewTotals(200, true, 1, 30, 40_000, 30, null), o.Current);
        Assert.Equal(new OverviewTotals(0, true, 0, 0, 0, null, null), o.Previous);
    }

    [Fact]
    public void Douze_semaines_du_lundi_la_derniere_en_cours()
    {
        var weeks = Read("period=day").Weeks;
        Assert.Equal(OverviewApi.WeekCount, weeks.Count);
        Assert.Equal("2026-09-07", weeks[0].Monday);
        Assert.Equal(new OverviewWeek("2026-11-23", true, 90_000, 40, 0.95), weeks[^1]);
        Assert.Equal(new OverviewWeek("2026-11-16", false, 100_000, 50, 0.95), weeks[^2]);
        Assert.All(weeks.SkipLast(2), w => Assert.Equal((0L, (double?)null), (w.WritingMs, w.RealWpm)));
    }

    [Fact]
    public void Tout_sans_periode_precedente() => Assert.Null(Read("period=all").Previous);

    public void Dispose() => _temp.Dispose();
}
