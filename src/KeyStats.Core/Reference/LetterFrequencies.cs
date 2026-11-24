using System.Collections.Frozen;

namespace KeyStats.Core.Reference;

/// <summary>Fréquences de référence d'une langue, pour comparer avec ta propre frappe.</summary>
/// <param name="Letters">Part de chaque lettre parmi les lettres (somme = 1), accents compris s'ils sont dans la source.</param>
/// <param name="AverageWordLength">Longueur moyenne des mots en lettres ; null si aucune source fiable n'a été trouvée.</param>
public sealed record LanguageReference(
    string Code, string Name, FrozenDictionary<char, double> Letters, double? AverageWordLength, string Source);

/// <summary>Références publiées. Les chiffres sont recopiés de leur source, sans retouche.</summary>
public static class LetterFrequencies
{
    /// <summary>
    /// Français : décompte des lettres du corpus « Wikipédia FR 2008 » (REDAC, laboratoire CLLE-ERSS,
    /// Université de Toulouse), tel que repris par l'article « Fréquence d'apparition des lettres »
    /// de Wikipédia et par bepo.fr. Les comptes bruts sont ramenés à une part du total des lettres.
    /// </summary>
    public static LanguageReference French { get; } = new(
        "fr", "Français",
        Normalize(new (char, double)[]
        {
            ('e', 115024205), ('a', 67563628), ('i', 62672992), ('s', 61882785), ('n', 60728196),
            ('r', 57656209), ('t', 56267109), ('o', 47724400), ('l', 47171247), ('u', 42698875),
            ('d', 34914685), ('c', 30219574), ('m', 24894034), ('p', 23647179), ('é', 18451937),
            ('g', 11684140), ('b', 10817171), ('v', 10590858), ('h', 10583562), ('f', 10579192),
            ('q', 6140307), ('y', 4351953), ('x', 3588990), ('j', 3276064), ('è', 2969466),
            ('à', 2966029), ('k', 2747547), ('w', 1653435), ('z', 1433913), ('ê', 802211),
            ('ç', 544509), ('ô', 357197), ('â', 320837), ('î', 280201), ('û', 164516),
            ('ù', 151236), ('ï', 138221), ('á', 73751), ('ü', 55172), ('ë', 53862),
            ('ö', 51020), ('í', 48391),
        }),
        AverageWordLength: null, // [à compléter : longueur moyenne des mots en français, avec sa source]
        "Corpus Wikipédia FR 2008 (REDAC, CLLE-ERSS), via fr.wikipedia.org « Fréquence d'apparition des lettres »");

    /// <summary>
    /// Anglais : tableau de l'article « Letter frequency » de Wikipédia en anglais (souvent attribué
    /// à Lewand, 2000), en pourcentage des lettres a–z.
    /// </summary>
    public static LanguageReference English { get; } = new(
        "en", "Anglais",
        Normalize(new (char, double)[]
        {
            ('a', 8.167), ('b', 1.492), ('c', 2.782), ('d', 4.253), ('e', 12.702), ('f', 2.228),
            ('g', 2.015), ('h', 6.094), ('i', 6.966), ('j', 0.153), ('k', 0.772), ('l', 4.025),
            ('m', 2.406), ('n', 6.749), ('o', 7.507), ('p', 1.929), ('q', 0.095), ('r', 5.987),
            ('s', 6.327), ('t', 9.056), ('u', 2.758), ('v', 0.978), ('w', 2.360), ('x', 0.150),
            ('y', 1.974), ('z', 0.074),
        }),
        AverageWordLength: null, // [à compléter : longueur moyenne des mots en anglais, avec sa source]
        "en.wikipedia.org « Letter frequency » (Lewand, 2000)");

    public static IReadOnlyList<LanguageReference> All { get; } = [French, English];

    /// <summary>Lettre sans accent (é → e, ç → c), pour comparer avec une source qui ne les distingue pas.</summary>
    public static char Fold(char c)
    {
        c = char.ToLowerInvariant(c);
        return c switch
        {
            'à' or 'â' or 'á' or 'ä' or 'ã' => 'a',
            'é' or 'è' or 'ê' or 'ë' => 'e',
            'î' or 'ï' or 'í' or 'ì' => 'i',
            'ô' or 'ö' or 'ó' or 'ò' or 'õ' => 'o',
            'ù' or 'û' or 'ü' or 'ú' => 'u',
            'ç' => 'c',
            'ÿ' => 'y',
            'ñ' => 'n',
            _ => c,
        };
    }

    private static FrozenDictionary<char, double> Normalize((char Letter, double Count)[] counts)
    {
        double total = 0;
        foreach (var (_, n) in counts) total += n;
        var d = new Dictionary<char, double>(counts.Length);
        foreach (var (c, n) in counts) d[c] = n / total;
        return d.ToFrozenDictionary();
    }
}
