using Microsoft.Web.WebView2.Core;

namespace KeyStats.Dashboard;

/// <summary>
/// Rapports de plantage de la WebView2 : un vidage mémoire du moteur de rendu peut contenir la phrase
/// de secours ou du texte déchiffré. Rien n'est envoyé à Microsoft (rapports personnalisés), et le dossier
/// où Crashpad les écrit est vidé au démarrage, après chaque plantage d'un processus et à la fermeture.
/// </summary>
internal static class CrashReports
{
    public static CoreWebView2EnvironmentOptions Options() => new() { IsCustomCrashReportingEnabled = true };

    public static void Watch(CoreWebView2Environment env, CoreWebView2 core)
    {
        string folder = env.FailureReportFolderPath;
        Purge(folder);
        // Le vidage s'écrit encore quand l'événement part (fichier vide à 1 s) : on repasse plus tard.
        core.ProcessFailed += async (_, _) =>
        {
            foreach (int seconds in (int[])[0, 5, 30])
            {
                await Task.Delay(TimeSpan.FromSeconds(seconds));
                Purge(folder);
            }
        };
    }

    /// <summary>Supprime tout le contenu de <paramref name="folder"/> ; un fichier encore ouvert est laissé au passage suivant.</summary>
    public static void Purge(string? folder)
    {
        if (string.IsNullOrEmpty(folder) || !Directory.Exists(folder)) return;
        foreach (string path in Directory.EnumerateFileSystemEntries(folder))
        {
            try
            {
                if (Directory.Exists(path)) Directory.Delete(path, recursive: true);
                else File.Delete(path);
            }
            catch (Exception e) when (e is IOException or UnauthorizedAccessException)
            {
                // Verrouillé par Crashpad : repris au prochain passage.
            }
        }
    }
}
