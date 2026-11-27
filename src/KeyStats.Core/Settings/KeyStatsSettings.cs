using KeyStats.Core.Analysis;
using KeyStats.Core.Focus;

namespace KeyStats.Core.Settings;

/// <summary>
/// Contenu de <c>settings.json</c>, lu par l'agent et écrit par le tableau de bord. Une clé absente
/// garde sa valeur par défaut ; une clé inconnue est ignorée, pour qu'une version plus ancienne lise
/// le fichier d'une plus récente. Seuls des noms d'exécutables et des seuils y figurent, jamais de texte.
/// </summary>
public sealed class KeyStatsSettings
{
    public const int CurrentVersion = 1;

    /// <summary>Une instance neuve à chaque appel, comme <see cref="AnalysisSettings.Default"/>.</summary>
    public static KeyStatsSettings Default => new();

    public int Version { get; set; } = CurrentVersion;

    /// <summary>Remplace la liste par défaut quand elle est présente, vide comprise.</summary>
    public IReadOnlyList<string>? ExcludedApps { get; set; }

    public AnalysisSettings Analysis { get; set; } = new();

    public LiveCounterSettings LiveCounter { get; set; } = new();

    /// <summary>Dossier absolu du miroir ; nul : <c>backup\</c> à côté de la base.</summary>
    public string? BackupFolder { get; set; }

    public ExclusionList Exclusions() => ExcludedApps is null ? ExclusionList.Default : new ExclusionList(ExcludedApps);
}

public sealed class LiveCounterSettings
{
    public const int MinOpacity = 20, MaxOpacity = 100;

    public bool Enabled { get; set; } = true;

    /// <summary>Coin de la zone de travail : une valeur de <see cref="LiveCorner.All"/>.</summary>
    public string Position { get; set; } = LiveCorner.BottomRight;

    /// <summary>Opacité en pour cent, de <see cref="MinOpacity"/> à <see cref="MaxOpacity"/>.</summary>
    public int Opacity { get; set; } = MaxOpacity;

    internal bool IsValid() => LiveCorner.All.Contains(Position) && Opacity is >= MinOpacity and <= MaxOpacity;
}

/// <summary>Valeurs de <c>liveCounter.position</c>, telles qu'écrites dans <c>settings.json</c>.</summary>
public static class LiveCorner
{
    public const string TopLeft = "topLeft", TopRight = "topRight", BottomLeft = "bottomLeft", BottomRight = "bottomRight";

    public static readonly IReadOnlyList<string> All = [TopLeft, TopRight, BottomLeft, BottomRight];
}
