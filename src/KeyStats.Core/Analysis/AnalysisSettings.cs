namespace KeyStats.Core.Analysis;

/// <summary>Seuils de l'analyse. Tous réglables ; les valeurs par défaut sont celles du plan.</summary>
public sealed class AnalysisSettings
{
    /// <summary>Une instance neuve à chaque appel : les seuils sont modifiables (désérialisation), aucune n'est partagée.</summary>
    public static AnalysisSettings Default => new();

    /// <summary>Écart maximal entre deux touches de texte d'une même session.</summary>
    public int SessionGapMs { get; set; } = 5000;

    /// <summary>Les écarts plus longs sont des pauses, exclues du temps actif.</summary>
    public int ActiveGapMs { get; set; } = 2000;

    // Conditions pour qu'une session compte comme de l'écriture.
    public int MinNetChars { get; set; } = 30;
    public int MinSpaces { get; set; } = 3;
    public int MinDurationMs { get; set; } = 8000;
    public double MinTextRatio { get; set; } = 0.75;
    /// <summary>Caractères différents : écarte le martèlement de quelques touches (ZQSD dans un jeu).</summary>
    public int MinDistinctChars { get; set; } = 10;
    /// <summary>Part maximale de répétitions automatiques (touche maintenue).</summary>
    public double MaxRepeatRatio { get; set; } = 0.3;

    // Texte / mixte / code selon la part de symboles.
    public double ProseMaxSymbolRatio { get; set; } = 0.03;
    public double CodeMinSymbolRatio { get; set; } = 0.08;

    public int PeakWindowMs { get; set; } = 30000;
    public int HoldCapMs { get; set; } = 2000;
    public int BigramMaxLatencyMs { get; set; } = 1000;
    /// <summary>Un Ctrl gauche juste avant AltGr est le faux Ctrl que Windows ajoute.</summary>
    public int FakeCtrlMs { get; set; } = 5;
    /// <summary>Au-delà, un effacement est une reformulation, pas une faute.</summary>
    public int MaxCorrectionLength { get; set; } = 40;
    /// <summary>Une session plus longue est coupée en deux (borne la mémoire).</summary>
    public int MaxSessionChars { get; set; } = 20000;

    // Compteur en direct.
    public int LiveShowMinChars { get; set; } = 12;
    public int LiveShowMinSpaces { get; set; } = 2;
    public int LiveShowWindowMs { get; set; } = 6000;
    public int LiveSpeedWindowMs { get; set; } = 8000;
}
