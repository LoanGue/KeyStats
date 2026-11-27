using KeyStats.Core.Analysis;
using KeyStats.Core.Storage;
using KeyStats.Core.Web;

namespace KeyStats.Core.Stats;

/// <param name="Day">Jour local de la frappe (aaaa-mm-jj).</param>
/// <param name="WritingMs">Temps actif des sessions de ce jour.</param>
public sealed record ActivityDay(string Day, long WritingMs);

/// <param name="Id">Id de l'appli, celui du filtre <c>app</c>.</param>
/// <param name="Name">Nom de l'exécutable.</param>
public sealed record ActivityApp(int Id, string Name, long WritingMs);

/// <param name="CalendarFrom">Lundi de la première des <see cref="ActivityApi.CalendarWeeks"/> semaines du calendrier.</param>
/// <param name="Calendar">Jours du calendrier qui ont du temps d'écriture, dans l'ordre. Le calendrier ignore la période, pas les autres filtres.</param>
/// <param name="Hours">
/// Temps actif de la période par jour de la semaine (du lundi au dimanche) et heure locale (0 à 23). Une session à cheval sur
/// plusieurs heures est répartie au prorata de sa durée.
/// </param>
/// <param name="Apps">Applis de la période, de la plus utilisée à la moins utilisée.</param>
public sealed record Activity(string From, string To, string CalendarFrom, IReadOnlyList<ActivityDay> Calendar,
    IReadOnlyList<IReadOnlyList<long>> Hours, IReadOnlyList<ActivityApp> Apps);

/// <summary>
/// Route <c>/api/activity</c> : quand et dans quelles applis on écrit. Tout vient des sessions d'écriture, qui suivent
/// tous les filtres de l'en-tête ; le temps compté est leur temps actif, comme sur la vue d'ensemble.
/// </summary>
public static class ActivityApi
{
    public const string Path = "/api/activity";

    /// <summary>53 semaines : les 12 derniers mois au complet, quel que soit le jour de la semaine.</summary>
    public const int CalendarWeeks = 53;

    /// <summary>Pas de la répartition par heure : un quart d'heure suit aussi les fuseaux décalés de 30 ou 45 min.</summary>
    private const long SliceMs = 900_000;

    public static void Register(WebRouter router, StatsContext stats)
    {
        ArgumentNullException.ThrowIfNull(router);
        ArgumentNullException.ThrowIfNull(stats);
        router.Get(Path, q => stats.Respond(q, f => Read(stats.Db, stats.Clock.Zone, f), StatsJson.Default.Activity));
    }

    public static Activity Read(KeyStatsDb db, TimeZoneInfo zone, StatsFilter filter)
    {
        ArgumentNullException.ThrowIfNull(db);
        ArgumentNullException.ThrowIfNull(zone);
        ArgumentNullException.ThrowIfNull(filter);
        var calendar = new CalendarWeeks(filter.Range.ToDay, CalendarWeeks);
        var days = new List<ActivityDay>();
        Query(db, filter, calendar.Range, "day, TOTAL(active_ms)", " GROUP BY day ORDER BY day",
            r => days.Add(new ActivityDay(StatsFilter.Iso(r.GetInt32(0)), (long)r.GetDouble(1))));
        var apps = new List<ActivityApp>();
        Query(db, filter, filter.Range, "app_id, (SELECT exe FROM app WHERE id = app_id), TOTAL(active_ms)",
            " GROUP BY app_id ORDER BY 3 DESC, 2",
            r => apps.Add(new ActivityApp(r.GetInt32(0), r.GetString(1), (long)r.GetDouble(2))));
        return new Activity(StatsFilter.Iso(filter.Range.FromDay), StatsFilter.Iso(filter.Range.ToDay),
            StatsFilter.Iso(calendar.Range.FromDay), days, Hours(db, zone, filter), apps);
    }

    private static long[][] Hours(KeyStatsDb db, TimeZoneInfo zone, StatsFilter filter)
    {
        var grid = new double[7, 24];
        Query(db, filter, filter.Range, "start_ms, end_ms, active_ms", "",
            r => Spread(grid, zone, r.GetInt64(0), r.GetInt64(1), r.GetInt64(2)));
        var hours = new long[7][];
        for (int d = 0; d < 7; d++)
        {
            hours[d] = new long[24];
            for (int h = 0; h < 24; h++) hours[d][h] = (long)Math.Round(grid[d, h]);
        }
        return hours;
    }

    /// <summary>Répartit le temps actif d'une session sur les heures locales qu'elle couvre, par tranche d'un quart d'heure UTC.</summary>
    internal static void Spread(double[,] grid, TimeZoneInfo zone, long startMs, long endMs, long activeMs)
    {
        if (endMs <= startMs)
        {
            Add(grid, zone, startMs, activeMs);
            return;
        }
        for (long from = startMs; from < endMs;)
        {
            long to = Math.Min(endMs, (Math.DivRem(from, SliceMs, out long r) - (r < 0 ? 1 : 0) + 1) * SliceMs);
            Add(grid, zone, from, (double)activeMs * (to - from) / (endMs - startMs));
            from = to;
        }
    }

    private static void Add(double[,] grid, TimeZoneInfo zone, long ms, double value)
    {
        DateTime local = TimeZoneInfo.ConvertTime(DateTimeOffset.FromUnixTimeMilliseconds(ms), zone).DateTime;
        grid[((int)local.DayOfWeek + 6) % 7, local.Hour] += value;
    }

    private static void Query(KeyStatsDb db, StatsFilter filter, DayRange range, string columns, string tail,
        Action<Microsoft.Data.Sqlite.SqliteDataReader> row)
    {
        var (where, values) = filter.SessionWhere(range);
        using var cmd = db.Command($"SELECT {columns} FROM session WHERE {where}{tail}", values);
        using var r = cmd.ExecuteReader();
        while (r.Read()) row(r);
    }
}
