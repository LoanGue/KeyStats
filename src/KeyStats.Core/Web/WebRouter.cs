using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization.Metadata;

namespace KeyStats.Core.Web;

/// <summary>Réponse rendue à WebView2 pour une requête interceptée.</summary>
public sealed record WebResponse(int Status, string ContentType, byte[] Body)
{
    private const string Text = "text/plain; charset=utf-8";

    public static WebResponse NotFound() => new(404, Text, Encoding.UTF8.GetBytes("Introuvable"));

    public static WebResponse MethodNotAllowed() => new(405, Text, Encoding.UTF8.GetBytes("Méthode refusée"));

    /// <summary>Message fixe : une exception peut porter du texte lu dans la base, il ne sort jamais.</summary>
    public static WebResponse Error() => new(500, Text, Encoding.UTF8.GetBytes("Erreur interne"));

    public static WebResponse Json<T>(T value, JsonTypeInfo<T> type) =>
        new(200, "application/json; charset=utf-8", JsonSerializer.SerializeToUtf8Bytes(value, type));
}

/// <summary>
/// Tout ce que la page demande à <c>https://keystats.app/</c> : les routes <c>/api/…</c> d'abord, puis
/// les fichiers de <c>wwwroot</c>. Aucune autre origine n'est servie, aucun port n'est ouvert.
/// </summary>
public sealed class WebRouter
{
    public const string Origin = "https://keystats.app";

    private readonly string _root;
    private readonly Dictionary<string, Func<WebResponse>> _get = new(StringComparer.Ordinal);

    public WebRouter(string root) => _root = Path.GetFullPath(root);

    public void Get(string path, Func<WebResponse> handler) => _get[path] = handler;

    /// <summary>
    /// Toujours une réponse, jamais d'exception : une requête sans réponse partirait vers le vrai
    /// domaine <see cref="Origin"/> sur le réseau.
    /// </summary>
    public WebResponse Handle(string method, string uri)
    {
        try
        {
            return LocalPath(uri) is { } path ? Route(method, path) : WebResponse.NotFound();
        }
        catch (Exception)
        {
            return WebResponse.Error();
        }
    }

    /// <summary>Adresse servie par ce routeur (même schéma, hôte et port que <see cref="Origin"/>).</summary>
    public static bool IsOwn(string uri) => LocalPath(uri) is not null;

    private WebResponse Route(string method, string path)
    {
        bool get = string.Equals(method, "GET", StringComparison.OrdinalIgnoreCase);
        if (!path.StartsWith("/api/", StringComparison.Ordinal))
            return get ? StaticFiles.Serve(_root, path) : WebResponse.MethodNotAllowed();
        if (!_get.TryGetValue(path, out var handler)) return WebResponse.NotFound();
        return get ? handler() : WebResponse.MethodNotAllowed();
    }

    private static string? LocalPath(string uri) =>
        Uri.TryCreate(uri, UriKind.Absolute, out var parsed)
        && string.Equals(parsed.GetLeftPart(UriPartial.Authority), Origin, StringComparison.OrdinalIgnoreCase)
            ? Uri.UnescapeDataString(parsed.AbsolutePath)
            : null;
}