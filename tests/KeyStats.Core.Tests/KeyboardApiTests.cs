using KeyStats.Core.Analysis;
using KeyStats.Core.Input;
using KeyStats.Core.Stats;
using KeyStats.Core.Web;

namespace KeyStats.Core.Tests;

public sealed class KeyboardApiTests : IDisposable
{
    private const int Today = 20261127; // un vendredi
    private const ushort KeyE = 0x12, KeyA = 0x10;
    private static readonly LocalDayClock Clock = new(TimeZoneInfo.Utc);
    private readonly TempDb _temp = new();
    private readonly int _laptop, _external;

    public KeyboardApiTests()
    {
        var db = _temp.Db;
        _laptop = db.GetOrAddDevice([1], "Portable", 1);
        _external = db.GetOrAddDevice([2], "Externe", 1);
        // Jour, minute depuis minuit, touche, clavier, appuis.
        AddPresses(20261126, 600, KeyE, _laptop, 30);
        AddPresses(20261126, 601, KeyE, _external, 10);
        AddPresses(20261125, 900, KeyE, _laptop, 20);
        AddPresses(20261126, 600, KeyA, _laptop, 40);
        AddPresses(20261118, 600, KeyA, _laptop, 500);

        AddChar(20261126, SessionKind.Prose, 'e', 90);
        AddChar(20261126, SessionKind.Prose, 'E', 10);
        AddChar(20261126, SessionKind.Code, 'e', 400);
        AddPair(20261126, 'e', 'r', ErrorType.Substitution, 4);
        AddPair(20261126, 'e', '\0', ErrorType.Insertion, 1);
        AddPair(20261126, '\0', 'e', ErrorType.Omission, 9);

        AddHistogram(20261126, HistogramKind.HoldMs, KeyE, LogScale.Hold.Bucket(100), 3);
        AddBigram(20261126, 'a', 'e', LogScale.BigramLatency.Bucket(200), 5);

        AddMetric(20261126, DayMetric.TopRowDigit0 + 1, 6);
        AddMetric(20261126, DayMetric.NumpadDigit0 + 1, 2);
        AddMetric(20261126, DayMetric.NumpadDigit0, 3);
    }

    public void Dispose() => _temp.Dispose();

    private static long MinuteOf(int day, int minute) =>
        LocalDayClock.MinuteOf(Clock.MidnightUtcMs(LocalDayClock.DateOf(day))) + minute;

    private void AddPresses(int day, int minute, ushort key, int device, int presses) =>
        _temp.Db.Exec($"INSERT INTO key_minute VALUES ({MinuteOf(day, minute)}, {key}, {device}, {presses}, 0)");

    private void AddChar(int day, SessionKind kind, char c, int n) =>
        _temp.Db.Exec($"INSERT INTO day_char VALUES ({day}, {(int)kind}, {(int)c}, {n})");

    private void AddPair(int day, char typed, char wanted, ErrorType type, int n) =>
        _temp.Db.Exec($"INSERT INTO day_error_pair VALUES ({day}, {(int)typed}, {(int)wanted}, {(int)type}, {n})");

    private void AddMetric(int day, DayMetric metric, long value) =>
        _temp.Db.Exec($"INSERT INTO day_metric VALUES ({day}, {(int)metric}, {value})");

    private static byte[] Histogram(int bucket, int n)
    {
        var counts = new int[LogScale.Buckets];
        counts[bucket] = n;
        return HistogramCodec.Encode(counts);
    }

    private void AddHistogram(int day, HistogramKind kind, int sub, int bucket, int n)
    {
        using var cmd = _temp.Db.Command("INSERT INTO day_hist VALUES ($d, $h, $s, $data)", day, (int)kind, sub, Histogram(bucket, n));
        cmd.ExecuteNonQuery();
    }

    private void AddBigram(int day, char a, char b, int bucket, int n)
    {
        using var cmd = _temp.Db.Command("INSERT INTO day_bigram VALUES ($d, $a, $b, $n, 0, $lat)", day, (int)a, (int)b, n, Histogram(bucket, n));
        cmd.ExecuteNonQuery();
    }

    private static StatsFilter Filter(string query) => StatsFilter.Parse(WebQuery.Parse(query), Today, 20261101)!;

    private Keyboard Read(string query) => KeyboardApi.Read(_temp.Db, Clock, Filter(query));

    private static KeyboardKey Key(Keyboard k, ushort code) => k.Keys.Single(x => x.Code == code);

    [Fact]
    public void Appuis_par_touche_sur_la_periode()
    {
        var k = Read("period=week");
        Assert.Equal(("2026-11-21", "2026-11-27", false), (k.From, k.To, k.DayTablesUnfiltered));
        Assert.Equal(100, k.Presses);
        Assert.Equal(60, Key(k, KeyE).Presses);
        Assert.Equal(40, Key(k, KeyA).Presses);
    }

    [Fact]
    public void Filtre_de_clavier_sur_les_appuis_seulement()
    {
        var k = Read($"period=week&device={_external}");
        Assert.True(k.DayTablesUnfiltered);
        Assert.Equal(10, k.Presses);
        var e = Key(k, KeyE);
        Assert.Equal(10, e.Presses);
        // Les tables par jour ne connaissent pas le clavier.
        Assert.Equal(0.05, e.ErrorRate!.Value, 6);
        Assert.DoesNotContain(k.Keys, x => x.Code == KeyA);
    }

    [Fact]
    public void Taux_de_faute_sur_la_prose_tapee_par_la_touche()
    {
        var k = Read("period=week");
        // Substitution et lettre en trop sur e, pour 100 caractères de prose (e et E) ; l'oubli ne compte pas.
        Assert.Equal(0.05, Key(k, KeyE).ErrorRate!.Value, 6);
        Assert.Null(Key(k, KeyA).ErrorRate);
    }

    [Fact]
    public void Duree_d_appui_et_delai_medians_par_touche()
    {
        var e = Key(Read("period=week"), KeyE);
        int hold = LogScale.Hold.Bucket(100), delay = LogScale.BigramLatency.Bucket(200);
        Assert.InRange(e.HoldMs!.Value, LogScale.Hold.LowerBound(hold), LogScale.Hold.UpperBound(hold));
        Assert.InRange(e.DelayMs!.Value, LogScale.BigramLatency.LowerBound(delay), LogScale.BigramLatency.UpperBound(delay));
        Assert.Null(Key(Read("period=week"), KeyA).HoldMs);
    }

    [Fact]
    public void Chiffres_de_la_rangee_du_haut_et_du_pave()
    {
        var digits = Read("period=week").Digits;
        Assert.Equal(10, digits.Count);
        Assert.Equal(new KeyboardDigit(0, 0, 3), digits[0]);
        Assert.Equal(new KeyboardDigit(1, 6, 2), digits[1]);
    }
}
