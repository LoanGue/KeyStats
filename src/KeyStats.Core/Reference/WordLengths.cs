namespace KeyStats.Core.Reference;

/// <summary>Longueur des mots d'une langue de référence.</summary>
/// <param name="Average">Longueur moyenne d'un mot, dans l'unité de la source (voir <paramref name="Source"/>).</param>
/// <param name="Shares">
/// Part des mots de chaque longueur, l'indice 0 valant 1 lettre ; null tant qu'aucune répartition publiée n'a été retenue.
/// </param>
public sealed record WordLengthReference(double Average, IReadOnlyList<double>? Shares, string Source)
{
    /// <summary>Part des mots de <paramref name="length"/> lettres, ou null sans répartition de référence.</summary>
    public double? Share(int length) =>
        Shares is not null && length >= 1 && length <= Shares.Count ? Shares[length - 1] : null;
}

/// <summary>Références publiées de longueur des mots. Les chiffres sont recopiés de leur source, sans retouche.</summary>
public static class WordLengths
{
    /// <summary>
    /// Français : 4,8 caractères par mot en moyenne (médiane 4), mesurés sur des extraits de Wikisource
    /// (Zola, L'Assommoir ; Voltaire, Candide ; Proust, À l'ombre des jeunes filles en fleurs) dans le billet
    /// d'Aabu « Utilise-t-on plutôt des mots courts ou longs ? », Zeste de Savoir, 21 janvier 2018
    /// (https://zestedesavoir.com/billets/2393/). Les mots y sont coupés aux espaces seulement, ponctuation comprise.
    /// Le billet ne publie la répartition par longueur qu'en graphique, sans valeurs : elle n'est pas reprise.
    /// </summary>
    public static WordLengthReference French { get; } = new(
        4.8, Shares: null,
        "Aabu, « Utilise-t-on plutôt des mots courts ou longs ? », Zeste de Savoir, 2018 (Zola, Voltaire, Proust ; caractères par mot, ponctuation comprise)");
}
