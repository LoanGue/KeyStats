namespace KeyStats.Core.Analysis;

/// <summary>
/// Jour local (aaaammjj) d'un horodatage UTC, pour les tables <c>day_*</c>. Les bornes du jour en
/// cours sont gardées en cache : un seul calcul de fuseau par jour. Les minutes et les heures des
/// autres tables restent en UTC. Pas sûr entre threads : il vit sur le thread d'analyse.
/// </summary>
public sealed class LocalDayClock
{
    private TimeZoneInfo _zone;
    private long _start = long.MaxValue, _end = long.MinValue;
    private int _key;

    public LocalDayClock(TimeZoneInfo? zone = null) => _zone = zone ?? TimeZoneInfo.Local;

    public TimeZoneInfo Zone => _zone;

    /// <summary>
    /// Change de fuseau, par exemple quand l'agent reçoit <c>WM_TIMECHANGE</c> (après
    /// <see cref="TimeZoneInfo.ClearCachedData"/>). Les frappes suivantes vont dans le jour local du
    /// nouveau fuseau ; les jours déjà écrits ne bougent pas.
    /// </summary>
    public void SetZone(TimeZoneInfo zone)
    {
        ArgumentNullException.ThrowIfNull(zone);
        _zone = zone;
        _start = long.MaxValue;
        _end = long.MinValue;
    }

    public int DayKey(long ms)
    {
        if (ms >= _start && ms < _end) return _key;
        DateTime local = TimeZoneInfo.ConvertTime(DateTimeOffset.FromUnixTimeMilliseconds(ms), _zone).DateTime;
        DateTime date = local.Date;
        int key = KeyOf(date);
        long start = MidnightUtcMs(date), end = MidnightUtcMs(date.AddDays(1));
        if (ms >= start && ms < end)
        {
            _start = start;
            _end = end;
            _key = key;
        }
        return key;
    }

    /// <summary>Début du jour local, en ms UTC. Minuit peut tomber dans un saut d'heure : on prend l'heure valide suivante.</summary>
    public long MidnightUtcMs(DateTime localDate)
    {
        var dt = DateTime.SpecifyKind(localDate.Date, DateTimeKind.Unspecified);
        int guard = 0;
        while (_zone.IsInvalidTime(dt) && guard++ < 4) dt = dt.AddMinutes(30);
        TimeSpan offset = _zone.IsAmbiguousTime(dt) ? _zone.GetAmbiguousTimeOffsets(dt).Max() : _zone.GetUtcOffset(dt);
        return new DateTimeOffset(dt, offset).ToUnixTimeMilliseconds();
    }

    public static long MinuteOf(long ms) => Math.DivRem(ms, 60_000, out long r) - (r < 0 ? 1 : 0);

    public static long HourOf(long ms) => Math.DivRem(ms, 3_600_000, out long r) - (r < 0 ? 1 : 0);

    /// <summary>Clé aaaammjj d'une date (l'heure est ignorée).</summary>
    public static int KeyOf(DateTime date) => date.Year * 10000 + date.Month * 100 + date.Day;

    public static DateTime DateOf(int dayKey) => new(dayKey / 10000, dayKey / 100 % 100, dayKey % 100);
}
