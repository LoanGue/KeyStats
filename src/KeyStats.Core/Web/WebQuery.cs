namespace KeyStats.Core.Web;

/// <summary>
/// Paramètres d'une adresse (<c>?period=week&amp;app=3</c>). Un nom répété garde sa première valeur ;
/// au-delà de <see cref="MaxLength"/> caractères, la chaîne est ignorée en entier.
/// </summary>
public sealed class WebQuery
{
    public const int MaxLength = 1024;

    public static readonly WebQuery Empty = new([]);

    private readonly Dictionary<string, string> _values;

    private WebQuery(Dictionary<string, string> values) => _values = values;

    public static WebQuery Parse(string? query)
    {
        if (string.IsNullOrEmpty(query) || query.Length > MaxLength) return Empty;
        var values = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (string pair in query.TrimStart('?').Split('&', StringSplitOptions.RemoveEmptyEntries))
        {
            int eq = pair.IndexOf('=', StringComparison.Ordinal);
            string name = Decode(eq < 0 ? pair : pair[..eq]);
            string value = eq < 0 ? "" : Decode(pair[(eq + 1)..]);
            values.TryAdd(name, value);
        }
        return new WebQuery(values);
    }

    public string? this[string name] => _values.GetValueOrDefault(name);

    /// <summary>Entier positif du paramètre ; null s'il est absent ; false s'il est présent mais invalide.</summary>
    public bool TryGetId(string name, out int? id)
    {
        id = null;
        if (this[name] is not { Length: > 0 } text) return true;
        if (!int.TryParse(text, System.Globalization.NumberStyles.None, System.Globalization.CultureInfo.InvariantCulture, out int value) || value <= 0)
            return false;
        id = value;
        return true;
    }

    private static string Decode(string part) => Uri.UnescapeDataString(part.Replace('+', ' '));
}
