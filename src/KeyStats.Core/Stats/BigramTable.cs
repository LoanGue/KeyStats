using KeyStats.Core.Analysis;
using KeyStats.Core.Input;
using KeyStats.Core.Storage;
using KeyStats.Core.Text;

namespace KeyStats.Core.Stats;

/// <param name="A">Premier caractère.</param>
/// <param name="B">Caractère tapé juste après <paramref name="A"/>.</param>
/// <param name="Count">Fois où l'enchaînement a été tapé dans une session.</param>
/// <param name="DelayMs">Délai moyen de l'enchaînement moins le délai médian de la période ; négatif s'il est plus rapide.</param>
/// <param name="CostMs">
/// Temps perdu sur la période : <paramref name="Count"/> × <paramref name="DelayMs"/>. Le délai moyen ne porte que sur les
/// enchaînements mesurés (sans pause), le compte sur tous.
/// </param>
/// <param name="ErrorRate">
/// Part des tentatives ratées : fautes sur <paramref name="B"/> juste après <paramref name="A"/>, rapportées aux fautes plus
/// <paramref name="Count"/>. Les fautes ne sont relevées qu'en prose, alors que <paramref name="Count"/> couvre toutes les sessions.
/// </param>
public sealed record RhythmBigram(string A, string B, long Count, double DelayMs, double CostMs, double ErrorRate);

/// <param name="Bigrams">Enchaînements de deux touches de lettres, chiffres ou ponctuation, hors pouces.</param>
/// <param name="SameFinger">Parmi eux, deux touches différentes tapées par le même doigt.</param>
/// <param name="Alternating">Parmi eux, passage d'une main à l'autre.</param>
public sealed record RhythmFlow(long Bigrams, long SameFinger, long Alternating);

/// <summary>Bigrammes de la période (table <c>day_bigram</c>), sommés sur les jours.</summary>
internal sealed class BigramTable
{
    private readonly Dictionary<(char A, char B), Totals> _pairs = [];

    /// <summary>Retard minimal pour entrer au classement par coût : la page affiche le retard à la milliseconde près.</summary>
    public const double MinDelayMs = 0.5;

    private BigramTable() { }

    /// <summary>Délai médian de tous les enchaînements mesurés ; null sans mesure.</summary>
    public double? MedianMs { get; private set; }

    public static BigramTable Read(KeyStatsDb db, DayRange range)
    {
        var table = new BigramTable();
        var all = new int[LogScale.Buckets];
        using (var cmd = db.Command("SELECT a, b, n, errors, lat FROM day_bigram WHERE day >= $from AND day <= $to", range.FromDay, range.ToDay))
        using (var r = cmd.ExecuteReader())
        {
            while (r.Read())
            {
                char a = (char)r.GetInt32(0), b = (char)r.GetInt32(1);
                if (char.IsControl(a) || char.IsControl(b)) continue;
                if (!table._pairs.TryGetValue((a, b), out var t)) table._pairs[(a, b)] = t = new Totals();
                t.Count += r.GetInt64(2);
                t.Errors += r.GetInt64(3);
                var lat = KeyStatsDb.Blob(r, 4);
                HistogramCodec.AddTo(lat, t.Latency);
                HistogramCodec.AddTo(lat, all);
            }
        }
        double median = LogScale.BigramLatency.Median(all);
        table.MedianMs = double.IsNaN(median) ? null : median;
        return table;
    }

    /// <summary>
    /// Les <paramref name="max"/> plus coûteux, plus lents que la médiane d'au moins <see cref="MinDelayMs"/>, et les <paramref name="max"/> au plus fort taux de
    /// faute, parmi ceux tapés au moins <paramref name="minCount"/> fois et dont le délai est mesuré ; du plus coûteux au moins
    /// coûteux.
    /// </summary>
    public List<RhythmBigram> Ranked(int minCount, int max)
    {
        if (MedianMs is not { } median) return [];
        var rows = _pairs
            .Where(p => p.Value.Count >= minCount && p.Value.Latency.AsSpan().ContainsAnyExcept(0))
            .Select(p =>
            {
                double delay = Mean(p.Value.Latency) - median;
                return new RhythmBigram(p.Key.A.ToString(), p.Key.B.ToString(), p.Value.Count, delay, p.Value.Count * delay,
                    (double)p.Value.Errors / (p.Value.Count + p.Value.Errors));
            })
            .ToList();
        var byCost = Ordered(rows.Where(b => b.DelayMs >= MinDelayMs).OrderByDescending(b => b.CostMs)).Take(max);
        var byErrors = Ordered(rows.Where(b => b.ErrorRate > 0).OrderByDescending(b => b.ErrorRate).ThenByDescending(b => b.Count)).Take(max);
        return [.. Ordered(byCost.Union(byErrors).OrderByDescending(b => b.CostMs))];
    }

    private static IOrderedEnumerable<RhythmBigram> Ordered(IOrderedEnumerable<RhythmBigram> rows) =>
        rows.ThenBy(b => b.A, StringComparer.Ordinal).ThenBy(b => b.B, StringComparer.Ordinal);

    /// <summary>
    /// Même doigt et alternance des mains, selon le doigté AZERTY standard. Seuls comptent les enchaînements de deux
    /// touches tenues par les doigts : l'espace, au pouce, n'appartient à aucune main. Un caractère est rattaché à la première
    /// touche qui l'écrit sans touche morte ; ceux composés par une touche morte (ê, â…) sont laissés de côté.
    /// </summary>
    public RhythmFlow Flow()
    {
        long total = 0, same = 0, alternating = 0;
        foreach (var (pair, t) in _pairs)
        {
            if (!Placed(pair.A, out var ka) || !Placed(pair.B, out var kb)) continue;
            total += t.Count;
            if (ka.Finger == kb.Finger && ka.Code != kb.Code) same += t.Count;
            if (ka.IsLeftHand != kb.IsLeftHand) alternating += t.Count;
        }
        return new RhythmFlow(total, same, alternating);
    }

    private static bool Placed(char c, out KeyPlacement placement)
    {
        placement = default;
        return LayoutTable.FrenchAzerty.TryFindKey(c, out ushort key, out _) && KeyboardGeometry.TryFind(key, out placement)
            && placement.Finger is not (Finger.None or Finger.LeftThumb or Finger.RightThumb);
    }

    /// <summary>Moyenne des milieux de case, pondérée par leur effectif.</summary>
    private static double Mean(int[] counts)
    {
        double sum = 0;
        long n = 0;
        for (int i = 0; i < counts.Length; i++)
        {
            sum += counts[i] * LogScale.BigramLatency.Middle(i);
            n += counts[i];
        }
        return sum / n;
    }

    private sealed class Totals
    {
        public long Count;
        public long Errors;
        public readonly int[] Latency = new int[LogScale.Buckets];
    }
}
