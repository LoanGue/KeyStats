using System.Diagnostics;

namespace KeyStats.Core.Input;

/// <summary>
/// Horodatage des événements en ms UTC à partir du compteur haute résolution, recalé sur l'horloge
/// système à la création et par <see cref="Resync"/> (changement d'heure, sortie de veille). Les
/// valeurs rendues ne reculent jamais : les blocs bruts exigent des événements croissants.
/// Pas sûr entre threads : il vit sur le thread de capture.
/// </summary>
public sealed class EventClock
{
    private readonly Func<long> _timestamp;
    private readonly Func<long> _utcMs;
    private readonly long _frequency;
    private long _baseTicks, _baseMs, _lastMs = long.MinValue;

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
        _baseTicks = _timestamp();
        _baseMs = Math.Max(_utcMs(), _lastMs);
    }

    public long NowMs()
    {
        long ms = _baseMs + (_timestamp() - _baseTicks) * 1000 / _frequency;
        if (ms < _lastMs) ms = _lastMs;
        _lastMs = ms;
        return ms;
    }
}
