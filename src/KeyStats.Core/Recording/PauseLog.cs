using KeyStats.Core.Storage;

namespace KeyStats.Core.Recording;

/// <summary>
/// Périodes non observées, en attente d'écriture dans la table <c>pause</c>. Deux sources
/// indépendantes : la fenêtre active (admin, exclue, champ secret) et une pause extérieure
/// (verrouillage, veille, pause par l'icône). Une période n'est écrite qu'une fois terminée. Les heures
/// reçues ne reculent pas (l'appelant les rend monotones) : une période de durée nulle n'est pas écrite.
/// </summary>
internal sealed class PauseLog
{
    private readonly List<(long Start, long End, PauseReason Reason)> _done = [];
    private PauseReason _focus, _external;
    private long _focusStart, _externalStart;

    public bool External => _external != 0;

    public bool HasDone => _done.Count > 0;

    /// <summary>Motif lié à la nouvelle fenêtre active, 0 si elle est observée normalement.</summary>
    public void SetFocusReason(long ms, PauseReason reason)
    {
        if (reason == _focus) return;
        Close(_focus, _focusStart, ms);
        _focus = reason;
        _focusStart = ms;
    }

    public void Begin(long ms, PauseReason reason)
    {
        if (reason == 0 || !Enum.IsDefined(reason)) throw new ArgumentOutOfRangeException(nameof(reason));
        if (reason == _external) return;
        Close(_external, _externalStart, ms);
        _external = reason;
        _externalStart = ms;
    }

    public void End(long ms)
    {
        Close(_external, _externalStart, ms);
        _external = 0;
    }

    /// <summary>Arrêt de l'agent : tout ce qui est ouvert se termine maintenant.</summary>
    public void CloseAll(long ms)
    {
        End(ms);
        SetFocusReason(ms, 0);
    }

    public void Write(KeyStatsDb db)
    {
        foreach (var (start, end, reason) in _done) db.AddPause(start, end, reason);
    }

    public void Clear() => _done.Clear();

    private void Close(PauseReason reason, long start, long end)
    {
        if (reason != 0 && end > start) _done.Add((start, end, reason));
    }
}
