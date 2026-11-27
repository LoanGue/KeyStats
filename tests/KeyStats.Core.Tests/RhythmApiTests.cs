using KeyStats.Core.Analysis;
using KeyStats.Core.Input;
using KeyStats.Core.Stats;
using KeyStats.Core.Text;
using KeyStats.Core.Web;

namespace KeyStats.Core.Tests;

public sealed class RhythmApiTests : IDisposable
{
    private const int Today = 20261127;
    private static readonly LogScale s_lat = LogScale.BigramLatency;
    private readonly TempDb _temp = new();

    public RhythmApiTests()
    {
        // Toutes les occurrences ont un délai mesuré, dans une seule case.
        AddBigram(20261126, 'q', 'u', 100, 0, 16);
        AddBigram(20261126, 'e', 's', 30, 3, 20);
        AddBigram(20261125, 'e', 's', 20, 2, 20);
        AddBigram(20261126, 'c', 'e', 20, 4, 24);
        AddBigram(20261126, 'a', 'b', 5, 5, 28);
        AddBigram(20261126, ' ', 'l', 10, 0, 16);
        AddBigram(20261126, 'x', '\n', 50, 0, 30);
        AddBigram(20261118, 'z', 'z', 900, 0, 31);

        AddHold(20261126, KeyOf('e'), 5, 30);
        AddHold(20261126, KeyCode.Space, 6, 20);
        AddHold(20261126, KeyCode.LeftShift, 15, 10);
        AddHold(20261118, KeyOf('p'), 25, 999);

        AddMetric(20261126, DayMetric.CharPresses, 400);
        AddMetric(20261126, DayMetric.Overlaps, 60);
        AddMetric(20261126, DayMetric.ShiftOppositeCapitals, 7);
        AddMetric(20261126, DayMetric.ShiftSameSideCapitals, 2);
        AddMetric(20261125, DayMetric.CapsLockCapitals, 1);
    }

    private static ushort KeyOf(char c) =>
        LayoutTable.FrenchAzerty.TryFindKey(c, out ushort key, out _) ? key : throw new ArgumentException(c.ToString());

    /// <summary>Histogramme d'une seule case, en littéral SQLite.</summary>
    private static string OneBucket(int bucket, int n)
    {
        var counts = new int[LogScale.Buckets];
        counts[bucket] = n;
        return $"X'{Convert.ToHexString(HistogramCodec.Encode(counts))}'";
    }

    private void AddBigram(int day, char a, char b, int n, int errors, int bucket) =>
        _temp.Db.Exec($"INSERT INTO day_bigram VALUES ({day}, {(int)a}, {(int)b}, {n}, {errors}, {OneBucket(bucket, n)})");

    private void AddHold(int day, ushort key, int bucket, int n) =>
        _temp.Db.Exec($"INSERT INTO day_hist VALUES ({day}, {(int)HistogramKind.HoldMs}, {key}, {OneBucket(bucket, n)})");

    private void AddMetric(int day, DayMetric metric, long value) =>
        _temp.Db.Exec($"INSERT INTO day_metric VALUES ({day}, {(int)metric}, {value})");

    private Rhythm Read(string query) => RhythmApi.Read(_temp.Db, StatsFilter.Parse(WebQuery.Parse(query), Today, 20261101)!);

    [Fact]
    public void Bigrammes_classes_par_cout_au_dessus_du_delai_median()
    {
        var r = Read("period=week");
        Assert.Equal(("2026-11-21", "2026-11-27", false), (r.From, r.To, r.DayTablesUnfiltered));
        // 110 des 185 délais mesurés tombent dans la case 16 et aucun plus bas : la médiane est le milieu de cette case.
        Assert.Equal(s_lat.Middle(16), r.MedianDelayMs!.Value, 6);
        // « ab » (5 fois) et « x↵ » (caractère de contrôle) sont écartés, « qu » et « ␣l », à la médiane et sans faute, aussi ;
        // la semaine précédente ne compte pas.
        Assert.Equal(["ce", "es"], r.Bigrams.Select(b => b.A + b.B));
        var ce = r.Bigrams[0];
        Assert.Equal(20, ce.Count);
        Assert.Equal(s_lat.Middle(24) - s_lat.Middle(16), ce.DelayMs, 6);
        Assert.Equal(20 * ce.DelayMs, ce.CostMs, 6);
        Assert.Equal(4.0 / 24, ce.ErrorRate, 6);
        Assert.Equal((50, 5.0 / 55), (r.Bigrams[1].Count, r.Bigrams[1].ErrorRate));
    }

    [Fact]
    public void Meme_doigt_et_alternance_hors_espace()
    {
        var flow = Read("period=week").Flow;
        // qu 100 (alternance), es 50 (même main), ce 20 (même doigt), ab 5 (même main) ; « ␣l » n'a pas de main.
        Assert.Equal(new RhythmFlow(175, 20, 100), flow);
    }

    [Fact]
    public void Durees_d_appui_des_touches_qui_ecrivent_et_charge_par_doigt()
    {
        var r = Read("period=week");
        // Maj est exclue de la durée d'appui : 30 appuis de « e » et 20 de l'espace.
        Assert.Equal(50, r.Hold.Bins.Sum(b => b.Count));
        Assert.Equal(LogScale.Hold.Middle(5), r.Hold.MedianMs!.Value, 6);
        var fingers = r.Fingers.ToDictionary(f => f.Finger, f => f.Presses);
        Assert.Equal(10, fingers.Count);
        Assert.Equal((30, 10, 20, 0), (fingers["leftMiddle"], fingers["leftPinky"], fingers["rightThumb"], fingers["rightPinky"]));
        Assert.Equal(("leftPinky", "rightPinky"), (r.Fingers[0].Finger, r.Fingers[9].Finger));
    }

    [Fact]
    public void Chevauchement_et_cote_de_la_touche_Maj()
    {
        var r = Read("period=week");
        Assert.Equal((400, 60), (r.CharPresses, r.Overlaps));
        Assert.Equal(new RhythmCapitals(7, 2, 1), r.Capitals);
    }

    [Fact]
    public void Filtre_de_session_signale_les_tables_par_jour_non_filtrees()
    {
        var r = Read("period=week&app=1");
        Assert.True(r.DayTablesUnfiltered);
        Assert.Equal(2, r.Bigrams.Count);
    }

    [Fact]
    public void Periode_vide()
    {
        var r = Read("period=day");
        Assert.Null(r.MedianDelayMs);
        Assert.Empty(r.Bigrams);
        Assert.Equal((null, 0), (r.Hold.MedianMs, r.Hold.Bins.Count));
        Assert.Equal(new RhythmFlow(0, 0, 0), r.Flow);
        Assert.All(r.Fingers, f => Assert.Equal(0, f.Presses));
    }

    [Fact]
    public void Route_repond_en_json()
    {
        var router = new WebRouter(Path.GetTempPath());
        RhythmApi.Register(router, new StatsContext(_temp.Db, () => 1_795_000_000_000, TimeZoneInfo.Utc));
        var response = router.Handle("GET", WebRouter.Origin + RhythmApi.Path + "?period=week");
        Assert.Equal(200, response.Status);
        Assert.Contains("\"medianDelayMs\":", System.Text.Encoding.UTF8.GetString(response.Body), StringComparison.Ordinal);
    }

    public void Dispose() => _temp.Dispose();
}
