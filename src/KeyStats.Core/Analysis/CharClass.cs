namespace KeyStats.Core.Analysis;

/// <summary>Classement des caractères pour l'analyse du texte.</summary>
public static class CharClass
{
    /// <summary>Lettre d'un mot. Apostrophes et traits d'union séparent les mots (« l'homme » : 1 + 5).</summary>
    public static bool IsWordLetter(char c) => char.IsLetter(c);

    public static bool IsWhitespace(char c) => c is ' ' or '\n' or '\t' or ' ' or ' ';

    /// <summary>
    /// Symbole typique du code. Les parenthèses, la ponctuation française courante et les
    /// guillemets n'en font pas partie : ils sont fréquents en prose.
    /// </summary>
    public static bool IsCodeSymbol(char c) =>
        c is '{' or '}' or '[' or ']' or '<' or '>' or '=' or '_' or '\\' or '|' or '&' or '*'
            or '#' or '$' or '%' or '^' or '~' or '`' or '@' or '+' or '/' or ';';

    /// <summary>Caractère qui fait partie d'un mot pour Ctrl+Retour arrière.</summary>
    internal static bool IsWordChar(char c) => char.IsLetterOrDigit(c) || c == '_';
}
