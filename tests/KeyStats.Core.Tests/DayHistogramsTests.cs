using KeyStats.Core.Analysis;
using KeyStats.Core.Stats;
using KeyStats.Core.Storage;

namespace KeyStats.Core.Tests;

public sealed class DayHistogramsTests : IDisposable
{
    private static readonly DayRange s_range = new(20261121, 20261127);
    private readonly TempDb _temp = new();

    public DayHistogramsTests()
    {
        Add(20261126, HistogramKind.HoldMs, 30, 3, 5);
        Add(20261125, HistogramKind.HoldMs, 30, 3, 2);
        Add(20261126, HistogramKind.HoldMs, 31, 7, 4);
        Add(20261118, HistogramKind.HoldMs, 30, 3, 100); // hors période
        Add(20261126, HistogramKind.DetectionDelayMs, 0, 3, 50); // autre histogramme
    }

    private void Add(int day, HistogramKind kind, int sub, int bucket, int n)
    {
        var counts = new int[StatsAccumulator.HistogramSize(kind)];
        counts[bucket] = n;
        string blob = Convert.ToHexString(HistogramCodec.Encode(counts));
        _temp.Db.Exec($"INSERT INTO day_hist VALUES ({day}, {(int)kind}, {sub}, X'{blob}')");
    }

    [Fact]
    public void Somme_par_sous_cle_sur_la_periode_et_le_seul_histogramme_demande()
    {
        var map = DayHistograms.BySub(_temp.Db, s_range, HistogramKind.HoldMs);

        Assert.Equal([30, 31], map.Keys.Order());
        Assert.Equal(7, map[30][3]);
        Assert.Equal(7, map[30].Sum());
        Assert.Equal(4, map[31][7]);
        Assert.Equal(4, map[31].Sum());
    }

    [Fact]
    public void Somme_toutes_sous_cles_confondues()
    {
        int[] counts = DayHistograms.Sum(_temp.Db, s_range, HistogramKind.HoldMs);

        Assert.Equal(7, counts[3]);
        Assert.Equal(4, counts[7]);
        Assert.Equal(11, counts.Sum());
    }

    public void Dispose() => _temp.Dispose();
}
