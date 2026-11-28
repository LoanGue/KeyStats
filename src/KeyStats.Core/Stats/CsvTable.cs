using System.Globalization;
using System.Text;

namespace KeyStats.Core.Stats;

/// <summary>
/// Fichier CSV tel que l'ouvre un tableur réglé en français : séparateur <c>;</c>, virgule décimale, UTF-8 avec
/// BOM, fins de ligne CRLF. Un champ qui contient <c>;</c>, un guillemet ou un saut de ligne est mis entre
/// guillemets ; un texte qui commence comme une formule (<c>= + - @</c>) est précédé d'une apostrophe.
/// </summary>
public sealed class CsvTable
{
    private readonly StringBuilder _text = new();
    private readonly int _columns;
    private int _cell;

    public CsvTable(params string[] headers)
    {
        ArgumentNullException.ThrowIfNull(headers);
        _columns = headers.Length;
        foreach (string h in headers) Text(h);
    }

    public CsvTable Text(string? value)
    {
        string v = value ?? "";
        if (v.Length > 0 && v[0] is '=' or '+' or '-' or '@') v = "'" + v;
        if (v.AsSpan().IndexOfAny(";\"\r\n") >= 0) v = "\"" + v.Replace("\"", "\"\"", StringComparison.Ordinal) + "\"";
        return Cell(v);
    }

    public CsvTable Number(long value) => Cell(value.ToString(CultureInfo.InvariantCulture));

    /// <summary>Nombre arrondi à <paramref name="decimals"/> chiffres, virgule décimale ; vide pour null.</summary>
    public CsvTable Number(double? value, int decimals = 1) =>
        // Format invariant, puis virgule : le résultat ne dépend pas des cultures installées.
        Cell(value is { } v ? v.ToString("0." + new string('#', decimals), CultureInfo.InvariantCulture).Replace('.', ',') : "");

    /// <summary>Le fichier, BOM compris.</summary>
    public byte[] ToBytes()
    {
        if (_cell != 0) throw new InvalidOperationException("Ligne incomplète.");
        return [.. Encoding.UTF8.GetPreamble(), .. Encoding.UTF8.GetBytes(_text.ToString())];
    }

    private CsvTable Cell(string v)
    {
        if (_cell > 0) _text.Append(';');
        _text.Append(v);
        if (++_cell == _columns)
        {
            _text.Append("\r\n");
            _cell = 0;
        }
        return this;
    }
}
