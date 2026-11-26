using KeyStats.Agent.Capture;
using KeyStats.Agent.Recording;
using KeyStats.Core.Analysis;
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

    private readonly AutoResetEvent _signal = new(false);
    private readonly RawInputCapture _capture;
    private readonly AgentStorage _storage;
    private readonly RecorderCommands _commands = new();
    private readonly PauseSources _pauses;
    private readonly AnalysisLoop _loop;
    private TimeZoneInfo _zone = TimeZoneInfo.Local;
    private bool _shutDown;

    private AgentHost(string dbPath)
    {
        var ring = new EventRing(4096);
        var focus = new FocusChannel();
        _capture = new RawInputCapture(ring, _signal, focus);
        _storage = AgentStorage.Open(dbPath);
        var recorder = new Recorder(_storage.Db, _storage.Archive, new LocalDayClock(_zone), _capture.Clock.PeekMs());
        _pauses = new PauseSources(_commands);
        _loop = new AnalysisLoop(new RingPump(ring, focus, recorder, _commands), _capture.Clock, _signal);
    }

    /// <summary>Tourne jusqu'à la fin de la session Windows.</summary>
    public static int Run(string dbPath)
    {
        using var host = new AgentHost(dbPath);
        host._loop.Start();
        host._capture.Start();
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
        WaitApplied(_commands.Posted, SuspendWait);
    }

    public void OnResume()
    {
        if (_shutDown) return;
        Apply(_pauses.SetSuspended(false, Now()));
        // Même encore verrouillé : l'horloge système a pu bouger pendant la veille.
        _capture.RequestResync();
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

    public void Dispose() => Shutdown();

    private long Now() => _capture.Clock.PeekMs();

    /// <summary>
    /// Un message arrivé pendant l'arrêt (le thread principal peut en recevoir pendant les attentes)
    /// est ignoré : la boucle d'analyse ne le lirait plus.
    /// </summary>
    private void Apply(PauseTransition transition)
    {
        if (transition == PauseTransition.None) return;
        _signal.Set();
        // Un relâchement a pu se perdre pendant la pause : l'état des touches repart de zéro.
        if (transition == PauseTransition.Resumed) _capture.RequestResync();
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
    private void WaitApplied(long posted, TimeSpan timeout)
    {
        long deadline = Environment.TickCount64 + (long)timeout.TotalMilliseconds;
        while (_commands.Applied < posted && Environment.TickCount64 < deadline)
        {
            _signal.Set();
            Thread.Sleep(20);
        }
    }

    /// <summary>
    /// Capture arrêtée, puis tout écrit. Sans arrêt confirmé de l'analyse, la base reste ouverte ;
    /// sans arrêt confirmé de la capture, le signal aussi, ses rappels pouvant encore le lever.
    /// </summary>
    private bool Shutdown()
    {
        if (_shutDown) return _loop.Closed;
        _shutDown = true;
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
