using KeyStats.Core.Analysis;
using KeyStats.Core.Input;
using KeyStats.Core.Storage;
using KeyStats.Core.Web;

namespace KeyStats.Core.Stats;

/// <param name="Code">Touche physique (<see cref="KeyCode"/>).</param>
/// <param name="Presses">Appuis, répétitions automatiques non comprises.</param>
/// <param name="ErrorRate">Fautes de prose sur le caractère que produit la touche, rapportées aux caractères de prose qu'elle a tapés ; null sans prose. Null aussi pour les chiffres et opérateurs que le pavé double : le caractère ne dit
/// pas quelle touche l'a tapé.</param>
/// <param name="HoldMs">Durée d'appui médiane ; null sans mesure.</param>
/// <param name="DelayMs">Délai médian entre le caractère précédent et celui de cette touche ; null sans mesure, et pour les
/// caractères que le pavé double.</param>
public sealed record KeyboardKey(int Code, long Presses, double? ErrorRate, double? HoldMs, double? DelayMs);

/// <param name="TopRow">Chiffres tapés sur la rangée du haut.</param>
/// <param name="Numpad">Chiffres tapés sur le pavé numérique.</param>
public sealed record KeyboardDigit(int Digit, long TopRow, long Numpad);

/// <param name="DayTablesUnfiltered">
/// Vrai avec un filtre d'appli, de nature ou de clavier. Les appuis suivent le filtre de clavier seul ; le taux de faute,
/// la durée d'appui, le délai et les chiffres couvrent toute la période.
/// </param>
/// <param name="Presses">Appuis sur toutes les touches, y compris celles absentes de <c>Keys</c>.</param>
/// <param name="Keys">Touches qui ont au moins une mesure, par code croissant.</param>
/// <param name="Digits">Chiffres de 0 à 9.</param>
public sealed record Keyboard(string From, string To, bool DayTablesUnfiltered, long Presses, IReadOnlyList<KeyboardKey> Keys,
    IReadOnlyList<KeyboardDigit> Digits);

/// <param name="Minute">Minute depuis minuit local (0 à 1439, jusqu'à 1499 un jour de changement d'heure).</param>
public sealed record KeyMinute(int Minute, long Presses);

/// <summary>
/// Appuis d'une touche minute par minute. Comme les appuis de <see cref="Keyboard"/>, ils viennent de <c>key_minute</c>,
/// qui ne suit que le filtre de clavier : les filtres d'application et de nature ne s'y appliquent pas.
/// </summary>
/// <param name="Day">Dernier jour de la période où la touche a servi (aaaa-mm-jj) ; null si elle n'a pas servi.</param>
/// <param name="Minutes">Minutes de ce jour qui ont des appuis, dans l'ordre.</param>
public sealed record KeyboardMinutes(int Code, string? Day, IReadOnlyList<KeyMinute> Minutes);

/// <summary>
/// Routes de la page Clavier : <c>/api/keyboard</c> (mesures par touche et origine des chiffres) et
/// <c>/api/keyboard/minutes?key=</c> (appuis minute par minute d'une touche).
/// </summary>
public static class KeyboardApi
{
    public const string Path = "/api/keyboard";
    public const string MinutesPath = "/api/keyboard/minutes";

    public static void Register(WebRouter router, StatsContext stats)
    {
        ArgumentNullException.ThrowIfNull(router);
        ArgumentNullException.ThrowIfNull(stats);
        router.Get(Path, q => stats.Respond(q, f => Read(stats.Db, stats.Clock, f), StatsJson.Default.Keyboard));
        router.Get(MinutesPath, q => q.TryGetId("key", out int? key) && key < KeyCode.Count
            ? stats.Respond(q, f => Minutes(stats.Db, stats.Clock, f, key.Value), StatsJson.Default.KeyboardMinutes)
            : WebResponse.BadRequest());
    }

    public static Keyboard Read(KeyStatsDb db, LocalDayClock clock, StatsFilter filter)
    {
        ArgumentNullException.ThrowIfNull(db);
        ArgumentNullException.ThrowIfNull(clock);
        ArgumentNullException.ThrowIfNull(filter);
        var range = filter.Range;
        var presses = KeyboardTables.Presses(db, KeyMinuteWhere(clock, filter, range, out var values), values);
        var errors = KeyboardTables.ErrorRates(db, range);
        var holds = KeyboardTables.HoldMedians(db, range);
        var delays = KeyboardTables.DelayMedians(db, range);
        var codes = new SortedSet<int>(presses.Keys);
        codes.UnionWith(errors.Keys);
        codes.UnionWith(holds.Keys);
        codes.UnionWith(delays.Keys);
        var keys = codes.Select(c => new KeyboardKey(c, presses.GetValueOrDefault(c),
            errors.TryGetValue(c, out double e) ? e : null, holds.TryGetValue(c, out double h) ? h : null,
            delays.TryGetValue(c, out double d) ? d : null)).ToList();
        return new Keyboard(StatsFilter.Iso(range.FromDay), StatsFilter.Iso(range.ToDay), filter.HasSessionFilter,
            presses.Values.Sum(), keys, KeyboardTables.Digits(db, range));
    }

    public static KeyboardMinutes Minutes(KeyStatsDb db, LocalDayClock clock, StatsFilter filter, int key)
    {
        ArgumentNullException.ThrowIfNull(db);
        ArgumentNullException.ThrowIfNull(clock);
        ArgumentNullException.ThrowIfNull(filter);
        // Une ligne peut ne compter que des répétitions (presses = 0) : elle ne désigne pas le dernier jour, ni une minute.
        string where = KeyMinuteWhere(clock, filter, filter.Range, out var values) + " AND key = $key AND presses > 0";
        object? last;
        using (var cmd = db.Command("SELECT MAX(ts_min) FROM key_minute WHERE " + where, [.. values, key]))
            last = cmd.ExecuteScalar();
        if (last is not long lastMinute) return new KeyboardMinutes(key, null, []);
        int day = clock.DayKey(lastMinute * 60_000);
        var dayRange = new DayRange(day, day);
        where = KeyMinuteWhere(clock, filter, dayRange, out values) + " AND key = $key";
        long midnight = LocalDayClock.MinuteOf(clock.MidnightUtcMs(LocalDayClock.DateOf(day)));
        var minutes = new List<KeyMinute>();
        using (var cmd = db.Command($"SELECT ts_min, SUM(presses) FROM key_minute WHERE {where} GROUP BY ts_min HAVING SUM(presses) > 0 ORDER BY ts_min",
            [.. values, key]))
        using (var r = cmd.ExecuteReader())
        {
            while (r.Read()) minutes.Add(new KeyMinute((int)(r.GetInt64(0) - midnight), r.GetInt64(1)));
        }
        return new KeyboardMinutes(key, StatsFilter.Iso(day), minutes);
    }

    /// <summary>Condition sur <c>key_minute</c> : minutes UTC des jours locaux de <paramref name="range"/>, et clavier choisi.</summary>
    private static string KeyMinuteWhere(LocalDayClock clock, StatsFilter filter, DayRange range, out object?[] values)
    {
        long from = LocalDayClock.MinuteOf(clock.MidnightUtcMs(LocalDayClock.DateOf(range.FromDay)));
        long to = LocalDayClock.MinuteOf(clock.MidnightUtcMs(LocalDayClock.DateOf(range.ToDay).AddDays(1)));
        if (filter.DeviceId is not { } device)
        {
            values = [from, to];
            return "ts_min >= $from AND ts_min < $to";
        }
        values = [from, to, device];
        return "ts_min >= $from AND ts_min < $to AND device_id = $device";
    }
}
