namespace KeyStats.Core.Text;

/// <summary>
/// Composition des touches mortes (^ ¨ ` ~ ´) avec la lettre suivante. Table écrite à la main :
/// l'agent tourne sans ICU, donc sans normalisation Unicode.
/// </summary>
public static class DeadKeys
{
    /// <summary>Caractère composé, ou '\0' si la combinaison n'existe pas (Windows émet alors les deux).</summary>
    public static char Compose(char dead, char c)
    {
        if (c == ' ') return dead;
        return dead switch
        {
            '^' => c switch
            {
                'a' => 'â',
                'e' => 'ê',
                'i' => 'î',
                'o' => 'ô',
                'u' => 'û',
                'y' => 'ŷ',
                'A' => 'Â',
                'E' => 'Ê',
                'I' => 'Î',
                'O' => 'Ô',
                'U' => 'Û',
                'Y' => 'Ŷ',
                _ => '\0',
            },
            '¨' => c switch
            {
                'a' => 'ä',
                'e' => 'ë',
                'i' => 'ï',
                'o' => 'ö',
                'u' => 'ü',
                'y' => 'ÿ',
                'A' => 'Ä',
                'E' => 'Ë',
                'I' => 'Ï',
                'O' => 'Ö',
                'U' => 'Ü',
                'Y' => 'Ÿ',
                _ => '\0',
            },
            '`' => c switch
            {
                'a' => 'à',
                'e' => 'è',
                'i' => 'ì',
                'o' => 'ò',
                'u' => 'ù',
                'A' => 'À',
                'E' => 'È',
                'I' => 'Ì',
                'O' => 'Ò',
                'U' => 'Ù',
                _ => '\0',
            },
            '~' => c switch
            {
                'a' => 'ã',
                'o' => 'õ',
                'n' => 'ñ',
                'A' => 'Ã',
                'O' => 'Õ',
                'N' => 'Ñ',
                _ => '\0',
            },
            '´' => c switch
            {
                'a' => 'á',
                'e' => 'é',
                'i' => 'í',
                'o' => 'ó',
                'u' => 'ú',
                'y' => 'ý',
                'A' => 'Á',
                'E' => 'É',
                'I' => 'Í',
                'O' => 'Ó',
                'U' => 'Ú',
                'Y' => 'Ý',
                _ => '\0',
            },
            _ => '\0',
        };
    }
}

/// <summary>
/// Codes Alt + pavé numérique. Avec un 0 en tête (Alt+0156), page Windows-1252 ;
/// sans 0 (Alt+144), page OEM 850, celle de Windows en français.
/// </summary>
public static class AltCodes
{
    // Windows-1252, 0x80 à 0x9F (le reste est identique à Latin-1). '\0' : non défini.
    private const string Cp1252High =
        "€\0‚ƒ„…†‡ˆ‰Š‹Œ\0Ž\0" +
        "\0‘’“”•–—˜™š›œ\0žŸ";

    // OEM 850, 0x80 à 0xFF.
    private const string Cp850High =
        "ÇüéâäàåçêëèïîìÄÅ" +
        "ÉæÆôöòûùÿÖÜø£Ø×ƒ" +
        "áíóúñÑªº¿®¬½¼¡«»" +
        "░▒▓│┤ÁÂÀ©╣║╗╝¢¥┐" +
        "└┴┬├─┼ãÃ╚╔╩╦╠═╬¤" +
        "ðÐÊËÈıÍÎÏ┘┌█▄¦Ì▀" +
        "ÓßÔÒõÕµþÞÚÛÙýÝ¯´" +
        "­±‗¾¶§÷¸°¨·¹³²■ ";

    // OEM (437/850), 0x01 à 0x1F, puis 0x7F : symboles graphiques.
    private const string CpOemLow = "☺☻♥♦♣♠•◘○◙♂♀♪♫☼►◄↕‼¶§▬↨↑↓→←∟↔▲▼";

    /// <summary>Caractère obtenu en relâchant Alt, ou '\0'. Windows ne garde que l'octet de poids faible.</summary>
    public static char Decode(int value, bool leadingZero)
    {
        int b = value & 0xFF;
        if (b == 0) return '\0';
        if (leadingZero)
        {
            if (b < 0x20 || b == 0x7F) return '\0';
            if (b is >= 0x80 and < 0xA0) return Cp1252High[b - 0x80];
            return (char)b;
        }
        if (b < 0x20) return CpOemLow[b - 1];
        if (b == 0x7F) return '⌂';
        if (b < 0x80) return (char)b;
        return Cp850High[b - 0x80];
    }
}
