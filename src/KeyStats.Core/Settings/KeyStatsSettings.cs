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
    public bool Enabled { get; set; } = true;
}
