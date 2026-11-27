using KeyStats.Core.Analysis;
using KeyStats.Core.Storage;
using KeyStats.Core.Text;

namespace KeyStats.Core.Stats;

/// <summary>Mesures par touche de <c>/api/keyboard</c>, indexées par code de touche.</summary>
internal static class KeyboardTables
{
    private const string InRange = "day >= $from AND day <= $to";

    /// <summary>Appuis par touche dans <c>key_minute</c>, sous la condition <paramref name="where"/>.</summary>
    public static Dictionary<int, long> Presses(KeyStatsDb db, string where, object?[] values)
    {
        var map = new Dictionary<int, long>();
        using var cmd = db.Command($"SELECT key, SUM(presses) FROM key_minute WHERE {where} GROUP BY key HAVING SUM(presses) > 0", values);
        using var r = cmd.ExecuteReader();
        while (r.Read()) map[r.GetInt32(0)] = r.GetInt64(1);
        return map;
    }

    /// <summary>
    /// Fautes de prose dont le caractère tapé à tort vient de la touche (substitution, lettre en trop), rapportées aux
    /// caractères de prose tapés par cette touche, toutes cases confondues (e et E).
    /// </summary>
    public static Dictionary<int, double> ErrorRates(KeyStatsDb db, DayRange range)
    {
        var typed = ByKey(db, $"SELECT ch, TOTAL(n) FROM day_char WHERE {InRange} AND kind = {(int)SessionKind.Prose} GROUP BY ch", range);
        var errors = ByKey(db, $"SELECT typed, TOTAL(n) FROM day_error_pair WHERE {InRange} AND type IN "
            + $"({(int)ErrorType.Substitution}, {(int)ErrorType.Insertion}) GROUP BY typed", range);
        return typed.Where(kv => kv.Value > 0)
            .ToDictionary(kv => kv.Key, kv => (double)errors.GetValueOrDefault(kv.Key) / kv.Value);
    }

    private static Dictionary<int, long> ByKey(KeyStatsDb db, string sql, DayRange range)
    {
        var map = new Dictionary<int, long>();
        using var cmd = db.Command(sql, range.FromDay, range.ToDay);
        using var r = cmd.ExecuteReader();
        while (r.Read())
        {
            if (!LayoutTable.FrenchAzerty.TryFindKey((char)r.GetInt32(0), out ushort key, out _)) continue;
            map[key] = map.GetValueOrDefault(key) + (long)r.GetDouble(1);
        }
        return map;
    }

    /// <summary>Durée d'appui médiane par touche (<c>day_hist</c>, sous-clé = code de touche).</summary>
    public static Dictionary<int, double> HoldMedians(KeyStatsDb db, DayRange range)
    {
        var sums = new Dictionary<int, int[]>();
        using (var cmd = db.Command($"SELECT sub, data FROM day_hist WHERE {InRange} AND hist = {(int)HistogramKind.HoldMs}",
            range.FromDay, range.ToDay))
        using (var r = cmd.ExecuteReader())
        {
            while (r.Read()) HistogramCodec.AddTo(KeyStatsDb.Blob(r, 1), Counts(sums, r.GetInt32(0)));
        }
        return Medians(sums, LogScale.Hold);
    }

    /// <summary>Délai médian avant la touche : latence des bigrammes (<c>day_bigram</c>) rangée par touche du second caractère.</summary>
    public static Dictionary<int, double> DelayMedians(KeyStatsDb db, DayRange range)
    {
        var sums = new Dictionary<int, int[]>();
        using (var cmd = db.Command($"SELECT b, lat FROM day_bigram WHERE {InRange} AND lat IS NOT NULL", range.FromDay, range.ToDay))
        using (var r = cmd.ExecuteReader())
        {
            while (r.Read())
            {
                if (LayoutTable.FrenchAzerty.TryFindKey((char)r.GetInt32(0), out ushort key, out _))
                    HistogramCodec.AddTo(KeyStatsDb.Blob(r, 1), Counts(sums, key));
            }
        }
        return Medians(sums, LogScale.BigramLatency);
    }

    private static int[] Counts(Dictionary<int, int[]> sums, int key)
    {
        if (!sums.TryGetValue(key, out var counts)) sums[key] = counts = new int[LogScale.Buckets];
        return counts;
    }

    private static Dictionary<int, double> Medians(Dictionary<int, int[]> sums, LogScale scale)
    {
        var map = new Dictionary<int, double>();
        foreach (var (key, counts) in sums)
        {
            double median = scale.Median(counts);
            if (!double.IsNaN(median)) map[key] = median;
        }
        return map;
    }

    /// <summary>Chiffres de 0 à 9, selon la rangée du haut ou le pavé (compteurs journaliers).</summary>
    public static List<KeyboardDigit> Digits(KeyStatsDb db, DayRange range)
    {
        var metrics = TypoDayTables.Metrics(db, range);
        return [.. Enumerable.Range(0, 10).Select(d => new KeyboardDigit(d,
            metrics.GetValueOrDefault(DayMetric.TopRowDigit0 + d), metrics.GetValueOrDefault(DayMetric.NumpadDigit0 + d)))];
    }
}
