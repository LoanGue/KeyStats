using KeyStats.Core.Analysis;
using KeyStats.Core.Stats;
using KeyStats.Core.Web;

namespace KeyStats.Core.Tests;

public sealed class SpeedApiTests : IDisposable
{
    private const int Today = 20261127; // un vendredi
    private readonly TempDb _temp = new();
    private readonly int _word, _code;

    public SpeedApiTests()
    {
        var db = _temp.Db;
        _word = db.GetOrAddApp("winword.exe");
        _code = db.GetOrAddApp("code.exe");
        int keyboard = db.GetOrAddDevice([1], "Clavier", 1);
        // Jour, début, appli, nature, caractères nets, tapés, fautes, temps actif, durée, vitesse brute et réelle (car./min), pointe.
        Add(20261126, 2000, _word, keyboard, SessionKind.Prose, 250, 260, 13, 30_000, 60_000, 300, 250, 320);
        Add(20261125, 1000, _code, keyboard, SessionKind.Code, 150, 160, 0, 60_000, 60_000, 200, 150, null);
        Add(20261118, 500, _word, keyboard, SessionKind.Prose, 500, 500, 25, 100_000, 120_000, 400, 250, 450);
    }

    private void Add(int day, long start, int app, int device, SessionKind kind, int chars, int typed, int typos, long activeMs, long durationMs,
        double gross, double real, double? peak) =>
        _temp.Db.Exec($"""
            INSERT INTO session (day, start_ms, end_ms, app_id, device_id, kind, chars, typed, deleted, keys, text_keys, repeats,
                active_ms, gross_cpm, net_cpm, real_cpm, peak_cpm, typos, collateral, corrections, unanalyzed, retyped, reformulations, overlaps, kspc)
            VALUES ({day}, {start}, {start + durationMs}, {app}, {device}, {(int)kind}, {chars}, {typed}, 0, {typed}, {typed}, 0,
                {activeMs}, {gross}, 0, {real}, {peak?.ToString(System.Globalization.CultureInfo.InvariantCulture) ?? "NULL"},
                {typos}, 0, 0, 0, 0, 0, 0, 1)
            """);

    private Speed Read(string query, int today = Today) => SpeedApi.Read(_temp.Db, StatsFilter.Parse(WebQuery.Parse(query), today, 20261101)!);

    [Fact]
    public void Sessions_de_la_periode_dans_l_ordre_en_mots_par_minute()
    {
        var s = Read("period=week");
        Assert.Equal(("2026-11-21", "2026-11-27", false), (s.From, s.To, s.Truncated));
        Assert.Equal([
            new SpeedSession(1000, _code, 30, 40, null, null, 60_000),
            new SpeedSession(2000, _word, 50, 60, 64, 0.95, 60_000),
        ], s.Sessions);
    }

    [Fact]
    public void Filtre_d_appli_sur_les_sessions_et_les_semaines()
    {
        var s = Read($"period=week&app={_code}");
        Assert.Equal([1000L], s.Sessions.Select(x => x.StartMs));
        Assert.Equal(new SpeedWeek("2026-11-23", true, 40, 30), s.Weeks[^1]);
    }

    [Fact]
    public void Huit_semaines_vitesse_de_frappe_ponderee_par_le_temps_actif()
    {
        var weeks = Read("period=day").Weeks;
        Assert.Equal(SpeedApi.WeekCount, weeks.Count);
        Assert.Equal("2026-10-05", weeks[0].Monday);
        // (300 × 30 s + 200 × 60 s) / 90 s = 233,3 car./min ; 400 car. nets en 2 min.
        Assert.Equal(233.333 / 5, weeks[^1].TypingWpm!.Value, 2);
        Assert.Equal(40, weeks[^1].RealWpm);
        Assert.Equal(new SpeedWeek("2026-11-16", false, 80, 50), weeks[^2]);
        Assert.All(weeks.SkipLast(2), w => Assert.Equal((null, null), (w.TypingWpm, w.RealWpm)));
    }

    [Fact]
    public void Au_dela_du_plafond_les_sessions_les_plus_recentes()
    {
        int device = _temp.Db.GetOrAddDevice([1], "Clavier", 1);
        for (int i = 0; i < SpeedApi.MaxSessions; i++)
            Add(20261127, 10_000 + i, _word, device, SessionKind.Prose, 100, 100, 0, 10_000, 10_000, 600, 600, null);
        var s = Read("period=week");
        Assert.True(s.Truncated);
        Assert.Equal(SpeedApi.MaxSessions, s.Sessions.Count);
        Assert.Equal((10_000L, 10_000L + SpeedApi.MaxSessions - 1), (s.Sessions[0].StartMs, s.Sessions[^1].StartMs));
    }

    public void Dispose() => _temp.Dispose();
}
