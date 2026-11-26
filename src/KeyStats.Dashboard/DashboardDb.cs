using KeyStats.Core.Storage;

namespace KeyStats.Dashboard;

/// <summary>Emplacement et ouverture de la base au démarrage du tableau de bord.</summary>
internal static class DashboardDb
{
    public static string DefaultPath =>
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "KeyStats", "keystats.db");

    /// <summary>Chemin complet de la base, son dossier créé ; null si le chemin ne désigne pas un fichier utilisable.</summary>
    public static string? Prepare(string given)
    {
        if (string.IsNullOrWhiteSpace(given)) return null;
        try
        {
            string full = Path.GetFullPath(given);
            // Une racine de lecteur ou un chemin finissant par un séparateur ne désigne pas un fichier.
            if (Path.GetDirectoryName(full) is not { Length: > 0 } dir || Path.GetFileName(full).Length == 0) return null;
            Directory.CreateDirectory(dir);
            return full;
        }
        catch (Exception e) when (e is ArgumentException or IOException or NotSupportedException or UnauthorizedAccessException)
        {
            return null;
        }
    }

    public static KeyStatsDb? Open(string dbPath)
    {
        try
        {
            return KeyStatsDb.Open(dbPath);
        }
        catch (Exception)
        {
            // Nom de fichier refusé par Windows, base illisible ou d'un schéma plus récent.
            return null;
        }
    }
}
