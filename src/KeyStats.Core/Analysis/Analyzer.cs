using KeyStats.Core.Input;
using KeyStats.Core.Text;

namespace KeyStats.Core.Analysis;

/// <summary>Fenêtre active à partir d'un instant (une ligne de la table <c>focus</c>).</summary>
/// <param name="TitleId">Titre de fenêtre (table <c>window_title</c>), 0 si inconnu.</param>
/// <param name="Layout">Disposition clavier de la fenêtre ; null : on garde la précédente.</param>
public readonly record struct FocusInfo(long Ms, int AppId, long TitleId, LayoutTable? Layout, FocusState Flags);

/// <summary>
/// Pipeline d'analyse : suit l'état des touches, traduit en caractères, découpe les sessions,
/// analyse les corrections et remplit les statistiques. Le même code sert en direct (agent) et pour
/// recalculer l'historique depuis le brut : toute décision ne dépend que des horodatages des
/// événements, pas de l'heure à laquelle ils sont traités. Un seul thread à la fois.
/// </summary>
public sealed class Analyzer
{
    private readonly AnalysisSettings _s;
    private readonly StatsAccumulator _stats;
    private readonly SessionBuilder _session;
    private readonly CorrectionAnalyzer _corrections;
    private readonly LiveMeter? _live;

    private int _appId;
    private long _titleId;
    private LayoutTable _layout = LayoutTable.FrenchAzerty;
    private bool _excluded;

    private readonly bool[] _down = new bool[KeyCode.Count];
    private readonly long[] _downMs = new long[KeyCode.Count];
    private readonly bool[] _textDown = new bool[KeyCode.Count];
    // Touches tenues au passage en mode compteurs seuls (ou à sa sortie) : leurs répétitions et leur
    // relâchement appartiennent à l'autre mode et sont ignorés.
    private readonly bool[] _stale = new bool[KeyCode.Count];
    private int _downCount, _textDownCount;
    private long _lastTextUpMs = long.MinValue;
    private bool _capsLock, _numLock;
    private bool _altAlone, _winAlone;
    private char _dead;
    private int _altValue, _altDigits;
    private bool _altLeadingZero;

    // Faux Ctrl gauche que Windows envoie avant AltGr.
    private InputEvent _pendingCtrl;
    private bool _hasPendingCtrl, _fakeCtrl;
    private long _fakeCtrlUntil = long.MinValue;

    public Analyzer(StatsAccumulator stats, AnalysisSettings? settings = null, LiveMeter? live = null)
    {
        _stats = stats;
        _s = settings ?? AnalysisSettings.Default;
        _live = live;
        _session = new SessionBuilder(stats, _s, live);
        _corrections = new CorrectionAnalyzer(_session, _s);
    }

    public StatsAccumulator Stats => _stats;

    public bool InSession => _session.IsOpen;

    public LayoutTable Layout => _layout;

    /// <summary>Changement de fenêtre active, de titre ou de disposition, dans l'ordre des événements.</summary>
    public void SetFocus(in FocusInfo f)
    {
        // Un Ctrl en attente appartient à la fenêtre qu'on quitte : il est tranché sous l'ancien mode.
        if (_hasPendingCtrl)
        {
            _hasPendingCtrl = false;
            ProcessCore(_pendingCtrl);
        }
        CheckGap(f.Ms);
        bool newWindow = (f.Flags & FocusState.NewWindow) != 0 || f.AppId != _appId;
        bool excluded = f.Flags.CountersOnly();
        if (newWindow)
        {
            EndSession();
            _dead = '\0';
            ResetAltCode();
        }
        else if (_session.IsOpen) _session.SetTitle(f.TitleId);
        if (excluded != _excluded)
        {
            // Les touches tenues d'un côté ne doivent rien déclencher de l'autre.
            EndSession();
            IsolateHeldKeys();
            if (excluded) _live?.Reset();
        }
        _appId = f.AppId;
        _titleId = f.TitleId;
        if (f.Layout is not null) _layout = f.Layout;
        _excluded = excluded;
    }

    public void Process(in InputEvent e)
    {
        if (_hasPendingCtrl)
        {
            _hasPendingCtrl = false;
            if (e.Kind == EventKind.KeyDown && e.Key == KeyCode.RightAlt && e.Ms - _pendingCtrl.Ms <= _s.FakeCtrlMs)
                _fakeCtrl = true;
            else ProcessCore(_pendingCtrl);
        }
        if (e.Key == KeyCode.LeftCtrl && e.IsKeyboard)
        {
            if (_fakeCtrl || e.Ms <= _fakeCtrlUntil) return;
            if (e.Kind == EventKind.KeyDown && !_down[KeyCode.LeftCtrl] && _layout.HasAltGr)
            {
                // Peut-être le faux Ctrl d'AltGr : on attend l'événement suivant pour trancher.
                _pendingCtrl = e;
                _hasPendingCtrl = true;
                return;
            }
        }
        ProcessCore(e);
    }

    /// <summary>
    /// Appelé régulièrement par l'agent quand la file est vide (avec un léger retard sur l'heure
    /// réelle) : un Ctrl seul est un vrai Ctrl, et une session sans frappe depuis 5 s se termine.
    /// </summary>
    public void Tick(long now)
    {
        if (_hasPendingCtrl && now - _pendingCtrl.Ms > _s.FakeCtrlMs)
        {
            _hasPendingCtrl = false;
            ProcessCore(_pendingCtrl);
        }
        CheckGap(now);
    }

    /// <summary>Fin du flux (arrêt de l'agent, fin d'un recalcul) : tout ce qui est en cours est conclu.</summary>
    public void EndAll()
    {
        if (_hasPendingCtrl)
        {
            _hasPendingCtrl = false;
            ProcessCore(_pendingCtrl);
        }
        EndSession();
    }

    private void ProcessCore(in InputEvent e)
    {
        CheckGap(e.Ms);
        switch (e.Kind)
        {
            case EventKind.KeyDown: KeyDown(e); break;
            case EventKind.KeyUp or EventKind.ForcedUp: KeyUp(e); break;
            case EventKind.MouseDown: MouseDown(e.Ms); break;
            case EventKind.Reset:
                EndSession();
                ClearKeyState();
                _live?.Reset();
                _capsLock = ((LockKeys)e.Flags & LockKeys.CapsLock) != 0;
                _numLock = ((LockKeys)e.Flags & LockKeys.NumLock) != 0;
                break;
        }
    }

    private void CheckGap(long ms)
    {
        if (_session.IsOpen && ms - _session.LastTextMs > _s.SessionGapMs) EndSession();
    }

    private void EndSession()
    {
        // Les corrections en attente sont conclues avant la session, qui les compte.
        _corrections.Invalidate();
        _session.End();
    }

    /// <summary>
    /// Bascule de mode : les touches tenues restent enfoncées (Maj garde son effet), mais leurs
    /// répétitions et leur relâchement ne comptent plus. Rien de ce qui est en cours ne traverse.
    /// </summary>
    private void IsolateHeldKeys()
    {
        for (int k = 0; k < _down.Length; k++)
            if (_down[k]) _stale[k] = true;
        _lastTextUpMs = long.MinValue;
        _altAlone = _winAlone = false;
        _dead = '\0';
        ResetAltCode();
    }

    private void ClearKeyState()
    {
        Array.Clear(_down);
        Array.Clear(_stale);
        Array.Clear(_textDown);
        _downCount = _textDownCount = 0;
        _lastTextUpMs = long.MinValue;
        _altAlone = _winAlone = false;
        _dead = '\0';
        ResetAltCode();
        _fakeCtrl = false;
        _fakeCtrlUntil = long.MinValue;
    }

    private void ResetAltCode()
    {
        _altValue = _altDigits = 0;
        _altLeadingZero = false;
    }

    private void KeyDown(in InputEvent e)
    {
        ushort k = e.Key;
        if (k == 0 || k >= KeyCode.Count) return;
        long ms = e.Ms;
        if (_stale[k]) return; // répétition d'une touche tenue depuis l'autre mode
        bool repeat = _down[k];
        if (!repeat)
        {
            _down[k] = true;
            _downMs[k] = ms;
            _downCount++;
        }
        if (_excluded)
        {
            if (!repeat) _stats.AddExcludedKey(ms);
            return;
        }

        _stats.AddKeyPress(ms, k, e.Device, repeat, _appId);
        if (!repeat)
        {
            _stats.AddHistogram(_stats.DayKey(ms), HistogramKind.Chord, 0, Math.Min(_downCount, StatsAccumulator.ChordBuckets - 1));
            if (IsPlainAlt(k))
            {
                _altAlone = true;
                _winAlone = false;
                ResetAltCode();
            }
            else if (k is KeyCode.LeftWin or KeyCode.RightWin)
            {
                _winAlone = true;
                _altAlone = false;
            }
            else _altAlone = _winAlone = false;
            if (k == KeyCode.CapsLock) _capsLock = !_capsLock;
            else if (k == KeyCode.NumLock) _numLock = !_numLock;
        }

        TextAction action = KeyTranslator.Translate(_down, _layout, _capsLock, _numLock, k, out char c, out ShortcutModifiers mods);
        Dispatch(action, k, c, mods, ms, e.Device, repeat);
    }

    private void KeyUp(in InputEvent e)
    {
        ushort k = e.Key;
        if (k == 0 || k >= KeyCode.Count) return;
        long ms = e.Ms;
        if (k == KeyCode.RightAlt && _fakeCtrl)
        {
            _fakeCtrl = false;
            _fakeCtrlUntil = ms + _s.FakeCtrlMs;
        }
        if (!_down[k]) return;
        _down[k] = false;
        _downCount--;
        if (_textDown[k])
        {
            _textDown[k] = false;
            _textDownCount--;
            if (e.Kind != EventKind.ForcedUp) _lastTextUpMs = ms;
        }
        bool stale = _stale[k];
        _stale[k] = false;
        if (e.Kind == EventKind.ForcedUp || stale)
        {
            // Relâchement perdu : on corrige l'état sans rien en déduire.
            if (IsPlainAlt(k)) ResetAltCode();
            _altAlone = _winAlone = false;
            return;
        }
        if (_excluded) return;

        long held = Math.Clamp(ms - _downMs[k], 0, _s.HoldCapMs);
        _stats.AddHistogram(_stats.DayKey(ms), HistogramKind.HoldMs, k, LogScale.Hold.Bucket(held));

        if (IsPlainAlt(k))
        {
            if (_altDigits > 0)
            {
                char c = AltCodes.Decode(_altValue, _altLeadingZero);
                ResetAltCode();
                if (c != '\0')
                {
                    if (!_session.IsOpen) _session.Open(ms, _appId, _titleId, e.Device);
                    Emit(c, ms, isolated: false);
                }
            }
            else if (_altAlone) Invalidating(TextAction.OtherKey, k, ShortcutModifiers.None, ms, repeat: false);
            _altAlone = false;
        }
        else if (k is KeyCode.LeftWin or KeyCode.RightWin && _winAlone)
        {
            // Win seul ouvre le menu Démarrer.
            _winAlone = false;
            Invalidating(TextAction.Shortcut, k, ShortcutModifiers.Win, ms, repeat: false);
        }
    }

    private void MouseDown(long ms)
    {
        _stats.AddClick(ms);
        _dead = '\0';
        _corrections.Invalidate();
        if (_session.IsOpen) _session.Invalidate();
        _altAlone = _winAlone = false;
    }

    /// <summary>Alt qui n'est pas AltGr.</summary>
    private bool IsPlainAlt(ushort k) => k == KeyCode.LeftAlt || (k == KeyCode.RightAlt && !_layout.HasAltGr);

    private void Dispatch(TextAction action, ushort k, char c, ShortcutModifiers mods, long ms, ushort device, bool repeat)
    {
        switch (action)
        {
            case TextAction.Neutral:
                return;
            case TextAction.Char or TextAction.DeadKey or TextAction.Backspace or TextAction.WordBackspace or TextAction.AltDigit:
                Text(action, k, c, ms, device, repeat);
                return;
            default:
                Invalidating(action, k, mods, ms, repeat);
                return;
        }
    }

    private void Text(TextAction action, ushort k, char c, long ms, ushort device, bool repeat)
    {
        if (!_session.IsOpen) _session.Open(ms, _appId, _titleId, device);
        bool overlap = false;
        if (!repeat && action is TextAction.Char or TextAction.DeadKey)
        {
            int day = _stats.DayKey(ms);
            _stats.AddMetric(day, DayMetric.CharPresses, 1);
            if (action == TextAction.Char && CapitalShift.Classify(_down, _capsLock, k, c) is { } capital)
                _stats.AddMetric(day, capital, 1);
            if (_textDownCount > 0)
            {
                overlap = true;
                _stats.AddMetric(day, DayMetric.Overlaps, 1);
            }
            else if (_lastTextUpMs != long.MinValue)
            {
                long flight = ms - _lastTextUpMs;
                if (flight >= 0 && flight <= _s.HoldCapMs)
                    _stats.AddHistogram(day, HistogramKind.FlightMs, 0, LogScale.Flight.Bucket(flight));
            }
            if (!_textDown[k])
            {
                _textDown[k] = true;
                _textDownCount++;
            }
        }
        _session.TextKey(ms, repeat, overlap);

        switch (action)
        {
            case TextAction.Char:
                if (_dead != '\0')
                {
                    char dead = _dead;
                    _dead = '\0';
                    char composed = DeadKeys.Compose(dead, c);
                    if (composed != '\0') Emit(composed, ms, repeat);
                    else
                    {
                        // Pas de composition : Windows sort l'accent seul puis la lettre.
                        Emit(dead, ms, repeat);
                        Emit(c, ms, isolated: true);
                    }
                }
                else
                {
                    CountDigit(k, c, ms);
                    Emit(c, ms, repeat);
                }
                break;

            case TextAction.DeadKey:
                if (repeat) break;
                if (_dead != '\0')
                {
                    char dead = _dead;
                    _dead = '\0';
                    Emit(dead, ms, isolated: false);
                    Emit(c, ms, isolated: true);
                }
                else _dead = c;
                break;

            case TextAction.Backspace:
                // Retour arrière après une touche morte : l'accent est annulé, rien n'est effacé.
                if (_dead != '\0')
                {
                    _dead = '\0';
                    break;
                }
                _stats.AddBackspace(ms, word: false, repeat ? 1 : 0);
                _corrections.OnBackspace(ms, repeat);
                _session.Backspace(ms, 1);
                _live?.OnDelete(ms, 1);
                break;

            case TextAction.WordBackspace:
                _dead = '\0';
                int n = Math.Max(1, _corrections.OnWordBackspace(ms));
                _stats.AddBackspace(ms, word: true, n);
                _session.Backspace(ms, n);
                _live?.OnDelete(ms, n);
                break;

            case TextAction.AltDigit:
                if (repeat) break;
                int digit = KeyCode.NumpadDigit(k);
                if (_altDigits == 0 && digit == 0) _altLeadingZero = true;
                if (_altDigits < 6) _altValue = _altValue * 10 + digit;
                _altDigits++;
                break;
        }
    }

    /// <summary>Chiffre tapé directement par une touche : rangée du haut ou pavé numérique.</summary>
    private void CountDigit(ushort k, char c, long ms)
    {
        if (c is < '0' or > '9') return;
        DayMetric first = KeyCode.NumpadDigit(k) >= 0 ? DayMetric.NumpadDigit0 : DayMetric.TopRowDigit0;
        _stats.AddMetric(_stats.DayKey(ms), first + (c - '0'), 1);
    }

    private void Emit(char c, long ms, bool isolated)
    {
        _stats.AddChars(ms, _appId, 1);
        _corrections.OnChar(c, ms);
        _session.OnChar(c, ms, isolated);
        _live?.OnChar(ms, c);
        // Entrée : l'appli peut indenter, valider un formulaire, envoyer un message…
        if (c == '\n') _corrections.Invalidate();
        if (_session.IsFull) EndSession();
    }

    /// <summary>Touche qui peut déplacer le curseur ou modifier le texte à notre insu.</summary>
    private void Invalidating(TextAction action, ushort k, ShortcutModifiers mods, long ms, bool repeat)
    {
        _dead = '\0';
        _corrections.Invalidate();
        if (_session.IsOpen)
        {
            _session.OtherKey();
            _session.Invalidate();
        }
        if (action == TextAction.Shortcut && !repeat) _stats.AddShortcut(_stats.DayKey(ms), mods, k);
    }
}
