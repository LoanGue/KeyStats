namespace KeyStats.Core.Web;

/// <summary>Fichiers de l'interface, lus sous un seul dossier, avec une liste fermée de types.</summary>
public static class StaticFiles
{
    private static readonly Dictionary<string, string> Types = new(StringComparer.OrdinalIgnoreCase)
    {
        [".html"] = "text/html; charset=utf-8",
        [".css"] = "text/css; charset=utf-8",
        [".js"] = "text/javascript; charset=utf-8",
        [".json"] = "application/json; charset=utf-8",
        [".svg"] = "image/svg+xml",
        [".png"] = "image/png",
        [".ico"] = "image/x-icon",
        [".woff2"] = "font/woff2",
    };

    public static WebResponse Serve(string root, string path) =>
        Resolve(root, path) is { } file && Types.TryGetValue(Path.GetExtension(file), out string? type)
            ? new WebResponse(200, type, File.ReadAllBytes(file))
            : WebResponse.NotFound();

    /// <summary>
    /// Chemin du fichier sous <paramref name="root"/>, ou null : segment <c>..</c>, barre oblique inverse,
    /// lecteur, ou fichier absent. <c>/</c> donne <c>index.html</c>.
    /// </summary>
    public static string? Resolve(string root, string path)
    {
        if (path == "/") path = "/index.html";
        if (!path.StartsWith('/') || path.Contains('\\') || path.Contains(':') || path.Contains('\0')) return null;
        string[] segments = path[1..].Split('/');
        if (segments.Any(s => s.Length == 0 || s == "." || s == "..")) return null;
        string full = Path.GetFullPath(Path.Combine([root, .. segments]));
        string prefix = Path.TrimEndingDirectorySeparator(Path.GetFullPath(root)) + Path.DirectorySeparatorChar;
        return full.StartsWith(prefix, StringComparison.OrdinalIgnoreCase) && File.Exists(full) ? full : null;
    }
}
