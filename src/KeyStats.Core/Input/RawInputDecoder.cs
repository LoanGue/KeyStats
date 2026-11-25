namespace KeyStats.Core.Input;

/// <summary>
/// Traduction des champs Raw Input (<c>RAWKEYBOARD</c>, <c>RAWMOUSE</c>) en <see cref="InputEvent"/>.
/// Sans appel Win32 : l'agent lit les structures, Core décide de ce qu'on garde.
/// </summary>
public static class RawInputDecoder
{
    /// <summary>Drapeaux de <c>RAWKEYBOARD.Flags</c>.</summary>
    public const ushort KeyBreak = 1, KeyE0 = 2, KeyE1 = 4;

    /// <summary>Taille minimale du tampon passé à <see cref="MouseButtons"/>.</summary>
    public const int MaxMouseEvents = 10;

    /// <summary>
    /// Faux si la frappe est ignorée : injectée par un logiciel (aucun appareil), fausse touche que
    /// Windows ajoute autour de certaines séquences (code virtuel 0xFF), ou touche inexploitable.
    /// </summary>
    public static bool Keyboard(ushort makeCode, ushort flags, ushort virtualKey, bool fromDevice, out EventKind kind, out ushort key)
    {
        kind = (flags & KeyBreak) != 0 ? EventKind.KeyUp : EventKind.KeyDown;
        key = 0;
        if (!fromDevice || virtualKey >= 0xFF) return false;
        key = KeyCode.FromScanCode(makeCode, (flags & KeyE0) != 0, (flags & KeyE1) != 0, virtualKey);
        return key != 0;
    }

    /// <summary>
    /// Boutons de <c>RAWMOUSE.usButtonFlags</c> (gauche, droit, milieu, X1, X2 : deux bits chacun,
    /// appui puis relâchement). Les mouvements et la molette ne sont pas gardés. Renvoie le nombre
    /// d'événements écrits dans <paramref name="dest"/>.
    /// </summary>
    public static int MouseButtons(ushort buttonFlags, long ms, ushort device, Span<InputEvent> dest)
    {
        int n = 0;
        for (int b = 0; b < 5; b++)
        {
            int down = 1 << (2 * b);
            if ((buttonFlags & down) != 0) dest[n++] = new InputEvent(ms, EventKind.MouseDown, (ushort)(b + 1), device);
            if ((buttonFlags & (down << 1)) != 0) dest[n++] = new InputEvent(ms, EventKind.MouseUp, (ushort)(b + 1), device);
        }
        return n;
    }
}
