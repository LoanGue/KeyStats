using KeyStats.Core.Analysis;
using KeyStats.Core.Reference;
using KeyStats.Core.Storage;
using KeyStats.Core.Web;

namespace KeyStats.Core.Stats;

/// <param name="Letter">Lettre a–z ; les accents et les majuscules y sont ramenés (é → e).</param>
/// <param name="Count">Fois où la lettre a été tapée sur la période.</param>
/// <param name="French">Part de la lettre parmi les lettres a–z du français de référence.</param>
/// <param name="English">Part de la lettre parmi les lettres a–z de l'anglais de référence.</param>
public sealed record LettersLetter(string Letter, long Count, double French, double English);

/// <param name="Length">Nombre de lettres ; la dernière case, <see cref="LettersApi.MaxLength"/>, compte aussi les mots plus longs.</param>
/// <param name="Count">Mots de cette longueur tapés sur la période.</param>
/// <param name="French">Part de cette longueur parmi les mots du français de référence ; null sans répartition publiée.</param>
public sealed record LettersWordLength(int Length, long Count, double? French);

/// <param name="Average">Lettres par mot, toutes natures de session ; null sans mot.</param>
/// <param name="Median">Longueur médiane, la plus courte des deux si le nombre de mots est pair ; null sans mot.</param>
/// <param name="Prose">Lettres par mot en prose ; null sans mot en prose.</param>
/// <param name="Code">Lettres par mot dans les sessions de code ; null sans mot en code.</param>
/// <param name="French">
/// Longueur moyenne d'un mot en français de référence, en caractères ponctuation collée comprise :
/// elle majore un peu la mesure en lettres seules faite ici.
/// </param>
public sealed record LettersWords(long Count, double? Average, int? Median, double? Prose, double? Code, double French);

/// <param name="DayTablesUnfiltered">
/// Vrai avec un filtre d'appli ou de clavier, que les tables par jour ignorent : tout est alors compté sur la période.
/// Le filtre de nature, lui, s'applique.
/// </param>
/// <param name="Letters">Les 26 lettres de a à z.</param>
/// <param name="WordLengths">Longueurs de 1 à <see cref="LettersApi.MaxLength"/>.</param>
/// <param name="Sources">Sources des références : français des lettres, anglais des lettres, français des mots.</param>
public sealed record LettersStats(
    string From, string To, bool DayTablesUnfiltered, IReadOnlyList<LettersLetter> Letters,
    IReadOnlyList<LettersWordLength> WordLengths, LettersWords Words, IReadOnlyList<string> Sources);

/// <summary>
/// Route <c>/api/letters</c> : fréquence de chaque lettre et longueur des mots, comparées au français
/// (et à l'anglais pour les lettres). Lu dans <c>day_char</c> et <c>day_wordlen</c>.
/// </summary>
public static class LettersApi
{
    public const string Path = "/api/letters";
    public const int MaxLength = 15;

    public static void Register(WebRouter router, StatsContext stats)
    {
        ArgumentNullException.ThrowIfNull(router);
        ArgumentNullException.ThrowIfNull(stats);
        router.Get(Path, q => stats.Respond(q, f => Read(stats.Db, f), StatsJson.Default.LettersStats));
    }

    public static LettersStats Read(KeyStatsDb db, StatsFilter filter)
    {
        ArgumentNullException.ThrowIfNull(db);
        ArgumentNullException.ThrowIfNull(filter);
        var lengths = LengthsByKind(db, filter);
        var all = new long[SessionBuilder.MaxWordLength + 1];
        foreach (var counts in lengths.Values)
        {
            for (int i = 0; i < all.Length; i++) all[i] += counts[i];
        }
        var words = new LettersWords(all.Sum(), Average(all), Median(all), Average(lengths.GetValueOrDefault(SessionKind.Prose)),
            Average(lengths.GetValueOrDefault(SessionKind.Code)), WordLengths.French.Average);
        return new LettersStats(StatsFilter.Iso(filter.Range.FromDay), StatsFilter.Iso(filter.Range.ToDay),
            filter.AppId is not null || filter.DeviceId is not null, LettersOf(db, filter), Bins(all), words,
            [LetterFrequencies.French.Source, LetterFrequencies.English.Source, WordLengths.French.Source]);
    }

    private static string Where(StatsFilter filter, out object?[] values)
    {
        values = filter.Kind is { } kind ? [filter.Range.FromDay, filter.Range.ToDay, (int)kind] : [filter.Range.FromDay, filter.Range.ToDay];
        return filter.Kind is null ? "day >= $from AND day <= $to" : "day >= $from AND day <= $to AND kind = $kind";
    }

    private static List<LettersLetter> LettersOf(KeyStatsDb db, StatsFilter filter)
    {
        var counts = new long[26];
        using (var cmd = db.Command($"SELECT ch, TOTAL(n) FROM day_char WHERE {Where(filter, out var values)} GROUP BY ch", values))
        using (var r = cmd.ExecuteReader())
        {
            while (r.Read())
            {
                char c = LetterFrequencies.Fold((char)r.GetInt32(0));
                if (c is >= 'a' and <= 'z') counts[c - 'a'] += (long)r.GetDouble(1);
            }
        }
        return [.. Enumerable.Range(0, 26).Select(i => new LettersLetter(((char)('a' + i)).ToString(), counts[i], FrenchShares[i], EnglishShares[i]))];
    }

    private static readonly double[] FrenchShares = Folded(LetterFrequencies.French);
    private static readonly double[] EnglishShares = Folded(LetterFrequencies.English);

    /// <summary>Parts a–z d'une référence, accents ramenés à leur lettre ; les lettres hors a–z (œ) sont écartées.</summary>
    private static double[] Folded(LanguageReference reference)
    {
        var shares = new double[26];
        foreach (var (letter, share) in reference.Letters)
        {
            char c = LetterFrequencies.Fold(letter);
            if (c is >= 'a' and <= 'z') shares[c - 'a'] += share;
        }
        double total = shares.Sum();
        for (int i = 0; i < shares.Length; i++) shares[i] /= total;
        return shares;
    }

    private static Dictionary<SessionKind, long[]> LengthsByKind(KeyStatsDb db, StatsFilter filter)
    {
        var map = new Dictionary<SessionKind, long[]>();
        using var cmd = db.Command($"SELECT kind, len, TOTAL(n) FROM day_wordlen WHERE {Where(filter, out var values)} GROUP BY kind, len", values);
        using var r = cmd.ExecuteReader();
        while (r.Read())
        {
            var kind = (SessionKind)r.GetInt32(0);
            int len = Math.Min(r.GetInt32(1), SessionBuilder.MaxWordLength);
            if (len < 1) continue; // l'écrivain n'en produit pas : une telle ligne n'est rangée nulle part
            if (!map.TryGetValue(kind, out var counts)) map[kind] = counts = new long[SessionBuilder.MaxWordLength + 1];
            counts[len] += (long)r.GetDouble(2);
        }
        return map;
    }

    private static List<LettersWordLength> Bins(long[] counts) =>
        [.. Enumerable.Range(1, MaxLength).Select(len => new LettersWordLength(len,
            len < MaxLength ? counts[len] : counts.Skip(MaxLength).Sum(), WordLengths.French.Share(len)))];

    private static double? Average(long[]? counts)
    {
        if (counts is null) return null;
        long words = 0, letters = 0;
        for (int len = 1; len < counts.Length; len++)
        {
            words += counts[len];
            letters += len * counts[len];
        }
        return words > 0 ? (double)letters / words : null;
    }

    private static int? Median(long[] counts)
    {
        long total = counts.Sum(), seen = 0;
        if (total == 0) return null;
        for (int len = 1; len < counts.Length; len++)
        {
            seen += counts[len];
            if (seen * 2 >= total) return len;
        }
        return counts.Length - 1;
    }
}
