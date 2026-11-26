using System.Diagnostics;
using KeyStats.Core.Web;
using Microsoft.Web.WebView2.Core;
using Microsoft.Web.WebView2.WinForms;

namespace KeyStats.Dashboard;

/// <summary>
/// Fenêtre du tableau de bord : une WebView2 dont chaque requête vers <see cref="WebRouter.Origin"/>
/// est servie en mémoire par <see cref="WebRouter"/>. Aucune autre navigation n'est permise.
/// </summary>
internal sealed class DashboardForm : Form
{
    private readonly WebView2 _view = new() { Dock = DockStyle.Fill };
    private readonly WebRouter _router;
    private readonly string _profile;

    /// <summary>Vrai une fois la WebView2 prête ; faux si le démarrage a échoué.</summary>
    public bool Started { get; private set; }

    public DashboardForm(WebRouter router, string profileFolder)
    {
        _router = router;
        _profile = profileFolder;
        Text = "KeyStats";
        ClientSize = new Size(1280, 860);
        StartPosition = FormStartPosition.CenterScreen;
        Controls.Add(_view);
        Load += async (_, _) => await StartOrCloseAsync();
    }

    private async Task StartOrCloseAsync()
    {
        try
        {
            await StartAsync();
        }
        catch (WebView2RuntimeNotFoundException)
        {
            Fail("Le moteur WebView2 de Microsoft Edge est introuvable : installez-le puis rouvrez KeyStats.");
        }
        catch (Exception)
        {
            // Aucun texte d'exception affiché : il pourrait venir de la page ou de la base.
            Fail("Le tableau de bord n'a pas pu démarrer. Rouvrez KeyStats.");
        }
    }

    // Fenêtre déjà fermée pendant le démarrage : personne à prévenir, et un message sur elle lèverait.
    private void Fail(string message)
    {
        if (IsDisposed) return;
        MessageBox.Show(this, message, "KeyStats", MessageBoxButtons.OK, MessageBoxIcon.Error);
        Close();
    }

    private async Task StartAsync()
    {
        var env = await CoreWebView2Environment.CreateAsync(null, _profile, CrashReports.Options());
        await _view.EnsureCoreWebView2Async(env);
        var core = _view.CoreWebView2;
        CrashReports.Watch(env, core);
        FormClosed += (_, _) => CrashReports.Purge(env.FailureReportFolderPath);
        core.Settings.AreDevToolsEnabled = Debugger.IsAttached;
        core.Settings.IsStatusBarEnabled = false;
        core.Settings.IsPasswordAutosaveEnabled = false;
        core.Settings.IsGeneralAutofillEnabled = false;
        core.Settings.IsReputationCheckingRequired = false;
        core.AddWebResourceRequestedFilter(WebRouter.Origin + "/*", CoreWebView2WebResourceContext.All);
        core.WebResourceRequested += new WebBridge(_router, env).OnRequest;
        core.NavigationStarting += (_, e) => e.Cancel = !WebRouter.IsOwn(e.Uri);
        core.NewWindowRequested += (_, e) => e.Handled = true;
        core.Navigate(WebRouter.Origin + "/");
        Started = true;
    }
}
