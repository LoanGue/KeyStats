using KeyStats.Agent.Interop;
using KeyStats.Core.Focus;
using KeyStats.Core.Input;
using KeyStats.Core.Recording;

namespace KeyStats.Agent.Recording;

/// <summary>Compteurs du thread d'analyse, sans contenu.</summary>
internal readonly record struct LoopCounters(long Events, long Resets, long Errors, long UnknownMarkers);

/// <summary>
/// Thread d'analyse : vide la file d'événements dans le <see cref="Recorder"/>, le fait avancer quand
/// la file est vide et remet l'état des touches à zéro si la file a perdu des événements (un
/// relâchement a pu disparaître). Une exception est comptée, jamais propagée : l'agent continue.
/// </summary>
internal sealed class AnalysisLoop : IDisposable
{
    private const int BatchSize = 256;
    private const int IdleWaitMs = 1000;
    // L'heure donnée au Tick a un léger retard, pour qu'une frappe encore en route ne paraisse pas en avance.
    private const int TickLagMs = 250;
    private const int VK_CAPITAL = 0x14, VK_NUMLOCK = 0x90;

    private readonly EventRing _ring;
    private readonly FocusChannel _focus;
    private readonly EventWaitHandle _signal;
    private readonly Recorder _recorder;
    private readonly Thread _thread;
    private readonly InputEvent[] _batch = new InputEvent[BatchSize];
    // Délégués créés une fois : la boucle se réveille à chaque frappe et ne doit rien allouer.
    private readonly Action _drain, _tick, _close;
    private volatile bool _stop;
    private long _dropped, _lastMs, _events, _resets, _errors, _unknownMarkers;

    public AnalysisLoop(EventRing ring, FocusChannel focus, EventWaitHandle signal, Recorder recorder)
    {
        _ring = ring;
        _focus = focus;
        _signal = signal;
        _recorder = recorder;
        _thread = new Thread(Run) { Name = "KeyStats analysis", IsBackground = true };
        _drain = Drain;
        _tick = () => _recorder.Tick(Now());
        _close = () => _recorder.Close(Now());
    }

    /// <summary>Lu par un autre thread après <see cref="Stop"/>.</summary>
    public LoopCounters Counters => new(Volatile.Read(ref _events), Volatile.Read(ref _resets), Volatile.Read(ref _errors), Volatile.Read(ref _unknownMarkers));

    public void Start() => _thread.Start();

    /// <summary>Vide la file une dernière fois, conclut et écrit. La capture doit être arrêtée avant.</summary>
    public bool Stop(TimeSpan timeout)
    {
        _stop = true;
        _signal.Set();
        return _thread.Join(timeout);
    }

    public void Dispose() => Stop(TimeSpan.FromSeconds(10));

    private void Run()
    {
        while (!_stop)
        {
            _signal.WaitOne(IdleWaitMs);
            Guard(_drain);
            Guard(_tick);
        }
        Guard(_drain);
        Guard(_close);
    }

    private void Drain()
    {
        for (int n; (n = _ring.Read(_batch)) > 0;)
        {
            var batch = _batch.AsSpan(0, n);
            long dropped = _ring.Dropped;
            if (dropped != _dropped)
            {
                // Les événements perdus précèdent ce lot : la remise à zéro passe avant lui.
                _dropped = dropped;
                _recorder.OnEvent(Reset(Math.Max(_lastMs, batch[0].Ms)));
                Interlocked.Increment(ref _resets);
            }
            _lastMs = Math.Max(_lastMs, batch[^1].Ms);
            Interlocked.Add(ref _events, n);
            foreach (ref readonly var e in batch) Dispatch(e);
            batch.Clear();
        }
    }

    /// <summary>
    /// Un marqueur de fenêtre est remplacé par le changement publié par le thread de capture. Un
    /// marqueur sans changement (file de fenêtres pleine) est seulement compté : la fenêtre suivante
    /// le rattrape.
    /// </summary>
    private void Dispatch(in InputEvent e)
    {
        if (e.Kind != EventKind.Focus)
            _recorder.OnEvent(e);
        else if (_focus.Take(e) is { } change)
            _recorder.OnFocus(change);
        else
            Interlocked.Increment(ref _unknownMarkers);
    }

    /// <summary>Heure du Tick : jamais avant le dernier événement vu, pour que le temps n'y recule pas.</summary>
    private long Now()
    {
        _lastMs = Math.Max(_lastMs, DateTimeOffset.UtcNow.ToUnixTimeMilliseconds() - TickLagMs);
        return _lastMs;
    }

    /// <summary>Remise à zéro avec l'état actuel de Verr Maj et Verr Num.</summary>
    private static InputEvent Reset(long ms)
    {
        var locks = LockKeys.None;
        if ((User32.GetKeyState(VK_CAPITAL) & 1) != 0) locks |= LockKeys.CapsLock;
        if ((User32.GetKeyState(VK_NUMLOCK) & 1) != 0) locks |= LockKeys.NumLock;
        return new InputEvent(ms, EventKind.Reset, 0, 0, (byte)locks);
    }

    private void Guard(Action step)
    {
        try
        {
            step();
        }
        catch (Exception)
        {
            // Le message pourrait citer une valeur : seul le nombre d'erreurs est gardé.
            Interlocked.Increment(ref _errors);
        }
    }
}
