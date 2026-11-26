using KeyStats.Core.Storage;

namespace KeyStats.Core.Recording;

/// <summary>
/// Texte de l'infobulle de l'icône de notification : état de l'enregistrement et fin prévue d'une pause.
/// Heure locale du fuseau donné, sans dépendre de la culture (l'agent tourne en globalisation invariante).
/// </summary>
public static class PauseLabel
{
    private const long MinuteMs = 60_000;

    /// <param name="reason"><see cref="PauseSources.Reason"/> : 0, pause par l'icône ou verrouillage.</param>
    /// <param name="snoozeUntilMs"><see cref="PauseSources.SnoozeUntil"/>.</param>
    public static string Format(PauseReason reason, long snoozeUntilMs, long nowMs, TimeZoneInfo zone) => reason switch
    {
        0 => "KeyStats — enregistrement en cours",
        PauseReason.Snooze when snoozeUntilMs == PauseSources.UntilResumed => "KeyStats — en pause jusqu'à reprise manuelle",
        PauseReason.Snooze => Until(snoozeUntilMs, nowMs, zone),
        PauseReason.Locked => "KeyStats — en pause (session verrouillée ou en veille)",
        _ => throw new ArgumentOutOfRangeException(nameof(reason), reason, "Motif que PauseSources ne produit pas."),
    };

    private static string Until(long untilMs, long nowMs, TimeZoneInfo zone)
    {
        var local = TimeZoneInfo.ConvertTimeFromUtc(DateTime.UnixEpoch.AddMilliseconds(untilMs), zone);
        string at = $"KeyStats — en pause, reprise à {local.Hour:00}:{local.Minute:00}";
        // Fin passée mais pas encore vue par la minuterie : pas de compte à rebours.
        if (untilMs <= nowMs) return at;
        // Arrondi au-dessus : « dans 1 min » jusqu'à la dernière seconde, jamais « dans 0 min ».
        long minutes = (untilMs - nowMs + MinuteMs - 1) / MinuteMs;
        string left = minutes < 60 ? $"{minutes} min" : $"{minutes / 60} h {minutes % 60:00}";
        return $"{at} (dans {left})";
    }
}
