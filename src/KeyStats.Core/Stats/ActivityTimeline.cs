using System.Globalization;
using KeyStats.Core.Analysis;
using KeyStats.Core.Storage;
using KeyStats.Core.Web;

namespace KeyStats.Core.Stats;

/// <param name="Minute">Minute du jour, depuis <see cref="DayTimeline.StartMs"/>, où commence l'heure locale.</param>
/// <param name="Hour">Heure locale affichée (0 à 23) ; une heure peut manquer ou revenir aux changements d'heure.</param>
public sealed record HourMark(int Minute, int Hour);

/// <param name="StartMs">Début, en ms UTC.</param>
/// <param name="EndMs">Fin exclue, en ms UTC.</param>
public sealed record TimeSpanMs(long StartMs, long EndMs);

/// <param name="Day">Jour local affiché (aaaa-mm-jj).</param>
/// <param name="Prev">Jour précédent qui a du temps d'écriture ; null s'il n'y en a pas.</param>
/// <param name="Next">Jour suivant qui a du temps d'écriture, jusqu'à aujourd'hui ; null s'il n'y en a pas.</param>
/// <param name="StartMs">Minuit local du jour, en ms UTC.</param>
/// <param name="EndMs">Minuit local du lendemain : le jour dure 1380, 1440 ou 1500 minutes aux changements d'heure.</param>
/// <param name="ObservedToMs">Fin de la partie écoulée du jour : maintenant pour aujourd'hui, <paramref name="EndMs"/> sinon.</param>
/// <param name="Keys">Touches de chaque minute du jour, une case par minute depuis <paramref name="StartMs"/>.</param>
/// <param name="Hours">Début de chaque heure locale du jour.</param>
/// <param name="Unobserved">
/// Périodes où l'agent n'observait pas la frappe (session Windows verrouillée ou en veille, pause par l'icône),
/// terminées, fusionnées et bornées à la partie écoulée du jour. L'agent n'écrit pas encore le temps où il est
/// arrêté : ce temps compte comme pause (#332).
/// </param>
/// <param name="WritingMs">Temps actif d'écriture de la partie écoulée du jour.</param>
/// <param name="PauseMs">
/// Reste du temps observé : écoulé, moins le non observé et le temps d'écriture. L'écriture peut empiéter de quelques
/// secondes sur le non observé (l'écart avant une touche compte dans sa minute) : la pause est alors bornée à 0.
/// </param>
/// <param name="UnobservedMs">Durée totale de <paramref name="Unobserved"/>.</param>
public sealed record DayTimeline(string Day, string? Prev, string? Next, long StartMs, long EndMs, long ObservedToMs,
    IReadOnlyList<int> Keys, IReadOnlyList<HourMark> Hours, IReadOnlyList<TimeSpanMs> Unobserved,
    long WritingMs, long PauseMs, long UnobservedMs);

/// <summary>
/// Route <c>/api/activity/day?day=aaaa-mm-jj</c> : une journée minute par minute. Sans <c>day</c>, le dernier jour
/// écrit jusqu'à aujourd'hui. Tout vient des totaux par minute et de la table <c>pause</c>, qui ne connaissent ni
/// appli, ni clavier, ni nature : la journée ignore les filtres de l'en-tête.
/// </summary>
public static class ActivityTimeline
{
    public const string Path = ActivityApi.Path + "/day";
    private const long MinuteMs = 60_000;

    /// <summary>
    /// Motifs où rien n'est observé ; les autres (admin, appli exclue, mot de passe) restent du temps observé.
    /// <see cref="PauseReason.Stopped"/> n'est pas encore écrit par l'agent (#332).
    /// </summary>
    private static readonly PauseReason[] Blind = [PauseReason.Stopped, PauseReason.Locked, PauseReason.Snooze];

    internal static void Register(WebRouter router, StatsContext stats) =>
        router.Get(Path, q =>
        {
            var clock = stats.Clock;
            long now = stats.NowMs;
            int today = clock.DayKey(now);
            int? day = q["day"] is { } text ? Parse(text) : LastWritten(stats.Db, today) ?? today;
            if (day is null || day > today) return WebResponse.BadRequest();
            return WebResponse.Json(Read(stats.Db, clock, day.Value, now, today),
                StatsJson.Default.DayTimeline);
        });

    public static DayTimeline Read(KeyStatsDb db, LocalDayClock clock, int day, long nowMs, int today)
    {
        ArgumentNullException.ThrowIfNull(db);
        ArgumentNullException.ThrowIfNull(clock);
        DateTime date = LocalDayClock.DateOf(day);
        long start = clock.MidnightUtcMs(date), end = clock.MidnightUtcMs(date.AddDays(1));
        long observedTo = Math.Clamp(nowMs, start, end);
        var keys = new int[(int)((end - start) / MinuteMs)];
        long writing = 0;
        // Jusqu'à la minute en cours : une minute écrite après maintenant (horloge déréglée) ne compte pas.
        long to = (observedTo + MinuteMs - 1) / MinuteMs;
        using (var cmd = db.Command("SELECT ts_min, keys, writing_ms FROM minute_total WHERE ts_min >= $from AND ts_min < $to",
            start / MinuteMs, to))
        using (var r = cmd.ExecuteReader())
            while (r.Read())
            {
                keys[r.GetInt64(0) - start / MinuteMs] = r.GetInt32(1);
                writing += r.GetInt64(2);
            }
        var blind = Unobserved(db, start, observedTo);
        long blindMs = blind.Sum(s => s.EndMs - s.StartMs);
        return new DayTimeline(StatsFilter.Iso(day), Iso(Neighbour(db, "MAX(day)", "day < $d", day)),
            Iso(Neighbour(db, "MIN(day)", "day > $d AND day <= $t", day, today)),
            start, end, observedTo, keys, Hours(clock.Zone, start, keys.Length), blind, writing,
            Math.Max(0, observedTo - start - blindMs - writing), blindMs);
    }

    /// <summary>Début de chaque heure locale, cherché par quart d'heure : les fuseaux décalés de 30 ou 45 min tombent juste.</summary>
    private static List<HourMark> Hours(TimeZoneInfo zone, long start, int minutes)
    {
        var marks = new List<HourMark>();
        for (int m = 0; m < minutes; m += 15)
        {
            DateTime local = TimeZoneInfo.ConvertTime(DateTimeOffset.FromUnixTimeMilliseconds(start + m * MinuteMs), zone).DateTime;
            if (local.Minute == 0) marks.Add(new HourMark(m, local.Hour));
        }
        return marks;
    }

    private static List<TimeSpanMs> Unobserved(KeyStatsDb db, long from, long to)
    {
        var spans = new List<TimeSpanMs>();
        string reasons = string.Join(", ", Blind.Select(r => ((int)r).ToString(CultureInfo.InvariantCulture)));
        using var cmd = db.Command(
            $"SELECT start_ms, end_ms FROM pause WHERE reason IN ({reasons}) AND end_ms > $from AND start_ms < $to ORDER BY start_ms",
            from, to);
        using var r = cmd.ExecuteReader();
        while (r.Read())
        {
            long s = Math.Max(from, r.GetInt64(0)), e = Math.Min(to, r.GetInt64(1));
            if (spans.Count > 0 && s <= spans[^1].EndMs)
                spans[^1] = spans[^1] with { EndMs = Math.Max(spans[^1].EndMs, e) };
            else spans.Add(new TimeSpanMs(s, e));
        }
        return spans;
    }

    private static int? LastWritten(KeyStatsDb db, int today) => Neighbour(db, "MAX(day)", "day <= $d", today);

    private static int? Neighbour(KeyStatsDb db, string pick, string where, params object[] values)
    {
        using var cmd = db.Command($"SELECT {pick} FROM session WHERE {where}", values);
        return cmd.ExecuteScalar() is long d ? (int)d : null;
    }

    // Une année bornée : minuit en ms UTC déborderait aux extrémités du calendrier.
    private static int? Parse(string text) =>
        DateTime.TryParseExact(text, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var d)
            && d.Year is >= 1970 and <= 9998 ? LocalDayClock.KeyOf(d) : null;

    private static string? Iso(int? day) => day is { } d ? StatsFilter.Iso(d) : null;
}
