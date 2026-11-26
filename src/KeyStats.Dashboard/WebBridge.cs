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

    /// <summary>Gestionnaire de <c>WebResourceRequested</c> ; ne lève jamais d'exception.</summary>
    public async void OnRequest(object? sender, CoreWebView2WebResourceRequestedEventArgs e)
    {
        var deferral = e.GetDeferral();
        try
        {
            var request = e.Request;
            var response = await router.HandleAsync(request.Method, request.Uri, ReadBody(request.Content));
            e.Response = env.CreateWebResourceResponse(new MemoryStream(response.Body), response.Status,
                response.Status == 200 ? "OK" : "Error", $"Content-Type: {response.ContentType}\r\n{Headers}");
        }
        catch (Exception)
        {
            Fallback(e);
        }
        finally
        {
            deferral.Complete();
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

    /// <summary>
    /// Lit au plus <see cref="WebRouter.MaxBody"/> + 1 octets : un corps plus long est refusé par le routeur
    /// sans avoir été chargé en entier. Le flux COM de WebView2 refuse toute lecture à un décalage non nul
    /// (<see cref="NotImplementedException"/>) : chaque morceau est lu en début de <c>chunk</c>, puis recopié.
    /// </summary>
    private static byte[] ReadBody(Stream? content)
    {
        if (content is null) return [];
        byte[] buffer = new byte[WebRouter.MaxBody + 1];
        byte[] chunk = new byte[4096];
        int total = 0;
        while (total < buffer.Length
            && content.Read(chunk, 0, Math.Min(chunk.Length, buffer.Length - total)) is var read and > 0)
        {
            chunk.AsSpan(0, read).CopyTo(buffer.AsSpan(total));
            total += read;
        }
        byte[] body = buffer[..total];
        Array.Clear(buffer);
        Array.Clear(chunk);
        return body;
    }
}
