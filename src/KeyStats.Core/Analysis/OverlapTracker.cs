using KeyStats.Core.Input;

namespace KeyStats.Core.Analysis;

/// <summary>
/// Durée des chevauchements entre deux touches de caractère : une touche enfoncée avant le relâchement de la
/// précédente. Le chevauchement court de ce second appui au premier des deux relâchements. Il est rangé dans
/// l'histogramme <see cref="HistogramKind.OverlapMs"/> du jour du second appui, sous-clé = la paire de touches
/// (<see cref="Sub"/>). Un chevauchement plus long que <paramref name="capMs"/> (une touche tenue exprès pendant
/// qu'on en tape d'autres) n'est pas un roulement et n'est pas mesuré. Tableaux fixes : aucune allocation par frappe.
/// </summary>
internal sealed class OverlapTracker(StatsAccumulator stats, int capMs)
{
    /// <summary>Chevauchements suivis en même temps ; au-delà, les suivants ne sont pas mesurés.</summary>
    public const int MaxPending = 8;

    private readonly ushort[] _first = new ushort[MaxPending], _second = new ushort[MaxPending];
    private readonly long[] _startMs = new long[MaxPending];
    private int _count;

    /// <summary>Sous-clé d'histogramme de la paire : touche tenue, puis touche enfoncée par-dessus.</summary>
    public static int Sub(ushort first, ushort second) => first * KeyCode.Count + second;

    public static (ushort First, ushort Second) Pair(int sub) => ((ushort)(sub / KeyCode.Count), (ushort)(sub % KeyCode.Count));

    /// <summary><paramref name="second"/> enfoncée à <paramref name="ms"/> pendant que <paramref name="first"/> est tenue.</summary>
    public void Start(ushort first, ushort second, long ms)
    {
        if (_count == MaxPending || first == second) return;
        _first[_count] = first;
        _second[_count] = second;
        _startMs[_count] = ms;
        _count++;
    }

    /// <summary>Relâchement de <paramref name="key"/> : clôt les chevauchements où elle figure.</summary>
    public void Release(ushort key, long ms)
    {
        for (int i = _count - 1; i >= 0; i--)
        {
            if (_first[i] != key && _second[i] != key) continue;
            long start = _startMs[i], duration = Math.Max(0, ms - start);
            if (duration <= capMs)
                stats.AddHistogram(stats.DayKey(start), HistogramKind.OverlapMs, Sub(_first[i], _second[i]), LogScale.Overlap.Bucket(duration));
            RemoveAt(i);
        }
    }

    /// <summary>Relâchement perdu ou hors mode : les chevauchements de <paramref name="key"/> sont abandonnés.</summary>
    public void Cancel(ushort key)
    {
        for (int i = _count - 1; i >= 0; i--)
            if (_first[i] == key || _second[i] == key) RemoveAt(i);
    }

    public void Clear() => _count = 0;

    private void RemoveAt(int i)
    {
        _count--;
        _first[i] = _first[_count];
        _second[i] = _second[_count];
        _startMs[i] = _startMs[_count];
    }
}
