using KeyStats.Core.Focus;
using KeyStats.Core.Input;

namespace KeyStats.Core.Recording;

/// <summary>Compteurs de la pompe, sans contenu.</summary>
public readonly record struct PumpCounters(long Events, long Errors, long UnknownMarkers);

/// <summary>
/// Côté consommateur de l'<see cref="EventRing"/>, sur le thread d'analyse : remplace chaque marqueur
/// de fenêtre par le changement publié, passe le reste au <see cref="Recorder"/> et le fait avancer.
/// Une exception est comptée pour l'événement fautif seul ; les suivants, marqueurs compris, passent.
/// </summary>
public sealed class RingPump(EventRing ring, FocusChannel focus, Recorder recorder)
{
    public const int BatchSize = 256;
    // L'heure donnée au Tick a un léger retard, pour qu'une frappe encore en route ne paraisse pas en avance.
    // Un thread de capture suspendu plus longtemps entre l'horodatage et l'écriture reste possible : rare.
    public const int TickLagMs = 250;

    private readonly InputEvent[] _batch = new InputEvent[BatchSize];
    private long _lastMs = long.MinValue, _events, _errors, _unknownMarkers;

    /// <summary>Lu par un autre thread une fois la pompe arrêtée.</summary>
    public PumpCounters Counters =>
        new(Volatile.Read(ref _events), Volatile.Read(ref _errors), Volatile.Read(ref _unknownMarkers));

    public void Drain()
    {
        for (int n; (n = ring.Read(_batch)) > 0;)
        {
            var batch = _batch.AsSpan(0, n);
            foreach (ref readonly var e in batch) Dispatch(e);
            _lastMs = Math.Max(_lastMs, batch[^1].Ms);
            Volatile.Write(ref _events, _events + n);
            // Le brut en clair ne reste pas dans le lot (la file, elle, le garde jusqu'à ce que la place resserve).
            batch.Clear();
        }
    }

    /// <param name="clockMs">Heure de l'horloge des événements (<see cref="EventClock.PeekMs"/>).</param>
    public void Tick(long clockMs) => recorder.Tick(Now(clockMs));

    public bool Close(long clockMs) => recorder.Close(Now(clockMs));

    /// <summary>Jamais avant le dernier événement vu, pour que le temps ne recule pas dans le Recorder.</summary>
    private long Now(long clockMs)
    {
        _lastMs = Math.Max(_lastMs, clockMs - TickLagMs);
        return _lastMs;
    }

    private void Dispatch(in InputEvent e)
    {
        try
        {
            if (e.Kind != EventKind.Focus)
                recorder.OnEvent(e);
            else if (focus.Take(e) is { } change)
                recorder.OnFocus(change);
            else
                Volatile.Write(ref _unknownMarkers, _unknownMarkers + 1);
        }
        catch (Exception)
        {
            // Le message pourrait citer une valeur : seul le nombre d'erreurs est gardé.
            Volatile.Write(ref _errors, _errors + 1);
        }
    }
}
