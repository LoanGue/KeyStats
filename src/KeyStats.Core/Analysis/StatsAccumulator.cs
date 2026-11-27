using System.Runtime.InteropServices;

namespace KeyStats.Core.Analysis;

internal struct KeyCount
{
    public int Presses;
    public int Repeats;
}

internal struct MinuteTotal
{
    public int Keys, Chars, Backspaces, Clicks, WritingMs, ExcludedKeys;
}

internal struct AppHourTotal
{
    public int Keys, Chars, WritingMs, Sessions;
}

internal struct BigramTotal
{
    public int N, Errors;
    public int[]? Latency;
}

/// <summary>
/// Statistiques en attente d'écriture, additionnées en mémoire entre deux enregistrements.
/// Pas d'allocation en régime établi : clés en tuples de valeurs, histogrammes recyclés.
/// </summary>
public sealed class StatsAccumulator
{
    public const int ChordBuckets = 16;

    private readonly LocalDayClock _clock;
    private readonly Stack<int[]> _logPool = new(), _chordPool = new();

    public StatsAccumulator(LocalDayClock clock) => _clock = clock;

    internal Dictionary<(long Minute, ushort Key, ushort Device), KeyCount> KeyMinutes { get; } = new();
    internal Dictionary<long, MinuteTotal> Minutes { get; } = new();
    internal Dictionary<(long Hour, int App), AppHourTotal> AppHours { get; } = new();
    internal List<SessionRecord> Sessions { get; } = new();
    internal Dictionary<(int Day, SessionKind Kind, char Ch), int> Chars { get; } = new();
    internal Dictionary<(int Day, char A, char B), BigramTotal> Bigrams { get; } = new();
    internal Dictionary<(int Day, int Length), int> WordLengths { get; } = new();
    internal Dictionary<(int Day, ShortcutModifiers Mods, ushort Key), int> Shortcuts { get; } = new();
    internal Dictionary<(int Day, char Typed, char Wanted, ErrorType Type), int> ErrorPairs { get; } = new();
    internal Dictionary<(int Day, DayMetric Metric), long> Metrics { get; } = new();
    internal Dictionary<(int Day, HistogramKind Kind, int Sub), int[]> Histograms { get; } = new();

    public bool IsEmpty =>
        KeyMinutes.Count == 0 && Minutes.Count == 0 && AppHours.Count == 0 && Sessions.Count == 0
        && Chars.Count == 0 && Bigrams.Count == 0 && WordLengths.Count == 0 && Shortcuts.Count == 0
        && ErrorPairs.Count == 0 && Metrics.Count == 0 && Histograms.Count == 0;

    public IReadOnlyList<SessionRecord> PendingSessions => Sessions;

    public static int HistogramSize(HistogramKind kind) => kind == HistogramKind.Chord ? ChordBuckets : LogScale.Buckets;

    public int DayKey(long ms) => _clock.DayKey(ms);

    public void AddKeyPress(long ms, ushort key, ushort device, bool repeat, int appId)
    {
        long minute = LocalDayClock.MinuteOf(ms);
        ref KeyCount kc = ref CollectionsMarshal.GetValueRefOrAddDefault(KeyMinutes, (minute, key, device), out _);
        int day = _clock.DayKey(ms);
        if (repeat)
        {
            kc.Repeats++;
            AddMetric(day, DayMetric.Repeats, 1);
            return;
        }
        kc.Presses++;
        Minute(minute).Keys++;
        AppHour(LocalDayClock.HourOf(ms), appId).Keys++;
        AddMetric(day, DayMetric.KeyPresses, 1);
    }

    public void AddChars(long ms, int appId, int n)
    {
        Minute(LocalDayClock.MinuteOf(ms)).Chars += n;
        AppHour(LocalDayClock.HourOf(ms), appId).Chars += n;
        AddMetric(_clock.DayKey(ms), DayMetric.CharsTyped, n);
    }

    /// <summary>
    /// Un retour arrière. <paramref name="extra"/> : 1 pour une répétition de l'appui maintenu, ou les
    /// caractères effacés par Ctrl+Retour arrière.
    /// </summary>
    public void AddBackspace(long ms, bool word, int extra)
    {
        Minute(LocalDayClock.MinuteOf(ms)).Backspaces++;
        int day = _clock.DayKey(ms);
        AddMetric(day, word ? DayMetric.WordBackspaces : DayMetric.Backspaces, 1);
        if (extra > 0) AddMetric(day, word ? DayMetric.WordBackspaceChars : DayMetric.BackspaceRepeats, extra);
    }

    public void AddClick(long ms)
    {
        Minute(LocalDayClock.MinuteOf(ms)).Clicks++;
        AddMetric(_clock.DayKey(ms), DayMetric.MouseClicks, 1);
    }

    public void AddExcludedKey(long ms)
    {
        Minute(LocalDayClock.MinuteOf(ms)).ExcludedKeys++;
        AddMetric(_clock.DayKey(ms), DayMetric.ExcludedKeys, 1);
    }

    public void AddWritingMs(long minute, int appId, int ms)
    {
        Minute(minute).WritingMs += ms;
        AppHour(Math.DivRem(minute, 60, out long r) - (r < 0 ? 1 : 0), appId).WritingMs += ms;
    }

    public void AddSessionToHour(long hour, int appId) => AppHour(hour, appId).Sessions++;

    public void AddMetric(int day, DayMetric metric, long value)
    {
        if (value == 0) return;
        CollectionsMarshal.GetValueRefOrAddDefault(Metrics, (day, metric), out _) += value;
    }

    public void AddChar(int day, SessionKind kind, char c) =>
        CollectionsMarshal.GetValueRefOrAddDefault(Chars, (day, kind, c), out _)++;

    /// <summary>Un bigramme a→b ; <paramref name="latencyMs"/> négatif si la latence n'est pas exploitable.</summary>
    public void AddBigram(int day, char a, char b, int latencyMs)
    {
        ref BigramTotal t = ref CollectionsMarshal.GetValueRefOrAddDefault(Bigrams, (day, a, b), out _);
        t.N++;
        if (latencyMs >= 0)
        {
            t.Latency ??= Rent(LogScale.Buckets);
            t.Latency[LogScale.BigramLatency.Bucket(latencyMs)]++;
        }
    }

    public void AddBigramError(int day, char a, char b) =>
        CollectionsMarshal.GetValueRefOrAddDefault(Bigrams, (day, a, b), out _).Errors++;

    public void AddWordLength(int day, int length, int n = 1) =>
        CollectionsMarshal.GetValueRefOrAddDefault(WordLengths, (day, length), out _) += n;

    public void AddShortcut(int day, ShortcutModifiers mods, ushort key)
    {
        CollectionsMarshal.GetValueRefOrAddDefault(Shortcuts, (day, mods, key), out _)++;
        AddMetric(day, DayMetric.Shortcuts, 1);
    }

    public void AddErrorPair(int day, char typed, char wanted, ErrorType type) =>
        CollectionsMarshal.GetValueRefOrAddDefault(ErrorPairs, (day, typed, wanted, type), out _)++;

    public void AddHistogram(int day, HistogramKind kind, int sub, int bucket, int n = 1)
    {
        ref int[]? h = ref CollectionsMarshal.GetValueRefOrAddDefault(Histograms, (day, kind, sub), out _);
        int size = HistogramSize(kind);
        h ??= Rent(size);
        h[Math.Clamp(bucket, 0, size - 1)] += n;
    }

    public void AddSession(SessionRecord session) => Sessions.Add(session);

    /// <summary>Vide tout après un enregistrement réussi ; les histogrammes retournent au recyclage.</summary>
    public void Clear()
    {
        foreach (var t in Bigrams.Values) if (t.Latency is not null) Return(t.Latency);
        foreach (var h in Histograms.Values) Return(h);
        KeyMinutes.Clear();
        Minutes.Clear();
        AppHours.Clear();
        Sessions.Clear();
        Chars.Clear();
        Bigrams.Clear();
        WordLengths.Clear();
        Shortcuts.Clear();
        ErrorPairs.Clear();
        Metrics.Clear();
        Histograms.Clear();
    }

    private ref MinuteTotal Minute(long minute) =>
        ref CollectionsMarshal.GetValueRefOrAddDefault(Minutes, minute, out _);

    private ref AppHourTotal AppHour(long hour, int appId) =>
        ref CollectionsMarshal.GetValueRefOrAddDefault(AppHours, (hour, appId), out _);

    private int[] Rent(int size)
    {
        var pool = size == ChordBuckets ? _chordPool : _logPool;
        return pool.Count > 0 ? pool.Pop() : new int[size];
    }

    private void Return(int[] a)
    {
        Array.Clear(a);
        (a.Length == ChordBuckets ? _chordPool : _logPool).Push(a);
    }
}
