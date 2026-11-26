namespace KeyStats.Core.Storage;

/// <summary>
/// Quand la sauvegarde nocturne est due : une fois par nuit, à partir de 3 h locales. Un PC éteint à
/// cette heure-là sauvegarde à la première occasion ensuite.
/// </summary>
public static class BackupSchedule
{
    public const int Hour = 3;

    /// <summary>
    /// Vrai sans sauvegarde précédente, si la dernière date d'avant le dernier passage à 3 h, ou si elle
    /// est dans le futur (horloge reculée depuis).
    /// </summary>
    public static bool IsDue(long? lastMs, long nowMs, TimeZoneInfo zone) =>
        lastMs is not { } last || last > nowMs || last < LatestSlotMs(nowMs, zone);

    /// <summary>Dernier passage à 3 h locales, au plus tard maintenant (ms UTC).</summary>
    internal static long LatestSlotMs(long nowMs, TimeZoneInfo zone)
    {
        var today = TimeZoneInfo.ConvertTimeFromUtc(DateTime.UnixEpoch.AddMilliseconds(nowMs), zone).Date;
        long slot = SlotMs(today, zone);
        return slot <= nowMs ? slot : SlotMs(today.AddDays(-1), zone);
    }

    private static long SlotMs(DateTime day, TimeZoneInfo zone)
    {
        var slot = DateTime.SpecifyKind(day.AddHours(Hour), DateTimeKind.Unspecified);
        // Heure sautée au passage à l'heure d'été : le créneau passe à l'heure suivante.
        if (zone.IsInvalidTime(slot)) slot = slot.AddHours(1);
        return (long)(TimeZoneInfo.ConvertTimeToUtc(slot, zone) - DateTime.UnixEpoch).TotalMilliseconds;
    }
}
