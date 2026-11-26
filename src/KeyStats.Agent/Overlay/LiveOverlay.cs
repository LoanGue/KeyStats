using System.ComponentModel;
using System.Runtime.InteropServices;
using KeyStats.Agent.Interop;
using KeyStats.Core.Analysis;

namespace KeyStats.Agent.Overlay;

/// <summary>
/// Compteur en direct, en bas à droite de l'écran où l'on tape : fenêtre calque au premier plan, qui
/// laisse passer les clics, ne prend jamais le focus, absente d'Alt+Tab et des captures d'écran.
/// Vit sur le thread de la fenêtre de session ; sa minuterie ne tourne que pendant l'affichage.
/// </summary>
internal sealed unsafe class LiveOverlay : IDisposable
{
    private const string ClassName = "KeyStats.Live";
    // Messages propres à cette fenêtre : pas de conflit avec ceux de la fenêtre de session.
    private const uint WM_APP_SHOWN = 0x8000 + 1;
    private const nuint TickId = 1;
    private const uint TickMs = 250;
    private static LiveOverlay? s_current;

    private readonly LiveMeter _meter;
    private readonly Func<long> _now;
    private readonly LiveSmoother _smoother = new();
    private nint _hwnd, _instance;
    private bool _visible, _ticking;
    private int _x, _y, _width, _height, _dpi;

    public LiveOverlay(LiveMeter meter, Func<long> now, bool enabled)
    {
        _meter = meter;
        _now = now;
        Enabled = enabled;
    }

    /// <summary><c>liveCounter.enabled</c> ; désactivé, le compteur disparaît tout de suite.</summary>
    public bool Enabled
    {
        get;
        set
        {
            field = value;
            if (!value) Hide();
        }
    }

    /// <summary>Crée la fenêtre, cachée, sur le thread appelant (celui de la boucle de messages).</summary>
    public void Create()
    {
        if (Interlocked.CompareExchange(ref s_current, this, null) is not null)
            throw new InvalidOperationException("Un compteur existe déjà.");
        _instance = Kernel32.GetModuleHandle(null);
        nint hwnd;
        fixed (char* cls = ClassName)
        {
            var wc = new WNDCLASSEXW { cbSize = (uint)sizeof(WNDCLASSEXW), lpfnWndProc = &WndProc, hInstance = _instance, lpszClassName = cls };
            if (User32.RegisterClassEx(&wc) == 0 && Marshal.GetLastPInvokeError() != User32.ERROR_CLASS_ALREADY_EXISTS)
                hwnd = 0;
            else
            {
                uint ex = OverlayApi.WS_EX_LAYERED | OverlayApi.WS_EX_TRANSPARENT | OverlayApi.WS_EX_TOPMOST
                    | OverlayApi.WS_EX_NOACTIVATE | OverlayApi.WS_EX_TOOLWINDOW;
                hwnd = User32.CreateWindowEx(ex, cls, null, OverlayApi.WS_POPUP, 0, 0, 0, 0, 0, 0, _instance, 0);
            }
        }
        if (hwnd == 0)
        {
            int error = Marshal.GetLastPInvokeError();
            s_current = null;
            throw new Win32Exception(error);
        }
        Volatile.Write(ref _hwnd, hwnd);
        // Refusé avant Windows 10 2004 : le compteur reste alors visible sur les captures.
        _ = OverlayApi.SetWindowDisplayAffinity(_hwnd, OverlayApi.WDA_EXCLUDEFROMCAPTURE);
    }

    /// <summary>Depuis le thread d'analyse (<see cref="LiveMeter.Shown"/>) : une vraie frappe commence.</summary>
    public void PostShown()
    {
        nint hwnd = Volatile.Read(ref _hwnd);
        if (hwnd != 0) _ = User32.PostMessage(hwnd, WM_APP_SHOWN, 0, 0);
    }

    public void Dispose()
    {
        if (_hwnd == 0) return;
        Hide();
        _ = User32.DestroyWindow(_hwnd);
        Volatile.Write(ref _hwnd, 0);
        fixed (char* cls = ClassName) _ = User32.UnregisterClass(cls, _instance);
        s_current = null;
    }

    [UnmanagedCallersOnly]
    private static nint WndProc(nint hwnd, uint msg, nint wParam, nint lParam)
    {
        if (s_current is not { } o || hwnd != o._hwnd) return User32.DefWindowProc(hwnd, msg, wParam, lParam);
        try
        {
            switch (msg)
            {
                case WM_APP_SHOWN:
                    o.Start();
                    return 0;
                case Timers.WM_TIMER when (nuint)wParam == TickId:
                    o.Tick();
                    return 0;
            }
        }
        catch (Exception)
        {
            // Le compteur n'est qu'un affichage : une erreur le cache, l'agent continue.
            o.Hide();
            return 0;
        }
        return User32.DefWindowProc(hwnd, msg, wParam, lParam);
    }

    private void Start()
    {
        if (!Enabled || _ticking || !Place()) return;
        _smoother.Reset();
        // Sans minuterie, rien ne cacherait le compteur : il ne s'affiche pas.
        _ticking = Timers.SetTimer(_hwnd, TickId, TickMs, 0) != 0;
        if (_ticking) Tick();
    }

    private void Tick()
    {
        var reading = _meter.Read(_now());
        if (!Enabled || reading.Mode == LiveMode.Hidden)
        {
            Hide();
            return;
        }
        var text = LiveDisplay.Frame(reading, _smoother, _now());
        if (!OverlayPainter.Paint(_hwnd, _x, _y, _width, _height, _dpi, text) || _visible) return;
        _ = OverlayApi.ShowWindow(_hwnd, OverlayApi.SW_SHOWNOACTIVATE);
        _visible = true;
    }

    /// <summary>Écran de la fenêtre active, au-dessus de sa barre des tâches, à son échelle.</summary>
    /// <returns>Faux si l'écran est introuvable : le compteur ne s'affiche pas.</returns>
    private bool Place()
    {
        nint monitor = OverlayApi.MonitorFromWindow(WinEvents.GetForegroundWindow(), OverlayApi.MONITOR_DEFAULTTONEAREST);
        var info = new MONITORINFO { cbSize = (uint)sizeof(MONITORINFO) };
        if (OverlayApi.GetMonitorInfo(monitor, &info) == 0) return false;
        uint dpiX = 96, dpiY = 96;
        if (OverlayApi.GetDpiForMonitor(monitor, OverlayApi.MDT_EFFECTIVE_DPI, &dpiX, &dpiY) != 0) dpiX = 96;
        _dpi = (int)dpiX;
        _width = LiveDisplay.Scale(LiveDisplay.Width, _dpi);
        _height = LiveDisplay.Scale(LiveDisplay.Height, _dpi);
        var work = new ScreenRect(info.rcWork.Left, info.rcWork.Top, info.rcWork.Right, info.rcWork.Bottom);
        (_x, _y) = LiveDisplay.BottomRight(work, _width, _height, LiveDisplay.Scale(LiveDisplay.Margin, _dpi));
        return true;
    }

    /// <summary>Cache le compteur et arrête sa minuterie, jusqu'au prochain signal de frappe.</summary>
    public void Hide()
    {
        if (_ticking) _ = Timers.KillTimer(_hwnd, TickId);
        _ticking = false;
        if (_visible) _ = OverlayApi.ShowWindow(_hwnd, OverlayApi.SW_HIDE);
        _visible = false;
    }
}
