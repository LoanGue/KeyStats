using KeyStats.Core.Input;

namespace KeyStats.Core.Text;

/// <summary>
/// Disposition « Français (France) » de Windows (KBDFR, KLID 0000040C), recopiée à la main.
/// Sert de repli si Windows ne répond pas, et de référence aux tests. L'agent utilise en
/// priorité la table construite depuis Windows pour la disposition active.
/// </summary>
internal static class FrenchAzertyLayout
{
    public static LayoutTable Create()
    {
        var t = new LayoutTable("0000040C", hasAltGr: true);

        // Touche, normal, Maj, AltGr, Verr Maj agit comme Maj. « !x » : touche morte x.
        // Sous Windows, Verr Maj sur la rangée des chiffres donne les chiffres (Verr Maj + é = 2).
        Key(t, 0x29, "²", "", "", false);
        Key(t, 0x02, "&", "1", "", true);
        Key(t, 0x03, "é", "2", "!~", true);
        Key(t, 0x04, "\"", "3", "#", true);
        Key(t, 0x05, "'", "4", "{", true);
        Key(t, 0x06, "(", "5", "[", true);
        Key(t, 0x07, "-", "6", "|", true);
        Key(t, 0x08, "è", "7", "!`", true);
        Key(t, 0x09, "_", "8", "\\", true);
        Key(t, 0x0A, "ç", "9", "^", true);
        Key(t, 0x0B, "à", "0", "@", true);
        Key(t, 0x0C, ")", "°", "]", false);
        Key(t, 0x0D, "=", "+", "}", false);

        Letters(t, 0x10, "azertyuiop");
        Set(t, 0x12, ShiftState.AltGr, "€");
        Set(t, 0x12, ShiftState.AltGr | ShiftState.Caps, "€");
        Key(t, 0x1A, "!^", "!¨", "", false);
        Key(t, 0x1B, "$", "£", "¤", false);

        Letters(t, 0x1E, "qsdfghjklm");
        Key(t, 0x28, "ù", "%", "", false);
        Key(t, 0x2B, "*", "µ", "", false);

        Key(t, KeyCode.Iso102, "<", ">", "", false);
        Letters(t, 0x2C, "wxcvbn");
        Key(t, 0x32, ",", "?", "", false);
        Key(t, 0x33, ";", ".", "", false);
        Key(t, 0x34, ":", "/", "", false);
        Key(t, 0x35, "!", "§", "", false);

        Key(t, KeyCode.Space, " ", " ", " ", false);

        // Pavé numérique : les opérateurs dans toutes les cases, les chiffres avec Verr Num.
        foreach (var (key, c) in new (ushort, char)[]
                 { (KeyCode.NumMultiply, '*'), (KeyCode.NumMinus, '-'), (KeyCode.NumPlus, '+'), (KeyCode.NumDivide, '/') })
        {
            for (int slot = 0; slot < LayoutTable.SlotCount; slot++) t.Set(key, slot, c);
        }
        foreach (var (key, c) in new (ushort, char)[]
                 {
                     (KeyCode.Num0, '0'), (KeyCode.Num1, '1'), (KeyCode.Num2, '2'), (KeyCode.Num3, '3'), (KeyCode.Num4, '4'),
                     (KeyCode.Num5, '5'), (KeyCode.Num6, '6'), (KeyCode.Num7, '7'), (KeyCode.Num8, '8'), (KeyCode.Num9, '9'),
                     (KeyCode.NumDecimal, '.'),
                 })
        {
            t.Set(key, LayoutTable.NumLockSlot, c);
        }
        return t;
    }

    private static void Letters(LayoutTable t, ushort first, string letters)
    {
        for (int i = 0; i < letters.Length; i++)
        {
            string lower = letters[i].ToString();
            Key(t, (ushort)(first + i), lower, lower.ToUpperInvariant(), "", true);
        }
    }

    private static void Key(LayoutTable t, ushort key, string normal, string shift, string altGr, bool capsLikeShift)
    {
        for (int slot = 0; slot < 8; slot++)
        {
            var s = (ShiftState)slot;
            bool sh = (s & ShiftState.Shift) != 0;
            bool caps = (s & ShiftState.Caps) != 0 && capsLikeShift;
            string def = (s & ShiftState.AltGr) != 0 ? (sh ? "" : altGr) : (sh ^ caps ? shift : normal);
            Set(t, key, s, def);
        }
    }

    private static void Set(LayoutTable t, ushort key, ShiftState state, string def)
    {
        if (def.Length == 0) { t.Set(key, (int)state, '\0'); return; }
        bool dead = def.Length == 2 && def[0] == '!';
        t.Set(key, (int)state, dead ? def[1] : def[0], dead);
    }
}
