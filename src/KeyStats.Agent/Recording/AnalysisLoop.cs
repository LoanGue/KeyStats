using KeyStats.Core.Input;
using KeyStats.Core.Recording;

namespace KeyStats.Agent.Recording;

/// <summary>
/// Thread d'analyse : réveillé à chaque écriture dans la file, il la vide par la <see cref="RingPump"/>
/// et fait avancer l'enregistrement, sur l'heure de l'horloge des événements. Une exception est
/// comptée, jamais propagée : l'agent continue.
/// </summary>
internal sealed class AnalysisLoop(RingPump pump, EventClock clock, EventWaitHandle signal) : IDisposable
{
    private const int IdleWaitMs = 1000;

    private readonly Thread _thread = new(static o => ((AnalysisLoop)o!).Run()) { Name = "KeyStats analysis", IsBackground = true };
    private volatile bool _stop;
    private long _tickErrors;
    private volatile bool _closed;

    public PumpCounters Counters => pump.Counters;

    /// <summary>Exceptions levées hors des événements (lecture de la file, avance du temps, fermeture).</summary>
    public long TickErrors => Volatile.Read(ref _tickErrors);

    /// <summary>Tout a été écrit à l'arrêt.</summary>
    public bool Closed => _closed;

    public void Start() => _thread.Start(this);

    /// <summary>Vide la file une dernière fois, conclut et écrit. La capture doit être arrêtée avant.</summary>
    public bool Stop(TimeSpan timeout)
    {
        if ((_thread.ThreadState & ThreadState.Unstarted) != 0) return true;
        _stop = true;
        signal.Set();
        return _thread.Join(timeout);
    }

    public void Dispose() => Stop(TimeSpan.FromSeconds(10));

    private void Run()
    {
        while (!_stop)
        {
            signal.WaitOne(IdleWaitMs);
            Step(close: false);
        }
        Step(close: true);
    }

    private void Step(bool close)
    {
        try
        {
            pump.Drain();
            if (close) _closed = pump.Close(clock.PeekMs());
            else pump.Tick(clock.PeekMs());
        }
        catch (Exception)
        {
            // Le message pourrait citer une valeur : seul le nombre d'erreurs est gardé.
            Interlocked.Increment(ref _tickErrors);
        }
    }
}
