using System.Text;
using KeyStats.Core.Analysis;
using KeyStats.Core.Storage;
using KeyStats.Core.Stats;
using KeyStats.Core.Web;

namespace KeyStats.Core.Tests;

public sealed class ActivityTimelineTests : IDisposable
{
    private const int Today = 20261127;
    private const long Minute = 60_000;
    private readonly TempDb _temp = new();
    private static readonly LocalDayClock Utc = new(TimeZoneInfo.Utc);

    public void Dispose() => _temp.Dispose();

    private static long At(int day, int hour, int minute) =>
        new DateTimeOffset(LocalDayClock.DateOf(day).AddHours(hour).AddMinutes(minute), TimeSpan.Zero).ToUnixTimeMilliseconds();

    private void AddMinute(long ms, int keys, long writingMs) =>
        _temp.Db.Exec($"INSERT INTO minute_total VALUES ({ms / Minute}, {keys}, {keys}, 0, 0, {writingMs}, 0)");

    private void AddSessionDay(int day) =>
        _temp.Db.Exec($"""
            INSERT INTO session (day, start_ms, end_ms, app_id, title_id, device_id, kind, chars, typed, deleted, keys, text_keys, repeats,
                active_ms, gross_cpm, net_cpm, real_cpm, peak_cpm, typos, collateral, corrections, unanalyzed, retyped, reformulations, overlaps, kspc)
            VALUES ({day}, {At(day, 9, 0)}, {At(day, 9, 10)}, {_temp.Db.GetOrAddApp("winword.exe")}, NULL, 1, 1, 100, 100, 0, 100, 100, 0,
                60000, 300, 0, 250, NULL, 0, 0, 0, 0, 0, 0, 0, 1)
            """);

    [Fact]
    public void Minutes_frappe_pause_et_non_observe_d_un_jour_passe()
    {
        AddMinute(At(20261126, 9, 0), 120, 40_000);
        AddMinute(At(20261126, 9, 1), 60, 20_000);
        AddMinute(At(20261125, 23, 59), 99, 50_000); // la veille : hors du jour
        // Deux pauses qui se chevauchent sont fusionnées ; une appli exclue reste du temps observé.
        _temp.Db.AddPause(At(20261126, 12, 0), At(20261126, 13, 0), PauseReason.Locked);
        _temp.Db.AddPause(At(20261126, 12, 30), At(20261126, 14, 0), PauseReason.Snooze);
        _temp.Db.AddPause(At(20261126, 15, 0), At(20261126, 16, 0), PauseReason.Excluded);
        _temp.Db.AddPause(At(20261126, 22, 0), At(20261127, 2, 0), PauseReason.Stopped);

        var t = ActivityTimeline.Read(_temp.Db, Utc, 20261126, At(Today, 12, 0), Today);

        Assert.Equal(1440, t.Keys.Count);
        Assert.Equal((120, 60, 0), (t.Keys[540], t.Keys[541], t.Keys[542]));
        Assert.Equal(At(20261127, 0, 0), t.EndMs);
        Assert.Equal(t.EndMs, t.ObservedToMs);
        Assert.Equal([new TimeSpanMs(At(20261126, 12, 0), At(20261126, 14, 0)), new TimeSpanMs(At(20261126, 22, 0), At(20261127, 0, 0))],
            t.Unobserved);
        Assert.Equal(60_000, t.WritingMs);
        Assert.Equal(4 * 60 * Minute, t.UnobservedMs);
        Assert.Equal(20 * 60 * Minute - 60_000, t.PauseMs);
        Assert.Equal(24, t.Hours.Count);
        Assert.Equal(new HourMark(540, 9), t.Hours[9]);
    }

    [Fact]
    public void Aujourd_hui_s_arrete_a_maintenant()
    {
        _temp.Db.AddPause(At(Today, 8, 0), At(Today, 20, 0), PauseReason.Locked);

        var t = ActivityTimeline.Read(_temp.Db, Utc, Today, At(Today, 10, 0), Today);

        Assert.Equal(At(Today, 10, 0), t.ObservedToMs);
        Assert.Equal([new TimeSpanMs(At(Today, 8, 0), At(Today, 10, 0))], t.Unobserved);
        Assert.Equal(8 * 60 * Minute, t.PauseMs);
    }

    [Fact]
    public void Jours_de_changement_d_heure()
    {
        var paris = new LocalDayClock(TimeZoneInfo.FindSystemTimeZoneById("Europe/Paris"));

        var fall = ActivityTimeline.Read(_temp.Db, paris, 20261025, At(Today, 12, 0), Today);
        Assert.Equal(1500, fall.Keys.Count);
        Assert.Equal([0, 1, 2, 2, 3], fall.Hours.Take(5).Select(h => h.Hour));
        Assert.Equal(new HourMark(180, 2), fall.Hours[3]);

        var spring = ActivityTimeline.Read(_temp.Db, paris, 20260329, At(Today, 12, 0), Today);
        Assert.Equal(1380, spring.Keys.Count);
        Assert.Equal([0, 1, 3, 4], spring.Hours.Take(4).Select(h => h.Hour));
    }

    [Fact]
    public void Route_du_jour_par_defaut_voisins_et_requetes_invalides()
    {
        AddSessionDay(20261120);
        AddSessionDay(20261124);
        AddSessionDay(20261126);
        AddSessionDay(20261130); // après aujourd'hui (horloge déréglée) : jamais proposé
        var router = new WebRouter(Path.GetTempPath());
        ActivityApi.Register(router, new StatsContext(_temp.Db, () => At(Today, 12, 0), TimeZoneInfo.Utc), new Crypto.KeySession());
        string Get(string query)
        {
            var r = router.Handle("GET", WebRouter.Origin + ActivityTimeline.Path + query);
            return r.Status == 200 ? Encoding.UTF8.GetString(r.Body) : r.Status.ToString(System.Globalization.CultureInfo.InvariantCulture);
        }

        Assert.Contains("\"day\":\"2026-11-26\",\"prev\":\"2026-11-24\",\"next\":null", Get(""), StringComparison.Ordinal);
        Assert.Contains("\"day\":\"2026-11-22\",\"prev\":\"2026-11-20\",\"next\":\"2026-11-24\"", Get("?day=2026-11-22"), StringComparison.Ordinal);
        Assert.Equal("400", Get("?day=2026-11-28"));
        Assert.Equal("400", Get("?day=27/11/2026"));
    }
}
