namespace KeyStats.Core.Analysis;

/// <summary>Seuils de l'analyse. Tous réglables ; les valeurs par défaut sont celles du plan.</summary>
public sealed class AnalysisSettings
{
    public static AnalysisSettings Default { get; } = new();

    /// <summary>Écart maximal entre deux touches de texte d'une même session.</summary>
    public int SessionGapMs { get; init; } = 5000;

    /// <summary>Les écarts plus longs sont des pauses, exclues du temps actif.</summary>
    public int ActiveGapMs { get; init; } = 2000;

    // Conditions pour qu'une session compte comme de l'écriture.
    public int MinNetChars { get; init; } = 30;
    public int MinSpaces { get; init; } = 3;
    public int MinDurationMs { get; init; } = 8000;
    public double MinTextRatio { get; init; } = 0.75;
    /// <summary>Caractères différents : écarte le martèlement de quelques touches (ZQSD dans un jeu).</summary>
    public int MinDistinctChars { get; init; } = 10;
    /// <summary>Part maximale de répétitions automatiques (touche maintenue).</summary>
    public double MaxRepeatRatio { get; init; } = 0.3;

    // Texte / mixte / code selon la part de symboles.
    public double ProseMaxSymbolRatio { get; init; } = 0.03;
    public double CodeMinSymbolRatio { get; init; } = 0.08;

    public int PeakWindowMs { get; init; } = 30000;
    public int HoldCapMs { get; init; } = 2000;
    public int BigramMaxLatencyMs { get; init; } = 1000;
    /// <summary>Un Ctrl gauche juste avant AltGr est le faux Ctrl que Windows ajoute.</summary>
    public int FakeCtrlMs { get; init; } = 5;
    /// <summary>Au-delà, un effacement est une reformulation, pas une faute.</summary>
    public int MaxCorrectionLength { get; init; } = 40;
    /// <summary>Une session plus longue est coupée en deux (borne la mémoire).</summary>
    public int MaxSessionChars { get; init; } = 20000;

    // Compteur en direct.
    public int LiveShowMinChars { get; init; } = 12;
    public int LiveShowMinSpaces { get; init; } = 2;
    public int LiveShowWindowMs { get; init; } = 6000;
    public int LiveSpeedWindowMs { get; init; } = 8000;
}
