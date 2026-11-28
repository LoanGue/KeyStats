using KeyStats.Core.Crypto;
using KeyStats.Core.Settings;
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
        var agent = new AgentControl(dbPath);
        router.Get(StatusApi.Path, () => StatusApi.Respond(db, agent.IsRunning));
        SettingsApi.Register(router, SettingsStore.PathFor(dbPath), agent);
        var folderPicker = new FolderPicker();
        using var session = new KeySession();
        var helloSigner = new HelloSigner();
        KeyApi.Register(router, db, session, helloSigner);
        var stats = new StatsContext(db, () => DateTimeOffset.UtcNow.ToUnixTimeMilliseconds());
        KeyChangeApi.Register(router, db, session, helloSigner, new HelloEnroller(), () => stats.NowMs);
        FiltersApi.Register(router, stats);
        OverviewApi.Register(router, stats);
        SpeedApi.Register(router, stats);
        TyposApi.Register(router, stats);
        KeyboardApi.Register(router, stats);
        LettersApi.Register(router, stats);
        RhythmApi.Register(router, stats);
        ActivityApi.Register(router, stats, session);
        ShortcutsApi.Register(router, stats);
        BackupApi.Register(router, dbPath, folderPicker, agent, () => stats.NowMs);
        ExportApi.Register(router, stats, dbPath, folderPicker, Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments));
        using var form = new DashboardForm(router, Path.Combine(dataDir, "webview"));
        folderPicker.Owner = form;
        // La clé privée ne survit pas à la fenêtre.
        form.FormClosed += (_, _) => session.Lock();
        Application.Run(form);
        return form.Started ? 0 : 1;
    }
}
