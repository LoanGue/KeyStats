namespace KeyStats.Core.Analysis;

/// <summary>Bilan d'un épisode de correction.</summary>
/// <param name="Deleted">Caractères effacés (connus ou non).</param>
/// <param name="Errors">Vraies fautes trouvées (0 sauf pour <see cref="CorrectionOutcome.Typo"/>).</param>
/// <param name="Collateral">Caractères justes effacés en même temps que la faute.</param>
/// <param name="DetectionDelayMs">De la première faute au premier retour arrière, -1 si sans objet.</param>
public readonly record struct CorrectionResult(
    CorrectionOutcome Outcome, int Deleted, int Errors, int Collateral, int DetectionDelayMs, bool Held, bool Word);

/// <summary>Une faute : caractère tapé, caractère voulu, et caractère voulu juste avant (pour les bigrammes).</summary>
public readonly record struct ErrorRecord(ErrorType Type, char Typed, char Wanted, char Prev);

public interface ICorrectionSink
{
    void OnCorrection(in CorrectionResult result, ReadOnlySpan<ErrorRecord> errors);
}

/// <summary>
/// Retrouve les vraies fautes derrière les retours arrière. Garde en mémoire (jamais sur disque) les
/// derniers caractères tapés, en supposant le curseur en fin de texte. Une suite de retours arrière
/// efface la chaîne D ; ce qui est tapé ensuite (R) est aligné avec D (Damerau-Levenshtein restreint)
/// pour séparer la faute des caractères justes effacés au passage.
/// </summary>
public sealed class CorrectionAnalyzer
{
    public const int BufferSize = 256;
    private const int MaxPending = 4;
    private const int Cost = 1000;

    private enum Op : byte { None, Match, Transpose, Substitute, Insert, Omit }

    [Flags]
    private enum EpisodeFlags : byte { None = 0, Unknown = 1, Overflow = 2, Held = 4, Word = 8 }

    private sealed class Episode(int capacity)
    {
        public readonly char[] D = new char[capacity];
        public readonly long[] DMs = new long[capacity];
        public int N, Count, Anchor;
        public long FirstBsMs;
        public EpisodeFlags Flags;
        public char Before;

        public void Reset(long firstBsMs)
        {
            N = Count = Anchor = 0;
            FirstBsMs = firstBsMs;
            Flags = EpisodeFlags.None;
            Before = '\0';
        }
    }

    private readonly ICorrectionSink _sink;
    private readonly int _maxLen, _cols;
    private readonly char[] _buf = new char[BufferSize];
    private readonly long[] _ms = new long[BufferSize];
    private int _len;

    private readonly Stack<Episode> _free = new();
    private readonly Episode[] _pending = new Episode[MaxPending];
    private int _pendingCount;
    private Episode? _current;

    private readonly int[] _score;
    private readonly Op[] _op;
    private readonly Op[] _path;
    private readonly ErrorRecord[] _errors;

    public CorrectionAnalyzer(ICorrectionSink sink, AnalysisSettings? settings = null)
    {
        _sink = sink;
        _maxLen = (settings ?? AnalysisSettings.Default).MaxCorrectionLength;
        _cols = Needed(_maxLen) + 1;
        _score = new int[(_maxLen + 1) * _cols];
        _op = new Op[(_maxLen + 1) * _cols];
        _path = new Op[_maxLen + _cols];
        _errors = new ErrorRecord[_maxLen + _cols];
        for (int i = 0; i < MaxPending + 1; i++) _free.Push(new Episode(_maxLen + 1));
    }

    /// <summary>Caractères en mémoire (pour les tests).</summary>
    internal int BufferedLength => _len;

    /// <summary>Longueur de la suite à attendre avant de conclure : un peu plus que le texte effacé.</summary>
    public static int Needed(int n) => n + Math.Max(2, (n + 1) / 2);

    public void OnChar(char c, long ms)
    {
        if (_current is not null) CloseEpisode();
        if (_len == BufferSize) Shift();
        _buf[_len] = c;
        _ms[_len] = ms;
        _len++;
        ResolveReady();
    }

    public void OnBackspace(long ms, bool repeat)
    {
        var ep = _current ??= StartEpisode(ms);
        if (repeat) ep.Flags |= EpisodeFlags.Held;
        PopOne(ep);
    }

    /// <summary>Ctrl+Retour arrière : efface les espaces de fin puis le mot. Renvoie le nombre de caractères connus effacés.</summary>
    public int OnWordBackspace(long ms)
    {
        var ep = _current ??= StartEpisode(ms);
        ep.Flags |= EpisodeFlags.Word;
        if (_len == 0)
        {
            ep.Flags |= EpisodeFlags.Unknown;
            ep.Count++;
            return 0;
        }
        int popped = 0;
        while (_len > 0 && CharClass.IsWhitespace(_buf[_len - 1])) { PopOne(ep); popped++; }
        if (_len > 0)
        {
            bool word = CharClass.IsWordChar(_buf[_len - 1]);
            while (_len > 0 && !CharClass.IsWhitespace(_buf[_len - 1]) && CharClass.IsWordChar(_buf[_len - 1]) == word)
            {
                PopOne(ep);
                popped++;
            }
        }
        // Tampon vidé : le mot commençait peut-être avant ce qu'on connaît.
        if (_len == 0) ep.Flags |= EpisodeFlags.Unknown;
        return popped;
    }

    /// <summary>
    /// Le curseur a pu bouger (clic, flèches, raccourci, Entrée, changement de fenêtre, fin de session) :
    /// les corrections en attente sont conclues avec ce qui a été tapé, puis le tampon est oublié.
    /// </summary>
    public void Invalidate()
    {
        if (_current is not null) CloseEpisode();
        while (_pendingCount > 0) ResolveAt(0);
        Array.Clear(_buf, 0, _len);
        _len = 0;
    }

    private Episode StartEpisode(long ms)
    {
        var ep = _free.Pop();
        ep.Reset(ms);
        return ep;
    }

    private void PopOne(Episode ep)
    {
        ep.Count++;
        if (_len == 0)
        {
            ep.Flags |= EpisodeFlags.Unknown;
            return;
        }
        _len--;
        // Effacer sous le point de reprise d'une correction en attente : elle se fond dans celle-ci.
        for (int i = _pendingCount - 1; i >= 0; i--)
        {
            if (_len < _pending[i].Anchor) RemovePending(i);
        }
        if (ep.N < _maxLen + 1)
        {
            ep.D[ep.N] = _buf[_len];
            ep.DMs[ep.N] = _ms[_len];
            ep.N++;
        }
        else ep.Flags |= EpisodeFlags.Overflow;
        _buf[_len] = '\0';
    }

    private void CloseEpisode()
    {
        var ep = _current!;
        _current = null;
        Array.Reverse(ep.D, 0, ep.N);
        Array.Reverse(ep.DMs, 0, ep.N);
        ep.Anchor = _len;
        ep.Before = _len > 0 ? _buf[_len - 1] : '\0';
        bool held = (ep.Flags & EpisodeFlags.Held) != 0, word = (ep.Flags & EpisodeFlags.Word) != 0;
        if ((ep.Flags & EpisodeFlags.Unknown) != 0)
            Emit(new CorrectionResult(CorrectionOutcome.Unanalyzed, ep.Count, 0, 0, -1, held, word), 0, ep);
        else if ((ep.Flags & EpisodeFlags.Overflow) != 0 || ep.N > _maxLen)
            Emit(new CorrectionResult(CorrectionOutcome.Reformulation, ep.Count, 0, 0, -1, held, word), 0, ep);
        else if (ep.N == 0)
            _free.Push(ep);
        else
        {
            if (_pendingCount == MaxPending) ResolveAt(0);
            _pending[_pendingCount++] = ep;
        }
    }

    private void ResolveReady()
    {
        for (int i = 0; i < _pendingCount;)
        {
            var p = _pending[i];
            if (_len - p.Anchor >= Needed(p.N)) ResolveAt(i);
            else i++;
        }
    }

    private void Shift()
    {
        const int drop = BufferSize / 2;
        for (int i = 0; i < _pendingCount;)
        {
            if (_pending[i].Anchor < drop) ResolveAt(i);
            else { _pending[i].Anchor -= drop; i++; }
        }
        Array.Copy(_buf, drop, _buf, 0, _len - drop);
        Array.Copy(_ms, drop, _ms, 0, _len - drop);
        _len -= drop;
    }

    private void RemovePending(int index)
    {
        _free.Push(_pending[index]);
        for (int i = index; i < _pendingCount - 1; i++) _pending[i] = _pending[i + 1];
        _pending[--_pendingCount] = null!;
    }

    private void ResolveAt(int index)
    {
        var ep = _pending[index];
        for (int i = index; i < _pendingCount - 1; i++) _pending[i] = _pending[i + 1];
        _pending[--_pendingCount] = null!;
        Resolve(ep);
    }

    private void Resolve(Episode ep)
    {
        int n = ep.N;
        int m = Math.Min(_len - ep.Anchor, Needed(n));
        bool held = (ep.Flags & EpisodeFlags.Held) != 0, word = (ep.Flags & EpisodeFlags.Word) != 0;
        if (m <= 0)
        {
            // Effacé sans rien retaper : du texte retiré, pas une faute qu'on peut caractériser.
            Emit(new CorrectionResult(CorrectionOutcome.Reformulation, ep.Count, 0, 0, -1, held, word), 0, ep);
            return;
        }
        ReadOnlySpan<char> d = ep.D.AsSpan(0, n), r = _buf.AsSpan(ep.Anchor, m);
        Align(d, r);

        // Meilleure longueur de reprise j : score minimal, puis j le plus proche de n.
        int bestJ = 0, best = int.MaxValue;
        for (int j = 0; j <= m; j++)
        {
            int s = _score[n * _cols + j];
            if (s < best || (s == best && Math.Abs(j - n) < Math.Abs(bestJ - n))) { best = s; bestJ = j; }
        }

        int steps = Backtrack(n, bestJ);
        int cost = 0, matches = 0;
        for (int k = 0; k < steps; k++)
        {
            if (_path[k] == Op.Match) matches++;
            else cost++;
        }

        if (cost == 0)
        {
            Emit(new CorrectionResult(CorrectionOutcome.Retyped, ep.Count, 0, 0, -1, held, word), 0, ep);
            return;
        }
        if (cost > Math.Max(1, n / 2))
        {
            Emit(new CorrectionResult(CorrectionOutcome.Reformulation, ep.Count, 0, 0, -1, held, word), 0, ep);
            return;
        }

        int errors = 0, firstError = -1, di = 0, rj = 0;
        for (int k = 0; k < steps; k++)
        {
            Op op = _path[k];
            char prev = rj > 0 ? r[rj - 1] : ep.Before;
            switch (op)
            {
                case Op.Match:
                    di++; rj++;
                    continue;
                case Op.Substitute:
                    _errors[errors++] = new ErrorRecord(ErrorType.Substitution, d[di], r[rj], prev);
                    if (firstError < 0) firstError = di;
                    di++; rj++;
                    break;
                case Op.Insert:
                    _errors[errors++] = new ErrorRecord(ErrorType.Insertion, d[di], '\0', prev);
                    if (firstError < 0) firstError = di;
                    di++;
                    break;
                case Op.Omit:
                    _errors[errors++] = new ErrorRecord(ErrorType.Omission, '\0', r[rj], prev);
                    if (firstError < 0) firstError = Math.Min(di, n - 1);
                    rj++;
                    break;
                case Op.Transpose:
                    _errors[errors++] = new ErrorRecord(ErrorType.Transposition, d[di], r[rj], prev);
                    if (firstError < 0) firstError = di;
                    di += 2; rj += 2;
                    break;
            }
        }
        int delay = firstError >= 0 ? (int)Math.Clamp(ep.FirstBsMs - ep.DMs[firstError], 0, int.MaxValue) : -1;
        Emit(new CorrectionResult(CorrectionOutcome.Typo, ep.Count, errors, matches, delay, held, word), errors, ep);
    }

    /// <summary>
    /// Programmation dynamique D contre R. Score = 1000 × coût − caractères couverts : à coût égal,
    /// l'alignement qui explique le plus de caractères gagne (une inversion en couvre deux).
    /// </summary>
    private void Align(ReadOnlySpan<char> d, ReadOnlySpan<char> r)
    {
        int n = d.Length, m = r.Length, w = _cols;
        for (int j = 0; j <= m; j++) { _score[j] = j * Cost; _op[j] = Op.Omit; }
        for (int i = 1; i <= n; i++)
        {
            _score[i * w] = i * Cost;
            _op[i * w] = Op.Insert;
            for (int j = 1; j <= m; j++)
            {
                int diag = _score[(i - 1) * w + j - 1];
                int best;
                Op op;
                if (d[i - 1] == r[j - 1]) { best = diag - 1; op = Op.Match; }
                else { best = int.MaxValue; op = Op.None; }
                if (i > 1 && j > 1 && d[i - 1] == r[j - 2] && d[i - 2] == r[j - 1] && d[i - 1] != d[i - 2])
                {
                    int t = _score[(i - 2) * w + j - 2] + Cost - 2;
                    if (t < best) { best = t; op = Op.Transpose; }
                }
                if (op != Op.Match)
                {
                    int s = diag + Cost;
                    if (s < best) { best = s; op = Op.Substitute; }
                }
                int ins = _score[(i - 1) * w + j] + Cost;
                if (ins < best) { best = ins; op = Op.Insert; }
                int omit = _score[i * w + j - 1] + Cost;
                if (omit < best) { best = omit; op = Op.Omit; }
                _score[i * w + j] = best;
                _op[i * w + j] = op;
            }
        }
    }

    /// <summary>Remonte le chemin optimal jusqu'à (0, 0) et le range dans l'ordre de frappe.</summary>
    private int Backtrack(int i, int j)
    {
        int steps = 0;
        while (i > 0 || j > 0)
        {
            Op op = i == 0 ? Op.Omit : j == 0 ? Op.Insert : _op[i * _cols + j];
            _path[steps++] = op;
            switch (op)
            {
                case Op.Match or Op.Substitute: i--; j--; break;
                case Op.Transpose: i -= 2; j -= 2; break;
                case Op.Insert: i--; break;
                default: j--; break;
            }
        }
        Array.Reverse(_path, 0, steps);
        return steps;
    }

    private void Emit(in CorrectionResult result, int errors, Episode ep)
    {
        _free.Push(ep);
        _sink.OnCorrection(result, _errors.AsSpan(0, errors));
    }
}
