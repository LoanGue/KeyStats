using KeyStats.Agent.Capture;
using KeyStats.Agent.Recording;
using KeyStats.Agent.Tray;
using KeyStats.Core.Analysis;
using KeyStats.Core.Control;
using KeyStats.Core.Focus;
using KeyStats.Core.Input;
using KeyStats.Core.Recording;
using KeyStats.Core.Storage;

namespace KeyStats.Agent.Lifecycle;

/// <summary>
/// L'agent complet : capture, analyse et écriture, plus le cycle de vie de la session Windows.
/// Les événements de session arrivent sur le thread principal et deviennent des commandes datées
/// sur l'échelle des frappes, appliquées à leur place par le thread d'analyse.
/// </summary>
internal sealed class AgentHost : ISessionEvents, IDisposable
{
    // Windows laisse environ 5 s après WM_ENDSESSION : la capture n'a qu'à quitter sa boucle,
    // l'analyse garde le plus long pour écrire le dernier lot.
    private static readonly TimeSpan CaptureStopTimeout = TimeSpan.FromSeconds(1);
    private static readonly TimeSpan StopTimeout = TimeSpan.FromSeconds(3.5);
    // Windows laisse environ 2 s avant la veille : l'écriture du lot doit tenir dedans.
    private static readonly TimeSpan SuspendWait = TimeSpan.FromSeconds(1.5);
    // Une commande du tube doit répondre en moins de 1,5 s (ControlBridge).
    private static readonly TimeSpan FlushWait = TimeSpan.FromSeconds(1);

    private readonly AutoResetEvent _signal = new(false);
    private readonly RawInputCapture _capture;
    private readonly AgentStorage _storage;
    private readonly RecorderCommands _commands = new();
    private readonly PauseSources _pauses;
    private readonly AnalysisLoop _loop;
    private readonly TrayController _tray;
    private readonly BackupScheduler _backup;
    private readonly string _dbPath;
    private ControlBridge? _control;
    private TimeZoneInfo _zone = TimeZoneInfo.Local;
    private bool _shutDown;

    private AgentHost(string dbPath)
    {
        _dbPath = dbPath;
        var ring = new EventRing(4096);
        var focus = new FocusChannel();
        _capture = new RawInputCapture(ring, _signal, focus);
        _storage = AgentStorage.Open(dbPath);
        var recorder = new Recorder(_storage.Db, _storage.Archive, new LocalDayClock(_zone), _capture.Clock.PeekMs());
        _pauses = new PauseSources(_commands);
        _tray = new TrayController(_pauses, Now, () => _zone, t => Apply(t));
        _loop = new AnalysisLoop(new RingPump(ring, focus, recorder, _commands), _capture.Clock, _signal);
        _backup = new BackupScheduler(dbPath, () => Volatile.Read(ref _zone));
    }

    /// <summary>Tourne jusqu'à la fin de la session Windows.</summary>
    public static int Run(string dbPath)
    {
        using var host = new AgentHost(dbPath);
        host._loop.Start();
        host._capture.Start();
        host._backup.Start();
        new SessionWindow(host).Run();
        return host.Shutdown() ? 0 : 1;
    }

    // Les commandes sont datées avant le recalage demandé, que la capture traite ensuite : le compteur
    // avance pendant la veille, l'écart reste celui de la dérive de l'horloge système.
    public void OnLock()
    {
        if (!_shutDown) Apply(_pauses.SetLocked(true, Now()));
    }

    public void OnUnlock()
    {
        if (!_shutDown) Apply(_pauses.SetLocked(false, Now()));
    }

    public void OnSuspend()
    {
        if (_shutDown) return;
        Apply(_pauses.SetSuspended(true, Now()));
        _ = WaitApplied(_commands.Posted, SuspendWait);
    }

    public void OnResume()
    {
        if (_shutDown) return;
        // Même encore verrouillé, l'horloge système a pu bouger pendant la veille : recalage dans tous les cas.
        if (!Apply(_pauses.SetSuspended(false, Now()))) _capture.RequestResync();
    }

    public void OnTimeChange()
    {
        if (_shutDown) return;
        TimeZoneInfo.ClearCachedData();
        var zone = TimeZoneInfo.Local;
        // Une simple correction d'heure (synchronisation réseau) ne coupe pas la session en cours.
        if (zone.Id != _zone.Id || !zone.HasSameRules(_zone))
        {
            _zone = zone;
            Post(new RecorderCommand(Now(), CommandKind.SetZone, Zone: zone));
        }
        _capture.RequestResync();
    }

    public void OnEndSession() => Shutdown();

    public void OnCreated(nint hwnd)
    {
        _tray.Attach(hwnd);
        _control = ControlBridge.Start(hwnd, _dbPath);
    }

    public void OnDestroyed() => _tray.Dispose();

    public void OnIconMessage(nint wParam, nint lParam)
    {
        if (!_shutDown) _tray.OnIconMessage(wParam, lParam);
    }

    public void OnTimer(nuint id)
    {
        if (!_shutDown && TrayController.IsTick(id)) _tray.OnTick();
    }

    public void OnTaskbarCreated() => _tray.OnTaskbarCreated();

    public AgentReply OnControl(AgentCommand command)
    {
        if (_shutDown) return AgentReply.Failed;
        bool done;
        switch (command)
        {
            case AgentCommand.Snooze: done = _tray.Execute(TrayCommand.Snooze); break;
            case AgentCommand.PauseUntilResumed: done = _tray.Execute(TrayCommand.SnoozeUntilResumed); break;
            case AgentCommand.Resume: done = _tray.Execute(TrayCommand.ResumeNow); break;
            case AgentCommand.Quit: done = _tray.Execute(TrayCommand.Quit); break;
            case AgentCommand.Flush:
                // Un autre échec pendant l'attente compte aussi : la réponse reste prudente.
                long failed = _commands.Failed;
                Post(new RecorderCommand(Now(), CommandKind.Flush));
                done = WaitApplied(_commands.Posted, FlushWait) && _commands.Failed == failed;
                break;
            default: return AgentReply.Unknown;
        }
        return done ? AgentReply.Ok : AgentReply.Failed;
    }

    public void Dispose() => Shutdown();

    private long Now() => _capture.Clock.PeekMs();

    /// <summary>
    /// Un message arrivé pendant l'arrêt (le thread principal peut en recevoir pendant les attentes)
    /// est ignoré : la boucle d'analyse ne le lirait plus.
    /// </summary>
    /// <returns>Vrai si un recalage a été demandé.</returns>
    private bool Apply(PauseTransition transition)
    {
        if (_shutDown || transition == PauseTransition.None) return false;
        _signal.Set();
        _tray.Refresh();
        if (transition != PauseTransition.Resumed) return false;
        // Un relâchement a pu se perdre pendant la pause : l'état des touches repart de zéro.
        _capture.RequestResync();
        return true;
    }

    private void Post(in RecorderCommand command)
    {
        _commands.Post(command);
        _signal.Set();
    }

    /// <summary>
    /// Jusqu'à ce que la pompe ait appliqué la commande <paramref name="posted"/> (lot écrit, clé oubliée),
    /// après son léger retard, en réveillant le thread d'analyse.
    /// </summary>
    /// <returns>Vrai si la commande a été appliquée dans le délai.</returns>
    private bool WaitApplied(long posted, TimeSpan timeout)
    {
        long deadline = Environment.TickCount64 + (long)timeout.TotalMilliseconds;
        while (_commands.Applied < posted && Environment.TickCount64 < deadline)
        {
            _signal.Set();
            Thread.Sleep(20);
        }
        return _commands.Applied >= posted;
    }

    /// <summary>
    /// Capture arrêtée, puis tout écrit. Sans arrêt confirmé de l'analyse, la base reste ouverte ;
    /// sans arrêt confirmé de la capture, le signal aussi, ses rappels pouvant encore le lever.
    /// </summary>
    private bool Shutdown()
    {
        if (_shutDown) return _loop.Closed;
        _shutDown = true;
        // D'abord le tube : plus aucune commande ne doit arriver pendant l'arrêt.
        _control?.Dispose();
        _backup.Dispose();
        bool captureStopped = _capture.Stop(CaptureStopTimeout);
        bool stopped = _loop.Stop(StopTimeout);
        if (stopped) _storage.Dispose();
        if (captureStopped)
        {
            _capture.Dispose();
            _signal.Dispose();
        }
        return stopped && _loop.Closed;
    }
}
