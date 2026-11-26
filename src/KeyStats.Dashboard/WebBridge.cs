using KeyStats.Core.Web;
using Microsoft.Web.WebView2.Core;

namespace KeyStats.Dashboard;

/// <summary>
/// Répond aux requêtes interceptées par WebView2 avec <see cref="WebRouter"/>, routes POST comprises.
/// Chaque requête reçoit une réponse : sans elle, WebView2 l'enverrait sur le réseau.
/// </summary>
internal sealed class WebBridge(WebRouter router, CoreWebView2Environment env)
{
    // Rien de la page ne reste dans le cache disque de WebView2 ; seuls ces fichiers et l'API sont permis.
    private const string Headers =
        "Cache-Control: no-store\r\n"
        + "Content-Security-Policy: default-src 'self'; img-src 'self' data:; style-src 'self' 'unsafe-inline'; object-src 'none'; base-uri 'none'; frame-ancestors 'none'\r\n"
        + "X-Content-Type-Options: nosniff";

    /// <summary>
    /// Gestionnaire de <c>WebResourceRequested</c> ; ne lève jamais d'exception, ce qui compte dans un
    /// <c>async void</c> : elle remonterait jusqu'à la boîte d'erreur de WinForms.
    /// </summary>
    public async void OnRequest(object? sender, CoreWebView2WebResourceRequestedEventArgs e)
    {
        CoreWebView2Deferral? deferral = null;
        try
        {
            deferral = e.GetDeferral();
            var request = e.Request;
            var response = await router.HandleAsync(request.Method, request.Uri, RequestBody.Read(request.Content));
            e.Response = env.CreateWebResourceResponse(new MemoryStream(response.Body), response.Status,
                response.Status == 200 ? "OK" : "Error", $"Content-Type: {response.ContentType}\r\n{Headers}");
        }
        catch (Exception)
        {
            Fallback(e);
        }
        finally
        {
            Complete(deferral);
        }
    }

    private void Fallback(CoreWebView2WebResourceRequestedEventArgs e)
    {
        try
        {
            e.Response = env.CreateWebResourceResponse(null, 500, "Error", Headers);
        }
        catch (Exception)
        {
            // Environnement WebView2 lui-même hors service : plus rien à répondre, limite acceptée.
        }
    }

    private static void Complete(CoreWebView2Deferral? deferral)
    {
        try
        {
            deferral?.Complete();
        }
        catch (Exception)
        {
            // WebView2 détruite pendant l'attente (fenêtre fermée pendant l'invite Hello) : plus personne à qui répondre.
        }
    }
}
