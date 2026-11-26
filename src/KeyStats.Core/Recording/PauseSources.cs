using KeyStats.Core.Storage;

namespace KeyStats.Core.Recording;

/// <summary>Effet d'un changement sur la pause extérieure en vigueur.</summary>
public enum PauseTransition : byte
{
    None = 0,
    /// <summary>L'enregistrement s'arrête.</summary>
    Paused = 1,
    /// <summary>Toujours en pause, pour un autre motif (verrouillé ↔ pause par l'icône).</summary>
    Changed = 2,
    /// <summary>L'enregistrement reprend : l'appelant remet à zéro l'état des touches et recale l'horloge.</summary>
    Resumed = 3,
}

/// <summary>
/// Réunit ce qui suspend l'enregistrement : verrouillage, veille et pause par l'icône (+5 min par clic,
/// ou jusqu'à reprise manuelle). Le verrouillage et la veille l'emportent sur la pause par l'icône.
/// Chaque changement devient une <see cref="RecorderCommand"/> ; la clé d'époque est oubliée en entrant
/// en verrouillage ou en veille seulement. Un seul thread appelant, heures prises sur l'échelle des événements.
/// </summary>
public sealed class PauseSources(RecorderCommands commands)
{
    public const long SnoozeStepMs = 5 * 60_000;
    /// <summary>Fin de pause par l'icône quand elle dure jusqu'à reprise manuelle.</summary>
    public const long UntilResumed = long.MaxValue;

    private bool _locked, _suspended;
    private long _snoozeUntil, _lastMs = long.MinValue;

    /// <summary>Motif en vigueur, 0 si l'enregistrement tourne.</summary>
    public PauseReason Reason { get; private set; }

    /// <summary>Fin prévue de la pause par l'icône (même pendant un verrouillage), 0 sans pause, <see cref="UntilResumed"/> sans fin.</summary>
    public long SnoozeUntil => _snoozeUntil;

    public PauseTransition SetLocked(bool on, long nowMs)
    {
        _locked = on;
        return Update(nowMs);
    }

    public PauseTransition SetSuspended(bool on, long nowMs)
    {
        _suspended = on;
        return Update(nowMs);
    }

    /// <summary>Clic sur l'icône : 5 min de plus à partir de la fin prévue, ou de maintenant.</summary>
    public PauseTransition Snooze(long nowMs)
    {
        // Une pause échue se termine d'abord à son heure : le clic en ouvre une nouvelle.
        var expired = Update(nowMs);
        if (_snoozeUntil != UntilResumed) _snoozeUntil = Math.Max(_snoozeUntil, nowMs) + SnoozeStepMs;
        var t = Update(nowMs);
        return t != PauseTransition.None ? t : expired;
    }

    public PauseTransition SnoozeUntilResumedManually(long nowMs)
    {
        _snoozeUntil = UntilResumed;
        return Update(nowMs);
    }

    /// <summary>Fin de la pause par l'icône ; sans effet sur le verrouillage ou la veille.</summary>
    public PauseTransition ResumeNow(long nowMs)
    {
        _snoozeUntil = 0;
        return Update(nowMs);
    }

    /// <summary>Appelé par une minuterie : la pause par l'icône se termine à l'heure prévue.</summary>
    public PauseTransition Tick(long nowMs) => Update(nowMs);

    private PauseTransition Update(long nowMs)
    {
        // Une pause échue se termine à son heure, pas à celle où on s'en aperçoit.
        long expiredAt = Expire(nowMs);
        long at = Math.Max(_lastMs, expiredAt != 0 && Reason == PauseReason.Snooze && !_locked && !_suspended ? expiredAt : nowMs);
        var target = _locked || _suspended ? PauseReason.Locked : _snoozeUntil != 0 ? PauseReason.Snooze : 0;
        if (target == Reason) return PauseTransition.None;
        var before = Reason;
        Reason = target;
        _lastMs = at;
        if (target == 0)
        {
            commands.Post(new RecorderCommand(at, CommandKind.Resume));
            return PauseTransition.Resumed;
        }
        commands.Post(new RecorderCommand(at, CommandKind.Pause, target));
        if (target == PauseReason.Locked) commands.Post(new RecorderCommand(at, CommandKind.DropEpoch));
        return before == 0 ? PauseTransition.Paused : PauseTransition.Changed;
    }

    /// <summary>Oublie une pause par l'icône échue et renvoie son heure de fin, 0 sinon.</summary>
    private long Expire(long nowMs)
    {
        if (_snoozeUntil == 0 || _snoozeUntil > nowMs) return 0;
        long end = _snoozeUntil;
        _snoozeUntil = 0;
        return end;
    }
}
