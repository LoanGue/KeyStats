using KeyStats.Core.Storage;

namespace KeyStats.Core.Analysis;

/// <summary>
/// Échelle logarithmique : des cases fines pour les petites durées, larges pour les grandes.
/// Les médianes calculées dessus ne sont pas faussées par les valeurs extrêmes.
/// </summary>
public sealed class LogScale
{
    public const int Buckets = 32;

    public static LogScale Hold { get; } = new(10, 2000);
    public static LogScale Flight { get; } = new(10, 2000);
    public static LogScale BigramLatency { get; } = new(10, 1000);
    public static LogScale DetectionDelay { get; } = new(50, 30000);
    public static LogScale Overlap { get; } = new(5, 1000);

    private readonly double _ratio;

    public LogScale(double min, double max)
    {
        Min = min;
        Max = max;
        _ratio = Math.Log(max / min);
    }

    public double Min { get; }
    public double Max { get; }

    public int Bucket(double value)
    {
        if (value <= Min) return 0;
        if (value >= Max) return Buckets - 1;
        int b = (int)(Buckets * Math.Log(value / Min) / _ratio);
        return Math.Clamp(b, 0, Buckets - 1);
    }

    /// <summary>Borne basse de la case (la case 0 commence à 0).</summary>
    public double LowerBound(int bucket) => bucket <= 0 ? 0 : Min * Math.Exp(_ratio * bucket / Buckets);

    public double UpperBound(int bucket) => bucket >= Buckets - 1 ? double.PositiveInfinity : Min * Math.Exp(_ratio * (bucket + 1) / Buckets);

    /// <summary>Milieu géométrique de la case, les cases ouvertes bornées à Min / 2 et Max × 1,5.</summary>
    public double Middle(int bucket)
    {
        double lo = Math.Max(LowerBound(bucket), Min / 2), hi = Math.Min(UpperBound(bucket), Max * 1.5);
        return Math.Sqrt(lo * hi);
    }

    /// <summary>Médiane approchée (milieu géométrique de la case médiane), ou NaN si vide.</summary>
    public double Median(ReadOnlySpan<int> counts)
    {
        long total = 0;
        foreach (int c in counts) total += c;
        if (total == 0) return double.NaN;
        long half = (total + 1) / 2, run = 0;
        for (int i = 0; i < counts.Length; i++)
        {
            run += counts[i];
            if (run >= half) return Middle(i);
        }
        return Max;
    }
}

/// <summary>Format des histogrammes en base : un entier variable par case, zéros de fin omis.</summary>
public static class HistogramCodec
{
    public static byte[] Encode(ReadOnlySpan<int> counts)
    {
        int n = counts.Length;
        while (n > 0 && counts[n - 1] == 0) n--;
        var w = new ByteWriter(n + 4);
        for (int i = 0; i < n; i++) w.WriteVarint((ulong)Math.Max(0, counts[i]));
        return w.ToArray();
    }

    /// <summary>Ajoute le contenu encodé aux compteurs (fusion avec l'existant).</summary>
    public static void AddTo(ReadOnlySpan<byte> data, Span<int> counts)
    {
        int pos = 0, i = 0;
        while (pos < data.Length)
        {
            int v = Varint.ReadInt(data, ref pos);
            if (i < counts.Length) counts[i] += v;
            i++;
        }
    }

    public static int[] Decode(ReadOnlySpan<byte> data, int size)
    {
        var counts = new int[size];
        AddTo(data, counts);
        return counts;
    }
}
