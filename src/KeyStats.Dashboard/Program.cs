using KeyStats.Core.Control;
using KeyStats.Core.Storage;
using KeyStats.Core.Web;

namespace KeyStats.Dashboard;

internal static class Program
{
    [STAThread]
    private static int Main(string[] args)
    {
        if (args is not ([] or ["--db", _]) || PrepareDb(args is [_, var given] ? given : DefaultDbPath) is not { } dbPath)
            return 2;
        string dataDir = Path.GetDirectoryName(dbPath)!;
        ApplicationConfiguration.Initialize();
        using var db = OpenDb(dbPath);
        if (db is null)
        {
            MessageBox.Show("La base KeyStats n'a pas pu être ouverte.", "KeyStats", MessageBoxButtons.OK, MessageBoxIcon.Error);
            return 2;
        }
        var router = new WebRouter(Path.Combine(AppContext.BaseDirectory, "wwwroot"));
        router.Get(StatusApi.Path, () => StatusApi.Respond(db, AgentRunning(dbPath)));
        using var form = new DashboardForm(router, Path.Combine(dataDir, "webview"));
        Application.Run(form);
        return form.Started ? 0 : 1;
    }

    private static string DefaultDbPath =>
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "KeyStats", "keystats.db");

    /// <summary>Chemin complet de la base, son dossier créé ; null si le chemin ne désigne pas un fichier utilisable.</summary>
    private static string? PrepareDb(string given)
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

    private static KeyStatsDb? OpenDb(string dbPath)
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

    private static bool AgentRunning(string dbPath)
    {
        if (!Mutex.TryOpenExisting(ControlProtocol.MutexName(dbPath), out var mutex)) return false;
        mutex.Dispose();
        return true;
    }
}
