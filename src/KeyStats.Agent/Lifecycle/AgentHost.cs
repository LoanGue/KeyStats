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
    private static readonly TimeSpan StopTimeout = TimeSpan.FromSeconds(4);
    // Windows laisse environ 2 s avant la veille : l'écriture du lot doit tenir dedans.
    private static readonly TimeSpan SuspendWait = TimeSpan.FromSeconds(1.5);

    private readonly AutoResetEvent _signal = new(false);
    private readonly RawInputCapture _capture;
    private readonly AgentStorage _storage;
    private readonly RecorderCommands _commands = new();
    private readonly AnalysisLoop _loop;
    private bool _locked, _suspended, _shutDown;

    private AgentHost(string dbPath)
    {
        var ring = new EventRing(4096);
        var focus = new FocusChannel();
        _capture = new RawInputCapture(ring, _signal, focus);
        _storage = AgentStorage.Open(dbPath);
        var recorder = new Recorder(_storage.Db, _storage.Archive, new LocalDayClock(), _capture.Clock.PeekMs());
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

    public void OnLock()
    {
        if (!_suspended) Pause();
        _locked = true;
    }

    public void OnUnlock()
    {
        _locked = false;
        Resume();
    }

    public void OnSuspend()
    {
        if (!_locked && !_suspended) Pause();
        _suspended = true;
        WaitApplied(SuspendWait);
    }

    public void OnResume()
    {
        // Deux messages de réveil se suivent : le second ne fait rien.
        if (!_suspended) return;
        _suspended = false;
        Resume();
    }

    public void OnTimeChange()
    {
        TimeZoneInfo.ClearCachedData();
        Post(new RecorderCommand(_capture.Clock.PeekMs(), CommandKind.SetZone, Zone: TimeZoneInfo.Local));
        _capture.RequestResync();
    }

    public void OnEndSession() => Shutdown();

    public void Dispose() => Shutdown();

    private void Pause()
    {
        long now = _capture.Clock.PeekMs();
        Post(new RecorderCommand(now, CommandKind.Pause, PauseReason.Locked));
        Post(new RecorderCommand(now, CommandKind.DropEpoch));
    }

    /// <summary>Reprise quand ni le verrouillage ni la veille ne tiennent plus.</summary>
    private void Resume()
    {
        if (_locked || _suspended) return;
        Post(new RecorderCommand(_capture.Clock.PeekMs(), CommandKind.Resume));
        // Un relâchement a pu se perdre : l'état des touches repart de zéro, l'horloge est recalée.
        _capture.RequestResync();
    }

    private void Post(in RecorderCommand command)
    {
        _commands.Post(command);
        _signal.Set();
    }

    /// <summary>Les commandes passent après le léger retard de la pompe, au réveil suivant du thread d'analyse.</summary>
    private void WaitApplied(TimeSpan timeout)
    {
        long deadline = Environment.TickCount64 + (long)timeout.TotalMilliseconds;
        while (!_commands.IsEmpty && Environment.TickCount64 < deadline)
        {
            _signal.Set();
            Thread.Sleep(50);
        }
    }

    /// <summary>Capture arrêtée, puis tout écrit. Sans arrêt confirmé de l'analyse, la base reste ouverte.</summary>
    private bool Shutdown()
    {
        if (_shutDown) return _loop.Closed;
        _shutDown = true;
        _capture.Stop();
        bool stopped = _loop.Stop(StopTimeout);
        if (stopped) _storage.Dispose();
        _capture.Dispose();
        _signal.Dispose();
        return stopped && _loop.Closed;
    }
}
