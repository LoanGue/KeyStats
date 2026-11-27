using KeyStats.Core.Control;
using KeyStats.Core.Crypto;
using KeyStats.Core.Stats;
using KeyStats.Core.Storage;
using KeyStats.Core.Web;

namespace KeyStats.Dashboard;

internal static class Program
{
    [STAThread]
    private static int Main(string[] args)
    {
        if (args is not ([] or ["--db", _]) || DashboardDb.Prepare(args is [_, var given] ? given : DashboardDb.DefaultPath) is not { } dbPath)
            return 2;
        string dataDir = Path.GetDirectoryName(dbPath)!;
        ApplicationConfiguration.Initialize();
        using var db = DashboardDb.Open(dbPath);
        if (db is null)
        {
            MessageBox.Show("La base KeyStats n'a pas pu être ouverte.", "KeyStats", MessageBoxButtons.OK, MessageBoxIcon.Error);
            return 2;
        }
        var router = new WebRouter(Path.Combine(AppContext.BaseDirectory, "wwwroot"));
        router.Get(StatusApi.Path, () => StatusApi.Respond(db, AgentRunning(dbPath)));
        using var session = new KeySession();
        KeyApi.Register(router, db, session, new HelloSigner());
        var stats = new StatsContext(db, () => DateTimeOffset.UtcNow.ToUnixTimeMilliseconds());
        FiltersApi.Register(router, stats);
        OverviewApi.Register(router, stats);
        SpeedApi.Register(router, stats);
        TyposApi.Register(router, stats);
        KeyboardApi.Register(router, stats);
        LettersApi.Register(router, stats);
        using var form = new DashboardForm(router, Path.Combine(dataDir, "webview"));
        // La clé privée ne survit pas à la fenêtre.
        form.FormClosed += (_, _) => session.Lock();
        Application.Run(form);
        return form.Started ? 0 : 1;
    }

    private static bool AgentRunning(string dbPath)
    {
        if (!Mutex.TryOpenExisting(ControlProtocol.MutexName(dbPath), out var mutex)) return false;
        mutex.Dispose();
        return true;
    }
}
