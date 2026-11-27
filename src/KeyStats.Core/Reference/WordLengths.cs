namespace KeyStats.Core.Reference;

/// <summary>Longueur des mots d'une langue de référence, en lettres.</summary>
/// <param name="Average">Lettres par mot.</param>
/// <param name="Shares">Part des mots de chaque longueur, l'indice 0 valant 1 lettre ; la dernière case compte aussi les plus longs.</param>
public sealed record WordLengthReference(double Average, IReadOnlyList<double> Shares, string Source)
{
    /// <summary>Part des mots de <paramref name="length"/> lettres ; la dernière case vaut pour toute longueur au-delà.</summary>
    public double Share(int length) => length < 1 ? 0 : Shares[Math.Min(length, Shares.Count) - 1];
}

/// <summary>Références de longueur des mots, calculées une fois depuis une base publiée.</summary>
public static class WordLengths
{
    /// <summary>
    /// Français : Lexique 4.15 (New, Pallier, Schalchli, Bourgin et Gimenes, 2026, Behavior Research Methods 58(5), 140 ;
    /// www.lexique.org, licence CC BY-SA 4.0). Chaque forme orthographique (1_Mot) compte pour sa fréquence
    /// 11_FreqOrtho, en occurrences par million dans un corpus de 316 millions de mots de sous-titres de films.
    /// Comme pour la frappe, une forme est coupée à tout ce qui n'est pas une lettre (j'ai → j + ai), puis
    /// chaque morceau est rangé selon son nombre de lettres. Les comptes ci-dessous sont ces occurrences
    /// par million, de 1 à 14 lettres puis 15 et plus ; la moyenne (3,95) est calculée sur les longueurs exactes.
    /// Le registre parlé des sous-titres donne des mots un peu plus courts qu'un texte écrit.
    /// </summary>
    public static WordLengthReference French { get; } = new(
        3.95,
        Normalize([89229, 247540, 150915, 153068, 98832, 75408, 57739, 40089, 21723, 13219, 5774, 3546, 1340, 681, 433]),
        "Lexique 4.15 (New et al., 2026), sous-titres de films, licence CC BY-SA 4.0");

    private static double[] Normalize(double[] counts)
    {
        double total = counts.Sum();
        return [.. counts.Select(c => c / total)];
    }
}
