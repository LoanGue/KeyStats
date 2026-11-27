using KeyStats.Core.Analysis;
using KeyStats.Core.Storage;

namespace KeyStats.Core.Stats;

/// <param name="ToMs">Borne haute exclue ; null pour la dernière case, sans borne.</param>
public sealed record HistogramBin(double FromMs, double? ToMs, long Count);

/// <param name="MedianMs">Médiane approchée de l'histogramme ; null s'il est vide.</param>
/// <param name="Bins">Cases logarithmiques, de la plus courte à la dernière non vide.</param>
public sealed record TimeDistribution(double? MedianMs, IReadOnlyList<HistogramBin> Bins)
{
    public static TimeDistribution Of(LogScale scale, ReadOnlySpan<int> counts)
    {
        ArgumentNullException.ThrowIfNull(scale);
        int last = counts.LastIndexOfAnyExcept(0);
        var bins = new List<HistogramBin>(last + 1);
        for (int i = 0; i <= last; i++)
        {
            double upper = scale.UpperBound(i);
            bins.Add(new HistogramBin(scale.LowerBound(i), double.IsPositiveInfinity(upper) ? null : upper, counts[i]));
        }
        double median = scale.Median(counts);
        return new TimeDistribution(double.IsNaN(median) ? null : median, bins);
    }
}

/// <summary>Lecture de la table <c>day_hist</c> sur une période.</summary>
internal static class DayHistograms
{
    /// <summary>Somme des histogrammes <paramref name="kind"/> de la période, toutes sous-clés confondues.</summary>
    public static int[] Sum(KeyStatsDb db, DayRange range, HistogramKind kind)
    {
        var counts = new int[StatsAccumulator.HistogramSize(kind)];
        foreach (var (_, data) in Rows(db, range, kind)) HistogramCodec.AddTo(data, counts);
        return counts;
    }

    /// <summary>Histogrammes <paramref name="kind"/> de la période, sommés par sous-clé.</summary>
    public static Dictionary<int, int[]> BySub(KeyStatsDb db, DayRange range, HistogramKind kind)
    {
        var map = new Dictionary<int, int[]>();
        int size = StatsAccumulator.HistogramSize(kind);
        foreach (var (sub, data) in Rows(db, range, kind))
        {
            if (!map.TryGetValue(sub, out var counts)) map[sub] = counts = new int[size];
            HistogramCodec.AddTo(data, counts);
        }
        return map;
    }

    private static IEnumerable<(int Sub, byte[] Data)> Rows(KeyStatsDb db, DayRange range, HistogramKind kind)
    {
        using var cmd = db.Command($"SELECT sub, data FROM day_hist WHERE day >= $from AND day <= $to AND hist = {(int)kind}",
            range.FromDay, range.ToDay);
        using var r = cmd.ExecuteReader();
        while (r.Read()) yield return (r.GetInt32(0), KeyStatsDb.Blob(r, 1));
    }
}
