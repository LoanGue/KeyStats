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
    // Rien de la page ne reste dans le cache disque de WebView2 ; seuls ces fichiers et l'API sont permis.
    private const string Headers =
        "Cache-Control: no-store\r\n"
        + "Content-Security-Policy: default-src 'self'; img-src 'self' data:; style-src 'self' 'unsafe-inline'; object-src 'none'; base-uri 'none'; frame-ancestors 'none'\r\n"
        + "X-Content-Type-Options: nosniff";

    private readonly WebView2 _view = new() { Dock = DockStyle.Fill };
    private readonly WebRouter _router;
    private readonly string _profile;

    public DashboardForm(WebRouter router, string profileFolder)
    {
        _router = router;
        _profile = profileFolder;
        Text = "KeyStats";
        ClientSize = new Size(1280, 860);
        StartPosition = FormStartPosition.CenterScreen;
        Controls.Add(_view);
        Load += async (_, _) => await StartAsync();
    }

    private async Task StartAsync()
    {
        var env = await CoreWebView2Environment.CreateAsync(null, _profile);
        await _view.EnsureCoreWebView2Async(env);
        var core = _view.CoreWebView2;
        core.Settings.AreDevToolsEnabled = Debugger.IsAttached;
        core.Settings.IsStatusBarEnabled = false;
        core.Settings.IsPasswordAutosaveEnabled = false;
        core.Settings.IsGeneralAutofillEnabled = false;
        core.AddWebResourceRequestedFilter(WebRouter.Origin + "/*", CoreWebView2WebResourceContext.All);
        core.WebResourceRequested += (_, e) => e.Response = Respond(env, e.Request);
        core.NavigationStarting += (_, e) => e.Cancel = !IsOwn(e.Uri);
        core.NewWindowRequested += (_, e) => e.Handled = true;
        core.Navigate(WebRouter.Origin + "/");
    }

    private CoreWebView2WebResourceResponse Respond(CoreWebView2Environment env, CoreWebView2WebResourceRequest request)
    {
        var response = _router.Handle(request.Method, request.Uri);
        return env.CreateWebResourceResponse(new MemoryStream(response.Body), response.Status,
            response.Status == 200 ? "OK" : "Error", $"Content-Type: {response.ContentType}\r\n{Headers}");
    }

    private static bool IsOwn(string uri) =>
        uri.StartsWith(WebRouter.Origin + "/", StringComparison.OrdinalIgnoreCase);
}
