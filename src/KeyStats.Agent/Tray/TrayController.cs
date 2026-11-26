using KeyStats.Agent.Interop;
using KeyStats.Core.Recording;
using KeyStats.Core.Storage;

namespace KeyStats.Agent.Tray;

/// <summary>
/// Relie l'icône à <see cref="PauseSources"/> : sélection (clic gauche, Entrée) +5 min, menu du clic droit, infobulle
/// tenue à jour par une minuterie d'une seconde, active seulement pendant une pause à durée fixée.
/// Thread de la fenêtre de session uniquement.
/// </summary>
internal sealed class TrayController(PauseSources pauses, Func<long> now, Func<TimeZoneInfo> zone,
    Action<PauseTransition> apply) : IDisposable
{
    private const nuint TickTimer = 1;

    private nint _hwnd;
    private TrayIcon? _icon;
    private bool _ticking;

    public void Attach(nint hwnd)
    {
        _hwnd = hwnd;
        _icon = new TrayIcon(hwnd);
        Refresh();
    }

    /// <summary>Message de l'icône (version 4 : événement dans le mot bas de lParam, point d'ancrage dans wParam).</summary>
    public void OnIconMessage(nint wParam, nint lParam)
    {
        switch ((uint)(lParam & 0xFFFF))
        {
            case TrayApi.NIN_SELECT:
            case TrayApi.NIN_KEYSELECT:
                Run(TrayCommand.Snooze);
                break;
            case TrayApi.WM_CONTEXTMENU:
                long snoozeUntil = pauses.SnoozeUntil;
                Run(TrayMenu.Show(_hwnd, (short)(wParam & 0xFFFF), (short)((wParam >> 16) & 0xFFFF), snoozeUntil != 0));
                break;
        }
    }

    public void OnTick() => Changed(pauses.Tick(now()));

    public void OnTaskbarCreated() => _icon?.Recreate();

    /// <summary>Après tout changement de pause, d'où qu'il vienne (verrouillage compris).</summary>
    public void Refresh()
    {
        if (_icon is null) return;
        var reason = pauses.Reason;
        _icon.Show(reason != 0, PauseLabel.Format(reason, pauses.SnoozeUntil, now(), zone()));
        // Pas de minuterie pendant un verrouillage : l'infobulle ne montre alors pas de compte à rebours.
        bool tick = reason == PauseReason.Snooze && pauses.SnoozeUntil != PauseSources.UntilResumed;
        if (tick == _ticking) return;
        _ticking = tick;
        if (tick) _ = Timers.SetTimer(_hwnd, TickTimer, 1000, 0);
        else _ = Timers.KillTimer(_hwnd, TickTimer);
    }

    public static bool IsTick(nuint timerId) => timerId == TickTimer;

    public void Dispose()
    {
        if (_ticking) _ = Timers.KillTimer(_hwnd, TickTimer);
        _icon?.Dispose();
        _icon = null;
    }

    private void Run(TrayCommand command)
    {
        // Le menu tourne une boucle modale : une fin de session a pu arrêter l'agent entre-temps.
        if (_icon is null) return;
        long t = now();
        switch (command)
        {
            case TrayCommand.Snooze: Changed(pauses.Snooze(t)); break;
            case TrayCommand.SnoozeUntilResumed: Changed(pauses.SnoozeUntilResumedManually(t)); break;
            case TrayCommand.ResumeNow: Changed(pauses.ResumeNow(t)); break;
            case TrayCommand.Quit: _ = User32.PostMessage(_hwnd, User32.WM_CLOSE, 0, 0); break;
        }
    }

    private void Changed(PauseTransition transition)
    {
        apply(transition);
        Refresh();
    }
}
