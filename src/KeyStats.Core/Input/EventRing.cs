using System.Numerics;

namespace KeyStats.Core.Input;

/// <summary>
/// File circulaire d'événements entre le thread de capture (seul producteur) et le thread d'analyse
/// (seul consommateur). Préallouée, sans verrou ni allocation par événement. Pleine, elle refuse
/// l'événement et le compte dans <see cref="Dropped"/> : le consommateur qui voit ce compteur bouger
/// remet l'état des touches à zéro, car un relâchement a pu se perdre.
/// </summary>
public sealed class EventRing
{
    private readonly InputEvent[] _items;
    private readonly int _mask;
    private long _head; // écrit par le producteur seul
    private long _tail; // écrit par le consommateur seul
    private long _dropped;

    public EventRing(int capacity)
    {
        if (capacity < 2 || !BitOperations.IsPow2(capacity))
            throw new ArgumentOutOfRangeException(nameof(capacity), "Puissance de deux attendue.");
        _items = new InputEvent[capacity];
        _mask = capacity - 1;
    }

    public int Capacity => _items.Length;

    /// <summary>Événements refusés faute de place, depuis la création.</summary>
    public long Dropped => Volatile.Read(ref _dropped);

    public int Count => (int)(Volatile.Read(ref _head) - Volatile.Read(ref _tail));

    /// <summary>Producteur seulement.</summary>
    public bool TryWrite(in InputEvent e)
    {
        long head = _head;
        if (head - Volatile.Read(ref _tail) >= _items.Length)
        {
            Volatile.Write(ref _dropped, _dropped + 1);
            return false;
        }
        _items[head & _mask] = e;
        Volatile.Write(ref _head, head + 1);
        return true;
    }

    /// <summary>Consommateur seulement. Copie au plus <c>dest.Length</c> événements, dans l'ordre.</summary>
    public int Read(Span<InputEvent> dest)
    {
        long tail = _tail;
        int n = (int)Math.Min(Volatile.Read(ref _head) - tail, dest.Length);
        for (int i = 0; i < n; i++) dest[i] = _items[(tail + i) & _mask];
        Volatile.Write(ref _tail, tail + n);
        return n;
    }
}
