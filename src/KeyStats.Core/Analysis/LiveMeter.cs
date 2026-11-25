namespace KeyStats.Core.Analysis;

public enum LiveMode : byte
{
    Hidden,
    /// <summary>Frappe en cours : vitesse sur les dernières secondes.</summary>
    Typing,
    /// <summary>Juste après une rafale : bilan affiché quelques secondes.</summary>
    Summary,
}

/// <param name="Accuracy">Entre 0 et 1 ; en mode frappe, celle de la rafale en cours.</param>
public readonly record struct LiveReading(LiveMode Mode, double Wpm, double Cpm, double Accuracy);

/// <summary>
/// Données du compteur en direct. Alimenté par le thread d'analyse, lu par l'interface : protégé
/// par un verrou, sans allocation. Ne garde que des horodatages, jamais les caractères.
/// </summary>
public sealed class LiveMeter
{
    public const int IdleMs = 4000, SummaryMs = 3000;
    private const int Ring = 512;

    private readonly AnalysisSettings _s;
    private readonly Lock _lock = new();
    private readonly long[] _ms = new long[Ring];
    private readonly bool[] _space = new bool[Ring];
    private int _head, _count;
    private long _lastMs = long.MinValue;
    private bool _visible;

    // Rafale en cours (pour le bilan) : suite d'activité sans arrêt de 4 s.
    private int _chars, _counted, _deleted, _typos;
    private long _activeMs;

    public LiveMeter(AnalysisSettings? settings = null) => _s = settings ?? AnalysisSettings.Default;

    public void OnChar(long ms, char c)
    {
        lock (_lock)
        {
            long gap = Touch(ms);
            _chars++;
            if (gap >= 0 && gap <= _s.ActiveGapMs) _counted++;
            _ms[_head] = ms;
            _space[_head] = CharClass.IsWhitespace(c);
            _head = (_head + 1) % Ring;
            if (_count < Ring) _count++;
            if (!_visible) _visible = ShouldShow(ms);
        }
    }

    public void OnDelete(long ms, int n)
    {
        lock (_lock)
        {
            Touch(ms);
            _deleted += n;
        }
    }

    public void OnTypos(int n)
    {
        lock (_lock) _typos += n;
    }

    /// <summary>Pause, verrouillage, appli exclue : tout est oublié.</summary>
    public void Reset()
    {
        lock (_lock)
        {
            _count = _head = 0;
            _lastMs = long.MinValue;
            _visible = false;
            ResetStretch();
        }
    }

    public LiveReading Read(long now)
    {
        lock (_lock)
        {
            if (!_visible) return default;
            long idle = now - _lastMs;
            double accuracy = _chars > 0 ? Math.Clamp(1 - (double)_typos / _chars, 0, 1) : 1;
            if (idle < IdleMs)
            {
                double cpm = WindowCpm(now);
                return new LiveReading(LiveMode.Typing, cpm / 5, cpm, accuracy);
            }
            if (idle < IdleMs + SummaryMs)
            {
                double cpm = _activeMs > 0 ? Math.Max(0, _counted - _deleted) * 60000.0 / _activeMs : 0;
                return new LiveReading(LiveMode.Summary, cpm / 5, cpm, accuracy);
            }
            _visible = false;
            return default;
        }
    }

    /// <summary>Note une activité ; renvoie l'écart avec la précédente (-1 s'il n'y en a pas).</summary>
    private long Touch(long ms)
    {
        long gap = _lastMs == long.MinValue ? -1 : ms - _lastMs;
        if (gap < 0 || gap >= IdleMs)
        {
            ResetStretch();
            // Reprise pendant le bilan : le compteur reste affiché.
            if (gap < 0 || gap >= IdleMs + SummaryMs) _visible = false;
        }
        else if (gap <= _s.ActiveGapMs) _activeMs += gap;
        _lastMs = ms;
        return gap;
    }

    private void ResetStretch()
    {
        _chars = _counted = _deleted = _typos = 0;
        _activeMs = 0;
    }

    /// <summary>Vraie frappe : assez de caractères et d'espaces dans les dernières secondes.</summary>
    private bool ShouldShow(long now)
    {
        int chars = 0, spaces = 0;
        for (int i = 0; i < _count; i++)
        {
            int idx = (_head - 1 - i + Ring) % Ring;
            if (_ms[idx] <= now - _s.LiveShowWindowMs) break;
            chars++;
            if (_space[idx]) spaces++;
        }
        return chars >= _s.LiveShowMinChars && spaces >= _s.LiveShowMinSpaces;
    }

    /// <summary>Caractères par minute de frappe active sur la fenêtre glissante, pauses exclues.</summary>
    private double WindowCpm(long now)
    {
        long from = now - _s.LiveSpeedWindowMs, active = 0;
        int counted = 0;
        for (int i = 0; i < _count - 1; i++)
        {
            int cur = (_head - 1 - i + Ring) % Ring, prev = (cur - 1 + Ring) % Ring;
            if (_ms[prev] <= from) break;
            long gap = _ms[cur] - _ms[prev];
            if (gap >= 0 && gap <= _s.ActiveGapMs)
            {
                counted++;
                active += gap;
            }
        }
        return active > 0 ? counted * 60000.0 / active : 0;
    }
}
