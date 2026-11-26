using KeyStats.Agent.Interop;
using KeyStats.Core.Recording;
using KeyStats.Core.Storage;

namespace KeyStats.Agent.Tray;

/// <summary>
/// Relie l'icône à <see cref="PauseSources"/> : sélection (clic gauche, Entrée) +5 min, menu du clic droit, infobulle
/// tenue à jour par une minuterie d'une seconde, active seulement pendant une pause à durée fixée.
/// Chaque clic qui prolonge la pause est confirmé par <paramref name="snoozed"/> (pause restante, en ms).
/// Thread de la fenêtre de session uniquement.
/// </summary>
internal sealed class TrayController(PauseSources pauses, Func<long> now, Func<TimeZoneInfo> zone,
    Action<PauseTransition> apply, Action<long> snoozed) : IDisposable
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
                if (!Execute(TrayCommand.Snooze)) break;
                long until = pauses.SnoozeUntil;
                if (pauses.Reason == PauseReason.Snooze && until != PauseSources.UntilResumed) snoozed(until - now());
                break;
            case TrayApi.WM_CONTEXTMENU:
                long snoozeUntil = pauses.SnoozeUntil;
                _ = Execute(TrayMenu.Show(_hwnd, (short)(wParam & 0xFFFF), (short)((wParam >> 16) & 0xFFFF), snoozeUntil != 0));
                break;
        }
    }

    public void OnTick() => Changed(pauses.Tick(now()));

    public void OnTaskbarCreated() => _icon?.Recreate();

    /// <summary>Après tout changement de pause, d'où qu'il vienne (verrouillage compris).</summary>
    public void Refresh()
    {
        PauseReason reason;
        long until;
        // Show attend l'Explorateur, et le thread traite pendant ce temps les messages envoyés : une commande
        // du tube peut changer la pause au milieu. On recommence tant que l'état lu n'est plus le bon.
        do
        {
            if (_icon is null) return;
            reason = pauses.Reason;
            until = pauses.SnoozeUntil;
            _icon.Show(reason != 0, PauseLabel.Format(reason, until, now(), zone()));
        }
        while (reason != pauses.Reason || until != pauses.SnoozeUntil);
        // Pas de minuterie pendant un verrouillage : l'infobulle ne montre alors pas de compte à rebours.
        bool tick = reason == PauseReason.Snooze && until != PauseSources.UntilResumed;
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

    /// <summary>Commande du menu, ou du tube de commande.</summary>
    /// <returns>Faux si l'agent s'arrête déjà : la commande n'a rien fait.</returns>
    public bool Execute(TrayCommand command)
    {
        // Le menu tourne une boucle modale : une fin de session a pu arrêter l'agent entre-temps.
        if (_icon is null) return false;
        long t = now();
        switch (command)
        {
            case TrayCommand.Snooze: Changed(pauses.Snooze(t)); break;
            case TrayCommand.SnoozeUntilResumed: Changed(pauses.SnoozeUntilResumedManually(t)); break;
            case TrayCommand.ResumeNow: Changed(pauses.ResumeNow(t)); break;
            case TrayCommand.Quit: _ = User32.PostMessage(_hwnd, User32.WM_CLOSE, 0, 0); break;
        }
        return true;
    }

    private void Changed(PauseTransition transition)
    {
        apply(transition);
        Refresh();
    }
}
