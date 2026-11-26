using KeyStats.Core.Control;
using KeyStats.Core.Storage;
using KeyStats.Core.Web;

namespace KeyStats.Dashboard;

internal static class Program
{
    [STAThread]
    private static int Main(string[] args)
    {
        if (args is not ([] or ["--db", _])) return 2;
        string dbPath = args is ["--db", var given] ? Path.GetFullPath(given) : DefaultDbPath;
        string dataDir = Path.GetDirectoryName(dbPath)!;
        Directory.CreateDirectory(dataDir);
        ApplicationConfiguration.Initialize();
        using var db = KeyStatsDb.Open(dbPath);
        var router = new WebRouter(Path.Combine(AppContext.BaseDirectory, "wwwroot"));
        router.Get(StatusApi.Path, () => StatusApi.Respond(db, AgentRunning(dbPath)));
        using var form = new DashboardForm(router, Path.Combine(dataDir, "webview"));
        Application.Run(form);
        return 0;
    }

    private static string DefaultDbPath =>
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "KeyStats", "keystats.db");

    private static bool AgentRunning(string dbPath)
    {
        if (!Mutex.TryOpenExisting(ControlProtocol.MutexName(dbPath), out var mutex)) return false;
        mutex.Dispose();
        return true;
    }
}
