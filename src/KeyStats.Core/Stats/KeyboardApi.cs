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

/// <summary>Route <c>/api/keyboard</c> de la page Clavier : mesures par touche et origine des chiffres.</summary>
public static class KeyboardApi
{
    public const string Path = "/api/keyboard";

    public static void Register(WebRouter router, StatsContext stats)
    {
        ArgumentNullException.ThrowIfNull(router);
        ArgumentNullException.ThrowIfNull(stats);
        router.Get(Path, q => stats.Respond(q, f => Read(stats.Db, stats.Clock, f), StatsJson.Default.Keyboard));
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
