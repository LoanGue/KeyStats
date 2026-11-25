using KeyStats.Core.Input;
using KeyStats.Core.Text;

namespace KeyStats.Core.Analysis;

/// <summary>Effet d'une touche sur le texte, avant toute analyse.</summary>
internal enum TextAction : byte
{
    Neutral, Char, DeadKey, Backspace, WordBackspace, AltDigit,
    Tab, Escape, Delete, Navigate, Shortcut, OtherKey,
}

/// <summary>Traduction d'un appui en caractère, touche morte, raccourci ou touche d'édition.</summary>
internal static class KeyTranslator
{
    /// <summary>
    /// Ce que produit l'appui sur <paramref name="k"/>, selon les touches déjà enfoncées
    /// (<paramref name="down"/>, indexé par code de touche) et l'état des verrous.
    /// </summary>
    public static TextAction Translate(
        ReadOnlySpan<bool> down, LayoutTable layout, bool capsLock, bool numLock, ushort k, out char c, out ShortcutModifiers mods)
    {
        c = '\0';
        mods = ShortcutModifiers.None;
        if (KeyCode.IsModifier(k) || KeyCode.IsLock(k) || KeyCode.IsNeutral(k)) return TextAction.Neutral;

        bool ctrl = down[KeyCode.LeftCtrl] || down[KeyCode.RightCtrl];
        bool altGrKey = down[KeyCode.RightAlt] && layout.HasAltGr;
        bool alt = down[KeyCode.LeftAlt] || (down[KeyCode.RightAlt] && !layout.HasAltGr);
        bool win = down[KeyCode.LeftWin] || down[KeyCode.RightWin];
        bool shift = down[KeyCode.LeftShift] || down[KeyCode.RightShift];

        if (alt && !ctrl && !altGrKey && !win && KeyCode.NumpadDigit(k) >= 0) return TextAction.AltDigit;

        if (!win && (altGrKey || (ctrl && alt)))
        {
            int slot = (int)ShiftState.AltGr | (shift ? (int)ShiftState.Shift : 0) | (capsLock ? (int)ShiftState.Caps : 0);
            c = layout.Get(k, slot, out bool isDead);
            if (c != '\0') return isDead ? TextAction.DeadKey : TextAction.Char;
            mods = ShortcutModifiers.Ctrl | ShortcutModifiers.Alt | (shift ? ShortcutModifiers.Shift : 0);
            return TextAction.Shortcut;
        }
        if (ctrl || alt || win)
        {
            if (k == KeyCode.Backspace && ctrl && !alt && !win) return TextAction.WordBackspace;
            mods = (ctrl ? ShortcutModifiers.Ctrl : 0) | (alt || altGrKey ? ShortcutModifiers.Alt : 0)
                | (shift ? ShortcutModifiers.Shift : 0) | (win ? ShortcutModifiers.Win : 0);
            return TextAction.Shortcut;
        }

        switch (k)
        {
            case KeyCode.Backspace: return TextAction.Backspace;
            case KeyCode.Tab: return TextAction.Tab;
            case KeyCode.Escape: return TextAction.Escape;
            case KeyCode.Enter or KeyCode.NumEnter:
                c = '\n';
                return TextAction.Char;
            case KeyCode.Delete: return TextAction.Delete;
            case KeyCode.Apps: return TextAction.OtherKey;
        }
        if (KeyCode.IsNavigation(k)) return TextAction.Navigate;
        if (KeyCode.IsNumpadDual(k))
        {
            // Verr Num actif : chiffres, sauf avec Maj qui rend temporairement les flèches.
            if (numLock && !shift)
            {
                c = layout.Get(k, LayoutTable.NumLockSlot, out _);
                if (c != '\0') return TextAction.Char;
            }
            return k == KeyCode.NumDecimal ? TextAction.Delete : TextAction.Navigate;
        }
        if (KeyCode.IsFunctionKey(k)) return TextAction.OtherKey;

        int plain = (shift ? (int)ShiftState.Shift : 0) | (capsLock ? (int)ShiftState.Caps : 0);
        c = layout.Get(k, plain, out bool dead);
        if (c == '\0') return TextAction.OtherKey;
        return dead ? TextAction.DeadKey : TextAction.Char;
    }
}
