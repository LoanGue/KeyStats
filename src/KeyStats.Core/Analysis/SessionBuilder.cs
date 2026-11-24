namespace KeyStats.Core.Analysis;

/// <summary>Une session d'écriture terminée (une ligne de la table <c>session</c>).</summary>
public sealed class SessionRecord
{
    /// <summary>Jour local du début (aaaammjj).</summary>
    public int Day { get; init; }
    public long StartMs { get; init; }
    /// <summary>Dernière touche de texte.</summary>
    public long EndMs { get; init; }
    public int AppId { get; init; }
    /// <summary>Dernier titre de fenêtre vu pendant la session, 0 si inconnu.</summary>
    public long TitleId { get; init; }
    public int DeviceId { get; init; }
    public SessionKind Kind { get; init; }
    /// <summary>Caractères nets (tapés moins effacés).</summary>
    public int Chars { get; init; }
    public int Typed { get; init; }
    public int Deleted { get; init; }
    /// <summary>Toutes les touches de la session, texte ou non (hors modificateurs).</summary>
    public int Keys { get; init; }
    public int TextKeys { get; init; }
    public int Repeats { get; init; }
    /// <summary>Temps « mains sur le clavier » : écarts de plus de 2 s exclus.</summary>
    public long ActiveMs { get; init; }
    /// <summary>Vitesse de frappe brute, en caractères par minute de temps actif.</summary>
    public double GrossCpm { get; init; }
    public double NetCpm { get; init; }
    /// <summary>Débit réel sur toute la durée, pauses de réflexion comprises.</summary>
    public double RealCpm { get; init; }
    /// <summary>Meilleure fenêtre de 30 s, nulle si la session est plus courte.</summary>
    public double? PeakCpm { get; init; }
    public int Typos { get; init; }
    public int Collateral { get; init; }
    public int Corrections { get; init; }
    public int Unanalyzed { get; init; }
    public int Retyped { get; init; }
    public int Reformulations { get; init; }
    public int Overlaps { get; init; }
    /// <summary>Touches de texte par caractère final.</summary>
    public double Kspc { get; init; }

    public long DurationMs => EndMs - StartMs;
}

/// <summary>Un caractère tapé pendant la session, avec le précédent pour les bigrammes ('\0' : pas de bigramme).</summary>
internal readonly record struct CharObservation(char C, char Prev, short LatencyMs);

/// <summary>
/// Accumule une session de frappe, décide à la fin si c'est de l'écriture et verse alors ses
/// statistiques. Les détails (caractères, mots, fautes) restent en mémoire jusque-là : une suite
/// de frappes courtes ne laisse que des comptes de caractères.
/// </summary>
public sealed class SessionBuilder : ICorrectionSink
{
    public const int MaxWordLength = 30;
    private const int PeakRing = 4096;

    private readonly AnalysisSettings _s;
    private readonly StatsAccumulator _stats;
    private readonly LiveMeter? _live;

    private long _startMs;
    private int _appId;
    private long _titleId;
    private ushort _device;

    private int _produced, _countedChars, _deleted, _textKeys, _otherKeys, _pendingOther, _repeats;
    private int _spaces, _symbols, _overlaps;
    private long _activeMs;
    private bool _counted;
    private readonly HashSet<char> _distinct = [];
    private readonly List<CharObservation> _chars = [];
    private readonly List<(long Minute, int Ms)> _writing = [];
    private readonly int[] _wordLengths = new int[MaxWordLength + 1];
    private int _wordLen;
    private bool _wordDirty;
    private char _prev;
    private long _prevMs;

    private readonly long[] _peakMs = new long[PeakRing];
    private readonly int[] _peakDelta = new int[PeakRing];
    private int _peakTail, _peakCount, _peakSum, _peakMax;

    private int _corrections, _typos, _collateral, _unanalyzed, _retyped, _reformulations, _held, _wordDeletes;
    private int _substitutions, _insertions, _omissions, _transpositions;
    private long _delaySum;
    private int _delayCount;
    private readonly int[] _delayHist = new int[LogScale.Buckets];
    private readonly List<ErrorRecord> _errors = [];

    public SessionBuilder(StatsAccumulator stats, AnalysisSettings? settings = null, LiveMeter? live = null)
    {
        _stats = stats;
        _s = settings ?? AnalysisSettings.Default;
        _live = live;
    }

    public bool IsOpen { get; private set; }

    /// <summary>Dernière touche de texte (sert à couper la session après 5 s sans écrire).</summary>
    public long LastTextMs { get; private set; }

    /// <summary>Taille limite atteinte : la session doit être close et une autre ouverte.</summary>
    public bool IsFull => _produced >= _s.MaxSessionChars;

    public void Open(long ms, int appId, long titleId, ushort device)
    {
        Clear();
        IsOpen = true;
        _startMs = LastTextMs = ms;
        _appId = appId;
        _titleId = titleId;
        _device = device;
    }

    /// <summary>Le titre de la fenêtre a changé : la session garde le dernier.</summary>
    public void SetTitle(long titleId) => _titleId = titleId;

    /// <summary>Appui sur une touche de texte (caractère, touche morte, retour arrière, chiffre d'un code Alt).</summary>
    public void TextKey(long ms, bool repeat, bool overlap)
    {
        if (_textKeys > 0)
        {
            long gap = ms - LastTextMs;
            _counted = gap >= 0 && gap <= _s.ActiveGapMs;
            if (_counted)
            {
                _activeMs += gap;
                AddWriting(LocalDayClock.MinuteOf(ms), (int)gap);
            }
        }
        else _counted = false;
        _textKeys++;
        if (repeat) _repeats++;
        if (overlap) _overlaps++;
        _otherKeys += _pendingOther;
        _pendingOther = 0;
        LastTextMs = ms;
    }

    /// <summary>Touche hors texte (flèche, raccourci, Tab…) : ne compte que si l'écriture reprend après.</summary>
    public void OtherKey() => _pendingOther++;

    /// <summary>
    /// Un caractère produit. <paramref name="isolated"/> : pas de bigramme avec le précédent ni avec
    /// le suivant (répétition automatique, caractère sorti d'une touche morte sans composition).
    /// </summary>
    public void OnChar(char c, long ms, bool isolated)
    {
        _produced++;
        if (_counted) _countedChars++;
        if (CharClass.IsWhitespace(c)) _spaces++;
        else if (CharClass.IsCodeSymbol(c)) _symbols++;
        _distinct.Add(c);
        PeakAdd(ms, 1);

        char prev = isolated ? '\0' : _prev;
        long latency = ms - _prevMs;
        short lat = prev != '\0' && latency >= 0 && latency <= _s.BigramMaxLatencyMs ? (short)latency : (short)-1;
        _chars.Add(new CharObservation(c, prev, lat));
        _prev = isolated ? '\0' : c;
        _prevMs = ms;

        if (CharClass.IsWordLetter(c)) _wordLen++;
        else FinishWord();
    }

    /// <summary><paramref name="deleted"/> caractères effacés (1 pour un retour arrière).</summary>
    public void Backspace(long ms, int deleted)
    {
        _deleted += deleted;
        PeakAdd(ms, -deleted);
        _prev = '\0';
        if (!_wordDirty && _wordLen >= deleted) _wordLen -= deleted;
        else
        {
            _wordDirty = true;
            _wordLen = 0;
        }
    }

    /// <summary>Le curseur a pu bouger : le mot en cours et le bigramme suivant ne sont plus fiables.</summary>
    public void Invalidate()
    {
        _wordDirty = true;
        _prev = '\0';
    }

    public void OnCorrection(in CorrectionResult result, ReadOnlySpan<ErrorRecord> errors)
    {
        if (result.Outcome == CorrectionOutcome.Typo) _live?.OnTypos(result.Errors);
        if (!IsOpen) return;
        _corrections++;
        switch (result.Outcome)
        {
            case CorrectionOutcome.Typo:
                _typos += result.Errors;
                _collateral += result.Collateral;
                if (result.DetectionDelayMs >= 0)
                {
                    _delaySum += result.DetectionDelayMs;
                    _delayCount++;
                    _delayHist[LogScale.DetectionDelay.Bucket(result.DetectionDelayMs)]++;
                }
                foreach (var e in errors)
                {
                    switch (e.Type)
                    {
                        case ErrorType.Substitution: _substitutions++; break;
                        case ErrorType.Insertion: _insertions++; break;
                        case ErrorType.Omission: _omissions++; break;
                        case ErrorType.Transposition: _transpositions++; break;
                    }
                    _errors.Add(e);
                }
                break;
            case CorrectionOutcome.Unanalyzed: _unanalyzed++; break;
            case CorrectionOutcome.Retyped: _retyped++; break;
            case CorrectionOutcome.Reformulation: _reformulations++; break;
        }
        if (result.Held) _held++;
        if (result.Word) _wordDeletes++;
    }

    /// <summary>
    /// Clôt la session. Les corrections en attente doivent avoir été conclues avant
    /// (<see cref="CorrectionAnalyzer.Invalidate"/>). Renvoie la session si c'était de l'écriture.
    /// </summary>
    public SessionRecord? End()
    {
        if (!IsOpen) return null;
        IsOpen = false;
        FinishWord();
        int day = _stats.DayKey(_startMs);
        int net = Math.Max(0, _produced - _deleted);
        long duration = LastTextMs - _startMs;

        if (!Qualifies(net, duration))
        {
            foreach (var o in _chars) _stats.AddChar(day, SessionKind.None, o.C);
            Clear();
            return null;
        }

        double symbolRatio = _produced > 0 ? (double)_symbols / _produced : 0;
        SessionKind kind = symbolRatio < _s.ProseMaxSymbolRatio ? SessionKind.Prose
            : symbolRatio < _s.CodeMinSymbolRatio ? SessionKind.Mixed : SessionKind.Code;

        double gross = _activeMs > 0 ? _countedChars * 60000.0 / _activeMs : 0;
        var record = new SessionRecord
        {
            Day = day,
            StartMs = _startMs,
            EndMs = LastTextMs,
            AppId = _appId,
            TitleId = _titleId,
            DeviceId = _device,
            Kind = kind,
            Chars = net,
            Typed = _produced,
            Deleted = _deleted,
            Keys = _textKeys + _otherKeys,
            TextKeys = _textKeys,
            Repeats = _repeats,
            ActiveMs = _activeMs,
            GrossCpm = gross,
            NetCpm = _produced > 0 ? gross * net / _produced : 0,
            RealCpm = duration > 0 ? Math.Max(0, net - 1) * 60000.0 / duration : 0,
            PeakCpm = duration >= _s.PeakWindowMs ? Math.Max(0, _peakMax) * 60000.0 / _s.PeakWindowMs : null,
            Typos = _typos,
            Collateral = _collateral,
            Corrections = _corrections,
            Unanalyzed = _unanalyzed,
            Retyped = _retyped,
            Reformulations = _reformulations,
            Overlaps = _overlaps,
            Kspc = net > 0 ? (double)_textKeys / net : 0,
        };
        Commit(record);
        Clear();
        return record;
    }

    private bool Qualifies(int net, long duration) =>
        net >= _s.MinNetChars
        && _spaces >= _s.MinSpaces
        && duration >= _s.MinDurationMs
        && _textKeys >= (_textKeys + _otherKeys) * _s.MinTextRatio
        && _distinct.Count >= _s.MinDistinctChars
        && _repeats <= _textKeys * _s.MaxRepeatRatio;

    private void Commit(SessionRecord r)
    {
        int day = r.Day;
        _stats.AddSession(r);
        _stats.AddMetric(day, DayMetric.Sessions, 1);
        _stats.AddMetric(day, DayMetric.SessionChars, r.Chars);
        _stats.AddMetric(day, DayMetric.SessionActiveMs, r.ActiveMs);
        _stats.AddMetric(day, DayMetric.SessionDurationMs, r.DurationMs);
        _stats.AddSessionToHour(LocalDayClock.HourOf(r.StartMs), r.AppId);
        foreach (var (minute, ms) in _writing) _stats.AddWritingMs(minute, r.AppId, ms);

        foreach (var o in _chars)
        {
            _stats.AddChar(day, r.Kind, o.C);
            if (o.Prev != '\0') _stats.AddBigram(day, o.Prev, o.C, o.LatencyMs);
        }
        for (int len = 1; len <= MaxWordLength; len++)
        {
            if (_wordLengths[len] > 0) _stats.AddWordLength(day, len, _wordLengths[len]);
        }

        // Les fautes ne sont fiables qu'en prose : dans un éditeur de code, l'auto-complétion
        // et l'indentation automatique modifient le texte à notre insu.
        if (r.Kind != SessionKind.Prose) return;
        _stats.AddMetric(day, DayMetric.ProseSessions, 1);
        _stats.AddMetric(day, DayMetric.ProseChars, r.Chars);
        _stats.AddMetric(day, DayMetric.Corrections, _corrections);
        _stats.AddMetric(day, DayMetric.Typos, _typos);
        _stats.AddMetric(day, DayMetric.Substitutions, _substitutions);
        _stats.AddMetric(day, DayMetric.Insertions, _insertions);
        _stats.AddMetric(day, DayMetric.Omissions, _omissions);
        _stats.AddMetric(day, DayMetric.Transpositions, _transpositions);
        _stats.AddMetric(day, DayMetric.Collateral, _collateral);
        _stats.AddMetric(day, DayMetric.Unanalyzed, _unanalyzed);
        _stats.AddMetric(day, DayMetric.Retyped, _retyped);
        _stats.AddMetric(day, DayMetric.Reformulations, _reformulations);
        _stats.AddMetric(day, DayMetric.HeldBackspace, _held);
        _stats.AddMetric(day, DayMetric.WordDeletes, _wordDeletes);
        _stats.AddMetric(day, DayMetric.DetectionDelaySumMs, _delaySum);
        _stats.AddMetric(day, DayMetric.DetectionDelayCount, _delayCount);
        for (int b = 0; b < _delayHist.Length; b++)
        {
            if (_delayHist[b] > 0) _stats.AddHistogram(day, HistogramKind.DetectionDelayMs, 0, b, _delayHist[b]);
        }
        foreach (var e in _errors)
        {
            _stats.AddErrorPair(day, e.Typed, e.Wanted, e.Type);
            if (e.Prev != '\0' && e.Wanted != '\0') _stats.AddBigramError(day, e.Prev, e.Wanted);
        }
    }

    private void FinishWord()
    {
        if (_wordLen > 0 && !_wordDirty) _wordLengths[Math.Min(_wordLen, MaxWordLength)]++;
        _wordLen = 0;
        _wordDirty = false;
    }

    private void AddWriting(long minute, int ms)
    {
        int last = _writing.Count - 1;
        if (last >= 0 && _writing[last].Minute == minute) _writing[last] = (minute, _writing[last].Ms + ms);
        else _writing.Add((minute, ms));
    }

    /// <summary>Somme glissante des caractères nets sur la fenêtre de pointe, et son maximum.</summary>
    private void PeakAdd(long ms, int delta)
    {
        long from = ms - _s.PeakWindowMs;
        while (_peakCount > 0 && (_peakMs[_peakTail] <= from || _peakCount == PeakRing))
        {
            _peakSum -= _peakDelta[_peakTail];
            _peakTail = (_peakTail + 1) % PeakRing;
            _peakCount--;
        }
        int head = (_peakTail + _peakCount) % PeakRing;
        _peakMs[head] = ms;
        _peakDelta[head] = delta;
        _peakCount++;
        _peakSum += delta;
        if (_peakSum > _peakMax) _peakMax = _peakSum;
    }

    private void Clear()
    {
        _produced = _countedChars = _deleted = _textKeys = _otherKeys = _pendingOther = _repeats = 0;
        _spaces = _symbols = _overlaps = 0;
        _activeMs = 0;
        _counted = false;
        _distinct.Clear();
        _chars.Clear();
        _writing.Clear();
        Array.Clear(_wordLengths);
        _wordLen = 0;
        _wordDirty = false;
        _prev = '\0';
        _prevMs = 0;
        _peakTail = _peakCount = _peakSum = _peakMax = 0;
        _corrections = _typos = _collateral = _unanalyzed = _retyped = _reformulations = _held = _wordDeletes = 0;
        _substitutions = _insertions = _omissions = _transpositions = 0;
        _delaySum = 0;
        _delayCount = 0;
        Array.Clear(_delayHist);
        _errors.Clear();
        _titleId = 0;
    }
}
