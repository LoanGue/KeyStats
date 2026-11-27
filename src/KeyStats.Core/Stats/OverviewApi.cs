using KeyStats.Core.Analysis;
using KeyStats.Core.Storage;
using KeyStats.Core.Web;

namespace KeyStats.Core.Stats;

/// <param name="Keys">Touches pressées. Avec un filtre d'appli, de nature ou de clavier : celles des sessions seulement.</param>
/// <param name="KeysInSessions">Vrai quand <paramref name="Keys"/> ne compte que les sessions d'écriture.</param>
/// <param name="Words">Mots de 5 caractères nets écrits en session.</param>
/// <param name="WritingMs">Temps actif des sessions, pauses de plus de 2 s exclues.</param>
/// <param name="RealWpm">Mots nets par minute sur la durée réelle des sessions, pauses comprises ; null sans session.</param>
/// <param name="Accuracy">1 − fautes / caractères tapés, sur les sessions de texte ; null sans texte analysé.</param>
public sealed record OverviewTotals(long Keys, bool KeysInSessions, int Sessions, long Words, long WritingMs, double? RealWpm, double? Accuracy);

/// <param name="Monday">Lundi de la semaine (aaaa-mm-jj).</param>
/// <param name="Partial">Semaine en cours, pas encore finie.</param>
public sealed record OverviewWeek(string Monday, bool Partial, long WritingMs, double? RealWpm, double? Accuracy);

/// <param name="Previous">Même nombre de jours juste avant ; null pour « tout ».</param>
/// <param name="Weeks">Les 12 dernières semaines calendaires (lundi à dimanche), filtres d'appli, de nature et de clavier appliqués.</param>
public sealed record Overview(string From, string To, OverviewTotals Current, OverviewTotals? Previous, IReadOnlyList<OverviewWeek> Weeks);

/// <summary>Route <c>/api/overview</c> : chiffres clés de la période et progression semaine par semaine.</summary>
public static class OverviewApi
{
    public const string Path = "/api/overview";
    public const int WeekCount = 12;

    public static void Register(WebRouter router, StatsContext stats)
    {
        ArgumentNullException.ThrowIfNull(router);
        ArgumentNullException.ThrowIfNull(stats);
        router.Get(Path, q => stats.Respond(q, f => Read(stats.Db, f), StatsJson.Default.Overview));
    }

    public static Overview Read(KeyStatsDb db, StatsFilter filter)
    {
        ArgumentNullException.ThrowIfNull(db);
        ArgumentNullException.ThrowIfNull(filter);
        return new Overview(
            StatsFilter.Iso(filter.Range.FromDay),
            StatsFilter.Iso(filter.Range.ToDay),
            TotalsOf(db, filter, filter.Range),
            filter.Previous is { } previous ? TotalsOf(db, filter, previous) : null,
            WeeksOf(db, filter));
    }

    private static OverviewTotals TotalsOf(KeyStatsDb db, StatsFilter filter, DayRange range)
    {
        var (where, values) = filter.SessionWhere(range);
        using var cmd = db.Command(SessionSums + where, values);
        using var r = cmd.ExecuteReader();
        r.Read();
        var sums = Sums.Read(r);
        long keys = filter.HasSessionFilter ? sums.Keys : DayKeys(db, range);
        return new OverviewTotals(keys, filter.HasSessionFilter, sums.Sessions, sums.Chars / 5, sums.ActiveMs, sums.RealWpm, sums.Accuracy);
    }

    private static List<OverviewWeek> WeeksOf(KeyStatsDb db, StatsFilter filter)
    {
        DateTime today = LocalDayClock.DateOf(filter.Range.ToDay);
        DateTime first = today.AddDays(-(((int)today.DayOfWeek + 6) % 7) - 7 * (WeekCount - 1));
        var weeks = new Sums[WeekCount];
        var (where, values) = filter.SessionWhere(new DayRange(LocalDayClock.KeyOf(first), filter.Range.ToDay));
        using (var cmd = db.Command("SELECT day, " + SessionSums[7..] + where + " GROUP BY day", values))
        using (var r = cmd.ExecuteReader())
        {
            while (r.Read())
            {
                int index = (int)(LocalDayClock.DateOf(r.GetInt32(0)) - first).TotalDays / 7;
                weeks[index] = weeks[index].Add(Sums.Read(r, 1));
            }
        }
        var list = new List<OverviewWeek>(WeekCount);
        for (int i = 0; i < WeekCount; i++)
            list.Add(new OverviewWeek(StatsFilter.Iso(LocalDayClock.KeyOf(first.AddDays(7 * i))), i == WeekCount - 1 && today.DayOfWeek != DayOfWeek.Sunday,
                weeks[i].ActiveMs, weeks[i].RealWpm, weeks[i].Accuracy));
        return list;
    }

    private static long DayKeys(KeyStatsDb db, DayRange range)
    {
        using var cmd = db.Command("SELECT TOTAL(value) FROM day_metric WHERE metric = $m AND day >= $from AND day <= $to",
            (int)DayMetric.KeyPresses, range.FromDay, range.ToDay);
        return (long)(double)cmd.ExecuteScalar()!;
    }

    private static readonly string SessionSums =
        "SELECT COUNT(*), TOTAL(chars), TOTAL(end_ms - start_ms), TOTAL(active_ms), TOTAL(keys), "
        + $"TOTAL(CASE WHEN kind = {(int)SessionKind.Prose} THEN typed END), TOTAL(CASE WHEN kind = {(int)SessionKind.Prose} THEN typos END) "
        + "FROM session WHERE ";

    /// <summary>Sommes sur des sessions ; les moyennes se calculent après, jamais en moyennant des moyennes.</summary>
    private readonly record struct Sums(int Sessions, long Chars, long DurationMs, long ActiveMs, long Keys, long ProseTyped, long ProseTypos)
    {
        public double? RealWpm => DurationMs > 0 ? Chars / 5.0 / (DurationMs / 60000.0) : null;

        public double? Accuracy => ProseTyped > 0 ? 1 - (double)ProseTypos / ProseTyped : null;

        public Sums Add(Sums o) => new(Sessions + o.Sessions, Chars + o.Chars, DurationMs + o.DurationMs, ActiveMs + o.ActiveMs,
            Keys + o.Keys, ProseTyped + o.ProseTyped, ProseTypos + o.ProseTypos);

        public static Sums Read(Microsoft.Data.Sqlite.SqliteDataReader r, int at = 0) => new(
            r.GetInt32(at), (long)r.GetDouble(at + 1), (long)r.GetDouble(at + 2), (long)r.GetDouble(at + 3),
            (long)r.GetDouble(at + 4), (long)r.GetDouble(at + 5), (long)r.GetDouble(at + 6));
    }
}
