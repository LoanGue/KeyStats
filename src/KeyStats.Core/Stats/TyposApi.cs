using KeyStats.Core.Analysis;
using KeyStats.Core.Storage;
using KeyStats.Core.Web;

namespace KeyStats.Core.Stats;

/// <param name="Day">Jour local (aaaa-mm-jj).</param>
/// <param name="Typed">Caractères tapés en sessions de prose.</param>
/// <param name="Accuracy">1 − fautes / caractères tapés ; null sans prose ce jour-là.</param>
public sealed record TyposDay(string Day, long Typed, double? Accuracy);

/// <param name="Typos">Fautes corrigées.</param>
/// <param name="Collateral">Caractères justes effacés pour atteindre une faute.</param>
/// <param name="Kspc">Touches de texte par caractère conservé ; null sans caractère conservé.</param>
/// <param name="Keys">Touches de texte (caractères, touches mortes, retours arrière).</param>
/// <param name="LostKeys">Touches qui n'ont pas laissé de caractère : <c>Keys</c> − caractères conservés.</param>
public sealed record TyposCost(long Typos, long Collateral, double? Kspc, long Keys, long LostKeys);

/// <param name="Typed">Caractère tapé à la place de <paramref name="Wanted"/>.</param>
/// <param name="Neighbors">Les deux touches se touchent sur le clavier AZERTY.</param>
public sealed record TyposPair(string Typed, string Wanted, bool Neighbors, long Count);

/// <param name="Typed">Fois où le caractère a été tapé en prose.</param>
/// <param name="Errors">Fautes sur ce caractère : tapé à tort à sa place, oublié, ou l'un des deux d'une inversion.</param>
public sealed record TyposChar(string Character, long Typed, long Errors);

/// <param name="Omissions">Lettre manquante.</param>
/// <param name="Insertions">Lettre en trop.</param>
public sealed record TyposTypes(long Substitutions, long Omissions, long Insertions, long Transpositions);

/// <param name="ToMs">Borne haute exclue ; null pour la dernière case, sans borne.</param>
public sealed record TyposDelayBin(double FromMs, double? ToMs, long Count);

/// <param name="MedianMs">Médiane approchée de l'histogramme ; null sans faute corrigée.</param>
/// <param name="Bins">Cases logarithmiques de 50 ms à 30 s, de la plus courte à la dernière non vide.</param>
public sealed record TyposDelay(double? MedianMs, IReadOnlyList<TyposDelayBin> Bins);

/// <summary>Caractères effacés selon la méthode.</summary>
/// <param name="Presses">Un retour arrière par caractère, premier appui d'une rafale compris.</param>
/// <param name="Held">Répétitions automatiques d'un retour arrière maintenu.</param>
/// <param name="Word">Caractères effacés par Ctrl+Retour arrière ; minorant, un appui sur un tampon inconnu compte pour un.</param>
/// <param name="WordPresses">Appuis sur Ctrl+Retour arrière.</param>
public sealed record TyposDeletion(long Presses, long Held, long Word, long WordPresses);

/// <param name="DayTablesUnfiltered">
/// Vrai avec un filtre d'appli, de nature ou de clavier, que les tables par jour ignorent : <c>Pairs</c>, <c>Accents</c>,
/// <c>Types</c> et <c>Delay</c> couvrent toute la prose de la période, <c>Deletion</c> toute la frappe, code compris.
/// </param>
/// <param name="Accuracy">Précision de la période, sessions de prose ; null sans prose.</param>
/// <param name="Days">Chaque jour de la période, du plus ancien au plus récent.</param>
/// <param name="Pairs">Substitutions les plus fréquentes, au plus <see cref="TyposApi.MaxPairs"/>.</param>
/// <param name="Accents">Caractères accentués, dans l'ordre de <see cref="TyposApi.AccentedChars"/>.</param>
public sealed record Typos(
    string From, string To, bool DayTablesUnfiltered, double? Accuracy, IReadOnlyList<TyposDay> Days, TyposCost Cost,
    IReadOnlyList<TyposPair> Pairs, IReadOnlyList<TyposChar> Accents, TyposTypes Types, TyposDelay Delay, TyposDeletion Deletion);

/// <summary>
/// Route <c>/api/typos</c> : précision jour par jour, paires confondues, caractères accentués, types de fautes,
/// délai de détection, coût des corrections et manière d'effacer. Les fautes ne sont analysées qu'en prose.
/// </summary>
public static class TyposApi
{
    public const string Path = "/api/typos";
    public const int MaxPairs = 50;
    public const string AccentedChars = "éèêëàâùûüîïôçœÉÈÊÀÇÔÎ";

    public static void Register(WebRouter router, StatsContext stats)
    {
        ArgumentNullException.ThrowIfNull(router);
        ArgumentNullException.ThrowIfNull(stats);
        router.Get(Path, q => stats.Respond(q, f => Read(stats.Db, f), StatsJson.Default.Typos));
    }

    public static Typos Read(KeyStatsDb db, StatsFilter filter)
    {
        ArgumentNullException.ThrowIfNull(db);
        ArgumentNullException.ThrowIfNull(filter);
        var range = filter.Range;
        var (accuracy, cost) = CostOf(db, filter);
        var metrics = TypoDayTables.Metrics(db, range);
        return new Typos(StatsFilter.Iso(range.FromDay), StatsFilter.Iso(range.ToDay), filter.HasSessionFilter, accuracy, DaysOf(db, filter),
            cost, TypoDayTables.Pairs(db, range), TypoDayTables.Accents(db, range), TypoDayTables.Types(metrics),
            TypoDayTables.Delay(db, range), TypoDayTables.Deletion(metrics));
    }

    private static string ProseWhere(StatsFilter filter, out object?[] values)
    {
        (string where, values) = filter.SessionWhere(filter.Range);
        return where + $" AND kind = {(int)SessionKind.Prose}";
    }

    private static double? Accuracy(double typed, double typos) => typed > 0 ? 1 - typos / typed : null;

    private static List<TyposDay> DaysOf(KeyStatsDb db, StatsFilter filter)
    {
        var byDay = new Dictionary<int, (double Typed, double Typos)>();
        using (var cmd = db.Command("SELECT day, TOTAL(typed), TOTAL(typos) FROM session WHERE "
            + ProseWhere(filter, out var values) + " GROUP BY day", values))
        using (var r = cmd.ExecuteReader())
        {
            while (r.Read()) byDay[r.GetInt32(0)] = (r.GetDouble(1), r.GetDouble(2));
        }
        var list = new List<TyposDay>(filter.Range.Days);
        for (var d = LocalDayClock.DateOf(filter.Range.FromDay); d <= LocalDayClock.DateOf(filter.Range.ToDay); d = d.AddDays(1))
        {
            int day = LocalDayClock.KeyOf(d);
            var s = byDay.GetValueOrDefault(day);
            list.Add(new TyposDay(StatsFilter.Iso(day), (long)s.Typed, Accuracy(s.Typed, s.Typos)));
        }
        return list;
    }

    private static (double? Accuracy, TyposCost Cost) CostOf(KeyStatsDb db, StatsFilter filter)
    {
        using var cmd = db.Command("SELECT TOTAL(typed), TOTAL(typos), TOTAL(collateral), TOTAL(text_keys), TOTAL(chars) FROM session WHERE "
            + ProseWhere(filter, out var values), values);
        using var r = cmd.ExecuteReader();
        r.Read();
        long keys = (long)r.GetDouble(3), chars = (long)r.GetDouble(4);
        return (Accuracy(r.GetDouble(0), r.GetDouble(1)),
            new TyposCost((long)r.GetDouble(1), (long)r.GetDouble(2), chars > 0 ? (double)keys / chars : null, keys, Math.Max(0, keys - chars)));
    }
}
