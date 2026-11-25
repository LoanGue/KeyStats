using System.Diagnostics;

namespace KeyStats.Core.Input;

/// <summary>
/// Horodatage des événements en ms UTC à partir du compteur haute résolution, recalé sur l'horloge
/// système à la création et par <see cref="Resync"/> (changement d'heure, sortie de veille). Les
/// valeurs rendues ne reculent jamais : les blocs bruts exigent des événements croissants.
/// <see cref="NowMs"/> et <see cref="Resync"/> appartiennent au thread de capture ; <see cref="PeekMs"/>
/// peut être lu par un autre thread, sur la même échelle de temps.
/// </summary>
public sealed class EventClock
{
    private readonly Func<long> _timestamp;
    private readonly Func<long> _utcMs;
    private readonly long _frequency;
    // Remplacé d'un bloc à chaque recalage : un lecteur d'un autre thread voit l'ancien ou le nouveau, jamais un mélange.
    private Anchor _anchor = new(0, 0);
    private long _lastMs = long.MinValue;

    public EventClock()
        : this(Stopwatch.GetTimestamp, static () => DateTimeOffset.UtcNow.ToUnixTimeMilliseconds(), Stopwatch.Frequency)
    {
    }

    public EventClock(Func<long> timestamp, Func<long> utcMs, long frequency)
    {
        ArgumentNullException.ThrowIfNull(timestamp);
        ArgumentNullException.ThrowIfNull(utcMs);
        ArgumentOutOfRangeException.ThrowIfLessThan(frequency, 1000);
        _timestamp = timestamp;
        _utcMs = utcMs;
        _frequency = frequency;
        Resync();
    }

    /// <summary>
    /// Recale sur l'horloge système. Si elle a reculé, on repart du dernier instant rendu : le temps
    /// continue d'avancer au rythme du compteur au lieu de rester figé jusqu'au rattrapage.
    /// </summary>
    public void Resync()
    {
        Volatile.Write(ref _anchor, new Anchor(_timestamp(), Math.Max(_utcMs(), _lastMs)));
    }

    public long NowMs()
    {
        long ms = Math.Max(Project(_anchor), _lastMs);
        _lastMs = ms;
        return ms;
    }

    /// <summary>
    /// Heure courante sur l'échelle des événements, depuis n'importe quel thread. Peut être un peu
    /// en retard sur le dernier <see cref="NowMs"/> juste après un recalage : l'appelant garde son maximum.
    /// </summary>
    public long PeekMs() => Project(Volatile.Read(ref _anchor));

    private long Project(Anchor a) => a.Ms + (_timestamp() - a.Ticks) * 1000 / _frequency;

    private sealed record Anchor(long Ticks, long Ms);
}
