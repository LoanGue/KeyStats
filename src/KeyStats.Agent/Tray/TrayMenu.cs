using KeyStats.Agent.Interop;

namespace KeyStats.Agent.Tray;

internal enum TrayCommand : uint
{
    None = 0,
    Snooze = 1,
    SnoozeUntilResumed = 2,
    ResumeNow = 3,
    Quit = 4,
}

/// <summary>Menu du clic droit, construit à chaque ouverture selon l'état de la pause.</summary>
internal static class TrayMenu
{
    /// <summary>Affiche le menu au point donné et renvoie la commande choisie.</summary>
    public static TrayCommand Show(nint hwnd, int x, int y, bool snoozed)
    {
        nint menu = TrayApi.CreatePopupMenu();
        if (menu == 0) return TrayCommand.None;
        try
        {
            Add(menu, TrayCommand.Snooze, "Pause : 5 minutes de plus", enabled: true);
            Add(menu, TrayCommand.SnoozeUntilResumed, "Pause jusqu'à reprise manuelle", enabled: true);
            Add(menu, TrayCommand.ResumeNow, "Reprendre maintenant", enabled: snoozed);
            _ = TrayApi.AppendMenu(menu, TrayApi.MF_SEPARATOR, 0, null);
            Add(menu, TrayCommand.Quit, "Quitter KeyStats", enabled: true);
            // Sans premier plan, le menu ne se ferme pas quand on clique ailleurs.
            _ = TrayApi.SetForegroundWindow(hwnd);
            var command = (TrayCommand)TrayApi.TrackPopupMenuEx(menu, TrayApi.TPM_RETURNCMD | TrayApi.TPM_NONOTIFY | TrayApi.TPM_RIGHTBUTTON, x, y, hwnd, 0);
            _ = User32.PostMessage(hwnd, TrayApi.WM_NULL, 0, 0);
            return command;
        }
        finally
        {
            _ = TrayApi.DestroyMenu(menu);
        }
    }

    private static void Add(nint menu, TrayCommand command, string text, bool enabled) =>
        _ = TrayApi.AppendMenu(menu, TrayApi.MF_STRING | (enabled ? 0 : TrayApi.MF_GRAYED), (nuint)command, text);
}
