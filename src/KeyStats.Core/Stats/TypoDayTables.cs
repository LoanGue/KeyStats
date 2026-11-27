using KeyStats.Core.Analysis;
using KeyStats.Core.Input;
using KeyStats.Core.Storage;
using KeyStats.Core.Text;

namespace KeyStats.Core.Stats;

/// <summary>Parties de <c>/api/typos</c> lues dans les tables par jour, sans filtre de session.</summary>
internal static class TypoDayTables
{
    private const string InRange = "day >= $from AND day <= $to";

    public static List<TyposPair> Pairs(KeyStatsDb db, DayRange range)
    {
        using var cmd = db.Command(
            $"SELECT typed, wanted, TOTAL(n) AS c FROM day_error_pair WHERE {InRange} AND type = {(int)ErrorType.Substitution} "
            + $"GROUP BY typed, wanted ORDER BY c DESC, typed, wanted LIMIT {TyposApi.MaxPairs}", range.FromDay, range.ToDay);
        using var r = cmd.ExecuteReader();
        var list = new List<TyposPair>();
        while (r.Read())
        {
            char typed = (char)r.GetInt32(0), wanted = (char)r.GetInt32(1);
            list.Add(new TyposPair(typed.ToString(), wanted.ToString(), AreNeighbors(typed, wanted), (long)r.GetDouble(2)));
        }
        return list;
    }

    /// <summary>Touches voisines sur l'AZERTY français, quelle que soit la case (Maj, AltGr) de chaque caractère.</summary>
    public static bool AreNeighbors(char a, char b) =>
        LayoutTable.FrenchAzerty.TryFindKey(a, out ushort ka, out _) && LayoutTable.FrenchAzerty.TryFindKey(b, out ushort kb, out _)
        && KeyboardGeometry.AreNeighbors(ka, kb);

    public static List<TyposChar> Accents(KeyStatsDb db, DayRange range)
    {
        var typed = CountByChar(db,
            $"SELECT ch, TOTAL(n) FROM day_char WHERE {InRange} AND kind = {(int)SessionKind.Prose} GROUP BY ch", range);
        var errors = CountByChar(db, $"SELECT wanted, TOTAL(n) FROM day_error_pair WHERE {InRange} GROUP BY wanted", range);
        return [.. TyposApi.AccentedChars.Select(c =>
            new TyposChar(c.ToString(), typed.GetValueOrDefault(c), errors.GetValueOrDefault(c)))];
    }

    private static Dictionary<char, long> CountByChar(KeyStatsDb db, string sql, DayRange range)
    {
        var map = new Dictionary<char, long>();
        using var cmd = db.Command(sql, range.FromDay, range.ToDay);
        using var r = cmd.ExecuteReader();
        while (r.Read())
        {
            char c = (char)r.GetInt32(0);
            if (TyposApi.AccentedChars.Contains(c)) map[c] = (long)r.GetDouble(1);
        }
        return map;
    }

    public static TyposTypes Types(Dictionary<DayMetric, long> m) =>
        new(m.GetValueOrDefault(DayMetric.Substitutions), m.GetValueOrDefault(DayMetric.Omissions),
            m.GetValueOrDefault(DayMetric.Insertions), m.GetValueOrDefault(DayMetric.Transpositions));

    public static TyposDeletion Deletion(Dictionary<DayMetric, long> m)
    {
        long presses = m.GetValueOrDefault(DayMetric.Backspaces), held = m.GetValueOrDefault(DayMetric.BackspaceRepeats);
        return new TyposDeletion(Math.Max(0, presses - held), held, m.GetValueOrDefault(DayMetric.WordBackspaceChars),
            m.GetValueOrDefault(DayMetric.WordBackspaces));
    }

    public static Dictionary<DayMetric, long> Metrics(KeyStatsDb db, DayRange range)
    {
        var map = new Dictionary<DayMetric, long>();
        using var cmd = db.Command($"SELECT metric, TOTAL(value) FROM day_metric WHERE {InRange} GROUP BY metric", range.FromDay, range.ToDay);
        using var r = cmd.ExecuteReader();
        while (r.Read()) map[(DayMetric)r.GetInt32(0)] = (long)r.GetDouble(1);
        return map;
    }

    public static TyposDelay Delay(KeyStatsDb db, DayRange range)
    {
        var counts = new int[LogScale.Buckets];
        using (var cmd = db.Command($"SELECT data FROM day_hist WHERE {InRange} AND hist = {(int)HistogramKind.DetectionDelayMs} AND sub = 0",
            range.FromDay, range.ToDay))
        using (var r = cmd.ExecuteReader())
        {
            while (r.Read()) HistogramCodec.AddTo(KeyStatsDb.Blob(r, 0), counts);
        }
        var scale = LogScale.DetectionDelay;
        int last = Array.FindLastIndex(counts, c => c > 0);
        var bins = new List<TyposDelayBin>(last + 1);
        for (int i = 0; i <= last; i++)
        {
            double upper = scale.UpperBound(i);
            bins.Add(new TyposDelayBin(scale.LowerBound(i), double.IsPositiveInfinity(upper) ? null : upper, counts[i]));
        }
        double median = scale.Median(counts);
        return new TyposDelay(double.IsNaN(median) ? null : median, bins);
    }
}
