using KeyStats.Core.Focus;
using KeyStats.Core.Input;

namespace KeyStats.Core.Recording;

/// <summary>Compteurs de la pompe, sans contenu.</summary>
public readonly record struct PumpCounters(long Events, long Errors, long UnknownMarkers);

/// <summary>
/// Côté consommateur de l'<see cref="EventRing"/>, sur le thread d'analyse : remplace chaque marqueur
/// de fenêtre par le changement publié, passe le reste au <see cref="Recorder"/> et le fait avancer.
/// Les <see cref="RecorderCommands"/> sont appliquées entre les frappes, dans l'ordre du temps.
/// Une exception est comptée pour l'événement ou la commande fautifs seuls ; les suivants passent.
/// </summary>
public sealed class RingPump(EventRing ring, FocusChannel focus, Recorder recorder, RecorderCommands? commands = null)
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
            foreach (ref readonly var e in batch)
            {
                ApplyCommands(e.Ms);
                Dispatch(e);
            }
            _lastMs = Math.Max(_lastMs, batch[^1].Ms);
            Volatile.Write(ref _events, _events + n);
            // Le brut en clair ne reste pas dans le lot (la file, elle, le garde jusqu'à ce que la place resserve).
            batch.Clear();
        }
    }

    /// <param name="clockMs">Heure de l'horloge des événements (<see cref="EventClock.PeekMs"/>).</param>
    public void Tick(long clockMs)
    {
        // Une commande plus récente attend : une frappe encore en route pourrait la précéder.
        ApplyCommands(clockMs - TickLagMs);
        recorder.Tick(Now(clockMs));
    }

    /// <summary>Arrêt : toutes les commandes en attente passent, puis tout est écrit.</summary>
    public bool Close(long clockMs)
    {
        ApplyCommands(long.MaxValue);
        return recorder.Close(Now(clockMs));
    }

    private void ApplyCommands(long untilMs)
    {
        if (commands is null) return;
        while (commands.TryTake(untilMs, out var c))
        {
            _lastMs = Math.Max(_lastMs, c.Ms);
            try
            {
                Apply(c, _lastMs);
            }
            catch (Exception)
            {
                Volatile.Write(ref _errors, _errors + 1);
            }
        }
    }

    private void Apply(in RecorderCommand c, long ms)
    {
        switch (c.Kind)
        {
            case CommandKind.Pause: recorder.BeginPause(c.Reason, ms); break;
            case CommandKind.Resume: recorder.EndPause(ms); break;
            case CommandKind.DropEpoch: recorder.DropEpoch(ms); break;
            case CommandKind.SetZone: recorder.SetZone(c.Zone!, ms); break;
            case CommandKind.Flush: recorder.Flush(ms); break;
        }
    }

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
