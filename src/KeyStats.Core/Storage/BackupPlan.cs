namespace KeyStats.Core.Storage;

/// <summary>
/// Décide, à chaque vérification de l'agent, si la sauvegarde nocturne doit tourner : due selon
/// <see cref="BackupSchedule"/>, et pas avant une heure après un échec. Le report se compte sur une
/// horloge monotone (ms depuis le démarrage), pour qu'une horloge reculée ne le prolonge pas.
/// Non thread-safe : l'appelant ne fait qu'une vérification à la fois.
/// </summary>
public sealed class BackupPlan(long? lastRunMs)
{
    public const long RetryMs = 3_600_000;

    private long? _last = lastRunMs;
    private long? _retryAtTick;

    /// <param name="nowMs">Heure murale, ms UTC.</param>
    /// <param name="tickMs">Horloge monotone, ms.</param>
    public bool ShouldRun(long nowMs, long tickMs, TimeZoneInfo zone) =>
        (_retryAtTick is not { } retry || tickMs >= retry) && BackupSchedule.IsDue(_last, nowMs, zone);

    public void Succeeded(long nowMs)
    {
        _last = nowMs;
        _retryAtTick = null;
    }

    public void Failed(long tickMs) => _retryAtTick = tickMs + RetryMs;
}
