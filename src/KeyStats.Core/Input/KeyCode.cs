using System.Globalization;

namespace KeyStats.Core.Input;

/// <summary>
/// Identifiant d'une touche physique, indépendant de la disposition : scan code (jeu 1),
/// + 0x100 si préfixe E0, + 0x200 si préfixe E1. Une touche sans scan code (certaines touches
/// multimédia ou Fn de portable) est codée 0x400 | code virtuel Windows.
/// </summary>
public static class KeyCode
{
    public const ushort E0 = 0x100, E1 = 0x200, VirtualKey = 0x400;

    /// <summary>Taille des tableaux indexés par code de touche.</summary>
    public const int Count = 0x500;

    /// <summary>Codes inférieurs à cette borne : touches à scan code, qui peuvent produire un caractère.</summary>
    public const int ScanCodeLimit = 0x200;

    public const ushort
        Escape = 0x01, Backspace = 0x0E, Tab = 0x0F, Enter = 0x1C, LeftCtrl = 0x1D,
        LeftShift = 0x2A, RightShift = 0x36, NumMultiply = 0x37, LeftAlt = 0x38, Space = 0x39, CapsLock = 0x3A,
        F1 = 0x3B, F10 = 0x44, NumLock = 0x45, ScrollLock = 0x46,
        Num7 = 0x47, Num8 = 0x48, Num9 = 0x49, NumMinus = 0x4A, Num4 = 0x4B, Num5 = 0x4C, Num6 = 0x4D,
        NumPlus = 0x4E, Num1 = 0x4F, Num2 = 0x50, Num3 = 0x51, Num0 = 0x52, NumDecimal = 0x53,
        Iso102 = 0x56, F11 = 0x57, F12 = 0x58,
        NumEnter = 0x11C, RightCtrl = 0x11D, NumDivide = 0x135, PrintScreen = 0x137, RightAlt = 0x138,
        Home = 0x147, Up = 0x148, PageUp = 0x149, Left = 0x14B, Right = 0x14D, End = 0x14F,
        Down = 0x150, PageDown = 0x151, Insert = 0x152, Delete = 0x153,
        LeftWin = 0x15B, RightWin = 0x15C, Apps = 0x15D, Pause = 0x21D;

    /// <summary>
    /// Code d'une touche reçue par Raw Input. Renvoie 0 pour une touche inexploitable.
    /// </summary>
    public static ushort FromScanCode(int makeCode, bool e0, bool e1, int virtualKey)
    {
        if (makeCode is <= 0 or >= 0x80)
            return virtualKey is > 0 and < 0xFF ? (ushort)(VirtualKey | virtualKey) : (ushort)0;
        // Verr Num arrive selon les claviers avec ou sans E0 : une seule touche physique.
        if (makeCode == 0x45 && !e1) return NumLock;
        return (ushort)(makeCode | (e1 ? E1 : e0 ? E0 : 0));
    }

    public static bool IsModifier(ushort k) =>
        k is LeftCtrl or RightCtrl or LeftShift or RightShift or LeftAlt or RightAlt or LeftWin or RightWin;

    public static bool IsLock(ushort k) => k is CapsLock or NumLock or ScrollLock;

    /// <summary>Touches du pavé qui servent de chiffre (Verr Num actif) ou de déplacement.</summary>
    public static bool IsNumpadDual(ushort k) => k is >= Num7 and <= NumDecimal and not NumMinus and not NumPlus;

    /// <summary>Chiffre porté par une touche du pavé (0 à 9), ou -1.</summary>
    public static int NumpadDigit(ushort k) => k switch
    {
        Num0 => 0,
        Num1 => 1,
        Num2 => 2,
        Num3 => 3,
        Num4 => 4,
        Num5 => 5,
        Num6 => 6,
        Num7 => 7,
        Num8 => 8,
        Num9 => 9,
        _ => -1,
    };

    public static bool IsNumpad(ushort k) =>
        k is (>= Num7 and <= NumDecimal) or NumMultiply or NumEnter or NumDivide or NumLock;

    /// <summary>Déplacement du curseur (bloc de navigation).</summary>
    public static bool IsNavigation(ushort k) =>
        k is Home or Up or PageUp or Left or Right or End or Down or PageDown or Insert;

    public static bool IsFunctionKey(ushort k) => k is (>= F1 and <= F10) or F11 or F12 or (>= 0x64 and <= 0x6E) or 0x76;

    /// <summary>
    /// Touches sans effet sur le texte ni sur le curseur : multimédia, volume, Impr écran, Pause…
    /// </summary>
    public static bool IsNeutral(ushort k) =>
        k >= VirtualKey || k is PrintScreen or Pause or ScrollLock
        || (k is >= 0x101 and < 0x180 && !IsKnownExtended(k));

    private static bool IsKnownExtended(ushort k) =>
        k is NumEnter or RightCtrl or NumDivide or RightAlt or Home or Up or PageUp or Left or Right
            or End or Down or PageDown or Insert or Delete or LeftWin or RightWin or Apps;

    /// <summary>Nom affiché de la touche, avec les inscriptions d'un clavier AZERTY français.</summary>
    public static string Name(ushort k) => k switch
    {
        Escape => "Échap",
        0x02 => "1",
        0x03 => "2",
        0x04 => "3",
        0x05 => "4",
        0x06 => "5",
        0x07 => "6",
        0x08 => "7",
        0x09 => "8",
        0x0A => "9",
        0x0B => "0",
        0x0C => ")",
        0x0D => "=",
        Backspace => "Retour arrière",
        Tab => "Tab",
        0x10 => "A",
        0x11 => "Z",
        0x12 => "E",
        0x13 => "R",
        0x14 => "T",
        0x15 => "Y",
        0x16 => "U",
        0x17 => "I",
        0x18 => "O",
        0x19 => "P",
        0x1A => "^",
        0x1B => "$",
        Enter => "Entrée",
        LeftCtrl => "Ctrl gauche",
        0x1E => "Q",
        0x1F => "S",
        0x20 => "D",
        0x21 => "F",
        0x22 => "G",
        0x23 => "H",
        0x24 => "J",
        0x25 => "K",
        0x26 => "L",
        0x27 => "M",
        0x28 => "ù",
        0x29 => "²",
        LeftShift => "Maj gauche",
        0x2B => "*",
        0x2C => "W",
        0x2D => "X",
        0x2E => "C",
        0x2F => "V",
        0x30 => "B",
        0x31 => "N",
        0x32 => ",",
        0x33 => ";",
        0x34 => ":",
        0x35 => "!",
        RightShift => "Maj droite",
        NumMultiply => "Pavé *",
        LeftAlt => "Alt",
        Space => "Espace",
        CapsLock => "Verr Maj",
        0x3B => "F1",
        0x3C => "F2",
        0x3D => "F3",
        0x3E => "F4",
        0x3F => "F5",
        0x40 => "F6",
        0x41 => "F7",
        0x42 => "F8",
        0x43 => "F9",
        F10 => "F10",
        NumLock => "Verr Num",
        ScrollLock => "Arrêt défil",
        Num7 => "Pavé 7",
        Num8 => "Pavé 8",
        Num9 => "Pavé 9",
        NumMinus => "Pavé -",
        Num4 => "Pavé 4",
        Num5 => "Pavé 5",
        Num6 => "Pavé 6",
        NumPlus => "Pavé +",
        Num1 => "Pavé 1",
        Num2 => "Pavé 2",
        Num3 => "Pavé 3",
        Num0 => "Pavé 0",
        NumDecimal => "Pavé .",
        Iso102 => "<",
        F11 => "F11",
        F12 => "F12",
        0x64 => "F13",
        0x65 => "F14",
        0x66 => "F15",
        0x67 => "F16",
        0x68 => "F17",
        0x69 => "F18",
        0x6A => "F19",
        0x6B => "F20",
        0x6C => "F21",
        0x6D => "F22",
        0x6E => "F23",
        0x76 => "F24",
        NumEnter => "Pavé Entrée",
        RightCtrl => "Ctrl droit",
        NumDivide => "Pavé /",
        PrintScreen => "Impr écran",
        RightAlt => "AltGr",
        Home => "Origine",
        Up => "Haut",
        PageUp => "Page préc",
        Left => "Gauche",
        Right => "Droite",
        End => "Fin",
        Down => "Bas",
        PageDown => "Page suiv",
        Insert => "Inser",
        Delete => "Suppr",
        LeftWin => "Win gauche",
        RightWin => "Win droite",
        Apps => "Menu",
        Pause => "Pause",
        0x110 => "Piste précédente",
        0x119 => "Piste suivante",
        0x120 => "Muet",
        0x122 => "Lecture",
        0x124 => "Arrêt",
        0x12E => "Volume -",
        0x130 => "Volume +",
        _ when k >= VirtualKey => "VK " + (k & 0xFF).ToString("X2", CultureInfo.InvariantCulture),
        _ => "Touche " + k.ToString("X3", CultureInfo.InvariantCulture),
    };

    /// <summary>Nom court pour les raccourcis (« Ctrl » plutôt que « Ctrl gauche »).</summary>
    public static string ShortName(ushort k) => k switch
    {
        LeftCtrl or RightCtrl => "Ctrl",
        LeftShift or RightShift => "Maj",
        LeftWin or RightWin => "Win",
        _ => Name(k),
    };
}
