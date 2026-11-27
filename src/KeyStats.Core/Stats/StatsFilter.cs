using System.Globalization;
using KeyStats.Core.Analysis;
using KeyStats.Core.Web;

namespace KeyStats.Core.Stats;

/// <summary>Période choisie dans l'en-tête du tableau de bord.</summary>
public enum StatsPeriod
{
    Day,
    Week,
    Month,
    Year,
    All,
}

/// <summary>Jours locaux aaaammjj, bornes comprises.</summary>
public readonly record struct DayRange(int FromDay, int ToDay)
{
    public int Days => (int)(LocalDayClock.DateOf(ToDay) - LocalDayClock.DateOf(FromDay)).TotalDays + 1;

    /// <summary>La plage de même longueur qui finit la veille de celle-ci.</summary>
    public DayRange Before()
    {
        DateTime end = LocalDayClock.DateOf(FromDay).AddDays(-1);
        return new DayRange(LocalDayClock.KeyOf(end.AddDays(1 - Days)), LocalDayClock.KeyOf(end));
    }
}

/// <summary>
/// Filtres globaux de l'en-tête : période (et la précédente, pour les écarts), appli, nature du texte,
/// clavier. L'appli, la nature et le clavier ne valent que pour les sessions d'écriture : les tables par
/// jour (caractères, bigrammes, raccourcis…) ne les connaissent pas.
/// </summary>
public sealed record StatsFilter(StatsPeriod Period, DayRange Range, DayRange? Previous, int? AppId, SessionKind? Kind, int? DeviceId)
{
    /// <summary>Longueur des périodes glissantes, en jours, aujourd'hui compris.</summary>
    public static int DaysOf(StatsPeriod period) => period switch
    {
        StatsPeriod.Day => 1,
        StatsPeriod.Week => 7,
        StatsPeriod.Month => 30,
        StatsPeriod.Year => 365,
        _ => throw new ArgumentOutOfRangeException(nameof(period)),
    };

    public bool HasSessionFilter => AppId is not null || Kind is not null || DeviceId is not null;

    /// <summary>
    /// Lit <c>period</c> (day, week, month, year, all ; week par défaut), <c>app</c>, <c>device</c> (ids) et
    /// <c>kind</c> (prose, mixed, code). <paramref name="firstDay"/> est le premier jour enregistré, pour
    /// « all » (null : base vide, la plage se réduit à aujourd'hui). Null si un paramètre est invalide.
    /// </summary>
    public static StatsFilter? Parse(WebQuery query, int today, int? firstDay)
    {
        ArgumentNullException.ThrowIfNull(query);
        if (!TryPeriod(query["period"], out var period) || !TryKind(query["kind"], out var kind)
            || !query.TryGetId("app", out int? app) || !query.TryGetId("device", out int? device))
            return null;
        if (period == StatsPeriod.All)
            return new StatsFilter(period, new DayRange(Math.Min(firstDay ?? today, today), today), null, app, kind, device);
        var range = new DayRange(LocalDayClock.KeyOf(LocalDayClock.DateOf(today).AddDays(1 - DaysOf(period))), today);
        return new StatsFilter(period, range, range.Before(), app, kind, device);
    }

    /// <summary>
    /// Condition SQL sur la table <c>session</c> pour <paramref name="range"/> et les filtres, avec ses
    /// valeurs dans l'ordre des paramètres.
    /// </summary>
    public (string Sql, object?[] Values) SessionWhere(DayRange range)
    {
        var sql = new System.Text.StringBuilder("day >= $from AND day <= $to");
        var values = new List<object?> { range.FromDay, range.ToDay };
        if (AppId is { } app)
        {
            sql.Append(" AND app_id = $app");
            values.Add(app);
        }
        if (Kind is { } kind)
        {
            sql.Append(" AND kind = $kind");
            values.Add((int)kind);
        }
        if (DeviceId is { } device)
        {
            sql.Append(" AND device_id = $device");
            values.Add(device);
        }
        return (sql.ToString(), [.. values]);
    }

    private static bool TryPeriod(string? text, out StatsPeriod period)
    {
        (bool ok, period) = text switch
        {
            null or "" or "week" => (true, StatsPeriod.Week),
            "day" => (true, StatsPeriod.Day),
            "month" => (true, StatsPeriod.Month),
            "year" => (true, StatsPeriod.Year),
            "all" => (true, StatsPeriod.All),
            _ => (false, default),
        };
        return ok;
    }

    private static bool TryKind(string? text, out SessionKind? kind)
    {
        kind = text switch
        {
            "prose" => SessionKind.Prose,
            "mixed" => SessionKind.Mixed,
            "code" => SessionKind.Code,
            _ => null,
        };
        return kind is not null || string.IsNullOrEmpty(text);
    }

    /// <summary>Jour aaaammjj pour l'affichage (2026-11-27).</summary>
    public static string Iso(int day) => LocalDayClock.DateOf(day).ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
}
