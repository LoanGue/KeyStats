using KeyStats.Core.Analysis;
using KeyStats.Core.Storage;
using KeyStats.Core.Web;

namespace KeyStats.Core.Stats;

/// <param name="StartMs">Début de la session, UTC en ms.</param>
/// <param name="RealWpm">Mots nets par minute sur la durée réelle, pauses comprises.</param>
/// <param name="TypingWpm">Mots par minute de temps actif, toutes les frappes de texte comptées, corrections comprises.</param>
/// <param name="PeakWpm">Meilleure fenêtre de 30 s ; null pour une session plus courte.</param>
/// <param name="Accuracy">1 − fautes / caractères tapés ; null hors des sessions de texte.</param>
public sealed record SpeedSession(long StartMs, int AppId, double RealWpm, double TypingWpm, double? PeakWpm, double? Accuracy, long DurationMs);

/// <param name="Monday">Lundi de la semaine (aaaa-mm-jj).</param>
/// <param name="Partial">Semaine en cours, pas encore finie.</param>
/// <param name="TypingWpm">Vitesse de frappe des sessions, pondérée par leur temps actif ; null sans session.</param>
/// <param name="RealWpm">Mots nets sur la durée réelle des sessions ; null sans session.</param>
public sealed record SpeedWeek(string Monday, bool Partial, double? TypingWpm, double? RealWpm);

/// <param name="Sessions">Sessions de la période, de la plus ancienne à la plus récente.</param>
/// <param name="Truncated">Vrai quand la période compte plus de <see cref="SpeedApi.MaxSessions"/> sessions : seules les plus récentes sont là.</param>
/// <param name="Weeks">Les 8 dernières semaines calendaires, filtres d'appli, de nature et de clavier appliqués.</param>
public sealed record Speed(string From, string To, IReadOnlyList<SpeedSession> Sessions, bool Truncated, IReadOnlyList<SpeedWeek> Weeks);

/// <summary>Route <c>/api/speed</c> : chaque session de la période et la vitesse semaine par semaine.</summary>
public static class SpeedApi
{
    public const string Path = "/api/speed";
    public const int WeekCount = 8;
    public const int MaxSessions = 5000;

    public static void Register(WebRouter router, StatsContext stats)
    {
        ArgumentNullException.ThrowIfNull(router);
        ArgumentNullException.ThrowIfNull(stats);
        router.Get(Path, q => stats.Respond(q, f => Read(stats.Db, f), StatsJson.Default.Speed));
    }

    public static Speed Read(KeyStatsDb db, StatsFilter filter)
    {
        ArgumentNullException.ThrowIfNull(db);
        ArgumentNullException.ThrowIfNull(filter);
        var sessions = SessionsOf(db, filter);
        bool truncated = sessions.Count > MaxSessions;
        if (truncated) sessions.RemoveAt(MaxSessions);
        sessions.Reverse();
        return new Speed(StatsFilter.Iso(filter.Range.FromDay), StatsFilter.Iso(filter.Range.ToDay), sessions, truncated, WeeksOf(db, filter));
    }

    /// <summary>Les sessions les plus récentes d'abord, une de plus que le plafond pour savoir s'il est atteint.</summary>
    private static List<SpeedSession> SessionsOf(KeyStatsDb db, StatsFilter filter)
    {
        var (where, values) = filter.SessionWhere(filter.Range);
        using var cmd = db.Command(
            "SELECT start_ms, app_id, real_cpm, gross_cpm, peak_cpm, kind, typed, typos, end_ms - start_ms FROM session WHERE "
            + where + $" ORDER BY start_ms DESC LIMIT {MaxSessions + 1}", values);
        using var r = cmd.ExecuteReader();
        var list = new List<SpeedSession>();
        while (r.Read())
        {
            long typed = r.GetInt64(6);
            double? accuracy = r.GetInt32(5) == (int)SessionKind.Prose && typed > 0 ? 1 - (double)r.GetInt64(7) / typed : null;
            list.Add(new SpeedSession(r.GetInt64(0), r.GetInt32(1), r.GetDouble(2) / 5, r.GetDouble(3) / 5,
                r.IsDBNull(4) ? null : r.GetDouble(4) / 5, accuracy, r.GetInt64(8)));
        }
        return list;
    }

    private static List<SpeedWeek> WeeksOf(KeyStatsDb db, StatsFilter filter)
    {
        var calendar = new CalendarWeeks(filter.Range.ToDay, WeekCount);
        // Par semaine : vitesse de frappe × temps actif, temps actif, caractères nets, durée.
        var sums = new (double Typing, double Active, double Chars, double Duration)[WeekCount];
        var (where, values) = filter.SessionWhere(calendar.Range);
        using (var cmd = db.Command(
            "SELECT day, TOTAL(gross_cpm * active_ms), TOTAL(active_ms), TOTAL(chars), TOTAL(end_ms - start_ms) FROM session WHERE "
            + where + " GROUP BY day", values))
        using (var r = cmd.ExecuteReader())
        {
            while (r.Read())
            {
                ref var w = ref sums[calendar.IndexOf(r.GetInt32(0))];
                w = (w.Typing + r.GetDouble(1), w.Active + r.GetDouble(2), w.Chars + r.GetDouble(3), w.Duration + r.GetDouble(4));
            }
        }
        var list = new List<SpeedWeek>(WeekCount);
        for (int i = 0; i < WeekCount; i++)
        {
            var w = sums[i];
            list.Add(new SpeedWeek(calendar.Monday(i), calendar.Partial(i),
                w.Active > 0 ? w.Typing / w.Active / 5 : null,
                w.Duration > 0 ? w.Chars / 5 / (w.Duration / 60000) : null));
        }
        return list;
    }
}
