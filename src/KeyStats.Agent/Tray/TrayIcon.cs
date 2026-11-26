using KeyStats.Agent.Interop;

namespace KeyStats.Agent.Tray;

/// <summary>
/// L'icône de la zone de notification, posée sur la fenêtre de session. Ses clics arrivent en
/// <see cref="CallbackMessage"/> ; elle est reposée quand l'Explorateur redémarre.
/// </summary>
internal sealed unsafe class TrayIcon : IDisposable
{
    public const uint CallbackMessage = 0x8000 + 2;
    /// <summary>Diffusé quand la barre des tâches est recréée (Explorateur relancé).</summary>
    public static readonly uint TaskbarCreated = TrayApi.RegisterWindowMessage("TaskbarCreated");

    private readonly nint _hwnd;
    private readonly nint _activeIcon, _pausedIcon;
    private bool _added, _paused;
    private string _tip = "KeyStats";

    public TrayIcon(nint hwnd)
    {
        _hwnd = hwnd;
        int size = Math.Max(16, TrayApi.GetSystemMetrics(TrayApi.SM_CXSMICON));
        _activeIcon = IconArt.Create(size, paused: false);
        _pausedIcon = IconArt.Create(size, paused: true);
    }

    /// <summary>Met à jour l'icône et l'infobulle ; sans effet si rien n'a changé.</summary>
    public void Show(bool paused, string tip)
    {
        if (_added && paused == _paused && tip == _tip) return;
        _paused = paused;
        _tip = tip;
        var data = Data(TrayApi.NIF_MESSAGE | TrayApi.NIF_ICON | TrayApi.NIF_TIP | TrayApi.NIF_SHOWTIP);
        if (_added)
        {
            _ = TrayApi.Shell_NotifyIcon(TrayApi.NIM_MODIFY, &data);
            return;
        }
        // Échoue tant que la barre des tâches n'est pas prête : TaskbarCreated suivra.
        if (TrayApi.Shell_NotifyIcon(TrayApi.NIM_ADD, &data) == 0) return;
        data.uVersion = TrayApi.NOTIFYICON_VERSION_4;
        if (TrayApi.Shell_NotifyIcon(TrayApi.NIM_SETVERSION, &data) == 0)
        {
            // Sans la version 4, les clics arriveraient sous une autre forme : on retire et on réessaiera.
            _ = TrayApi.Shell_NotifyIcon(TrayApi.NIM_DELETE, &data);
            return;
        }
        _added = true;
    }

    /// <summary>L'Explorateur a oublié l'icône : on la repose telle quelle.</summary>
    public void Recreate()
    {
        _added = false;
        Show(_paused, _tip);
    }

    public void Dispose()
    {
        if (_added)
        {
            var data = Data(0);
            _ = TrayApi.Shell_NotifyIcon(TrayApi.NIM_DELETE, &data);
            _added = false;
        }
        _ = TrayApi.DestroyIcon(_activeIcon);
        _ = TrayApi.DestroyIcon(_pausedIcon);
    }

    private NOTIFYICONDATAW Data(uint flags)
    {
        var data = new NOTIFYICONDATAW
        {
            cbSize = (uint)sizeof(NOTIFYICONDATAW),
            hWnd = _hwnd,
            uID = 1,
            uFlags = flags,
            uCallbackMessage = CallbackMessage,
            hIcon = _paused ? _pausedIcon : _activeIcon,
        };
        var tip = _tip.AsSpan(0, Math.Min(_tip.Length, 127));
        tip.CopyTo(new Span<char>(data.szTip, 128));
        data.szTip[tip.Length] = '\0';
        return data;
    }
}
