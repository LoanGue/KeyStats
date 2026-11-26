using System.ComponentModel;
using System.Runtime.InteropServices;
using KeyStats.Agent.Interop;
using KeyStats.Agent.Tray;

namespace KeyStats.Agent.Lifecycle;

/// <summary>Ce que la fenêtre de session signale, sur le thread qui fait tourner <see cref="SessionWindow.Run"/>.</summary>
internal interface ISessionEvents
{
    void OnLock();
    void OnUnlock();
    void OnSuspend();
    void OnResume();
    void OnTimeChange();
    /// <summary>Fin de session Windows : tout doit être écrit avant le retour.</summary>
    void OnEndSession();
    /// <summary>Fenêtre créée : l'icône de notification peut s'y poser.</summary>
    void OnCreated(nint hwnd);
    /// <summary>Fenêtre détruite : dernière occasion de retirer l'icône.</summary>
    void OnDestroyed();
    void OnIconMessage(nint wParam, nint lParam);
    void OnTimer(nuint id);
    void OnTaskbarCreated();
}

/// <summary>
/// Fenêtre cachée de premier niveau, sur le thread principal. Une fenêtre de messages ne reçoit pas
/// les diffusions (changement d'heure, veille, fin de session) : il faut une vraie fenêtre, jamais affichée.
/// </summary>
internal sealed unsafe class SessionWindow
{
    private const string ClassName = "KeyStats.Session";
    private static SessionWindow? s_current;

    private readonly ISessionEvents _events;
    private nint _hwnd;
    private bool _registered;

    public SessionWindow(ISessionEvents events) => _events = events;

    /// <summary>Fait tourner la boucle de messages jusqu'à la fin de session (ou un <c>WM_CLOSE</c>).</summary>
    public void Run()
    {
        if (Interlocked.CompareExchange(ref s_current, this, null) is not null)
            throw new InvalidOperationException("Une fenêtre de session existe déjà.");
        nint instance = Kernel32.GetModuleHandle(null);
        fixed (char* cls = ClassName)
        {
            try
            {
                var wc = new WNDCLASSEXW { cbSize = (uint)sizeof(WNDCLASSEXW), lpfnWndProc = &WndProc, hInstance = instance, lpszClassName = cls };
                if (User32.RegisterClassEx(&wc) == 0 && Marshal.GetLastPInvokeError() != User32.ERROR_CLASS_ALREADY_EXISTS)
                    throw new Win32Exception(Marshal.GetLastPInvokeError());
                _hwnd = User32.CreateWindowEx(Session.WS_EX_TOOLWINDOW, cls, null, Session.WS_POPUP, 0, 0, 0, 0, 0, 0, instance, 0);
                if (_hwnd == 0) throw new Win32Exception(Marshal.GetLastPInvokeError());
                // Sans notification, le verrouillage passe inaperçu ; la veille et l'heure restent suivies.
                _registered = Session.Register(_hwnd);
                nint hwnd = _hwnd;
                Handle(e => e.OnCreated(hwnd));
                MSG msg;
                while (User32.GetMessage(&msg, 0, 0, 0) > 0) User32.DispatchMessage(&msg);
            }
            finally
            {
                _ = User32.UnregisterClass(cls, instance);
                s_current = null;
            }
        }
    }

    [UnmanagedCallersOnly]
    private static nint WndProc(nint hwnd, uint msg, nint wParam, nint lParam)
    {
        if (s_current is not { } w) return User32.DefWindowProc(hwnd, msg, wParam, lParam);
        // Identifiant attribué à l'exécution : hors du switch.
        if (msg == TrayIcon.TaskbarCreated && msg != 0)
        {
            w.Handle(static e => e.OnTaskbarCreated());
            return 0;
        }
        switch (msg)
        {
            case Session.WM_WTSSESSION_CHANGE when wParam == Session.WTS_SESSION_LOCK:
                w.Handle(static e => e.OnLock());
                return 0;
            case Session.WM_WTSSESSION_CHANGE when wParam == Session.WTS_SESSION_UNLOCK:
                w.Handle(static e => e.OnUnlock());
                return 0;
            case Session.WM_POWERBROADCAST when wParam == Session.PBT_APMSUSPEND:
                w.Handle(static e => e.OnSuspend());
                return 1;
            // Réveil automatique, puis réveil par l'utilisateur : le second arrive en double, sans gêne.
            case Session.WM_POWERBROADCAST when wParam is Session.PBT_APMRESUMEAUTOMATIC or Session.PBT_APMRESUMESUSPEND:
                w.Handle(static e => e.OnResume());
                return 1;
            case Session.WM_TIMECHANGE:
                w.Handle(static e => e.OnTimeChange());
                return 0;
            case TrayIcon.CallbackMessage:
                w.Handle((wParam, lParam), static (e, m) => e.OnIconMessage(m.wParam, m.lParam));
                return 0;
            case Timers.WM_TIMER:
                w.Handle((nuint)wParam, static (e, id) => e.OnTimer(id));
                return 0;
            case Session.WM_QUERYENDSESSION:
                return 1;
            case Session.WM_ENDSESSION when wParam != 0:
                // Le processus peut être tué dès le retour : tout s'écrit ici, puis la boucle s'arrête.
                w.Handle(static e => e.OnEndSession());
                _ = User32.DestroyWindow(hwnd);
                return 0;
            case User32.WM_CLOSE:
                _ = User32.DestroyWindow(hwnd);
                return 0;
            case User32.WM_DESTROY:
                w.Handle(static e => e.OnDestroyed());
                if (w._registered) Session.Unregister(hwnd);
                User32.PostQuitMessage(0);
                return 0;
        }
        return User32.DefWindowProc(hwnd, msg, wParam, lParam);
    }

    // Variante avec état : pas de fermeture allouée pour les messages fréquents (minuterie, icône).
    private void Handle<T>(T state, Action<ISessionEvents, T> action)
    {
        try
        {
            action(_events, state);
        }
        catch (Exception)
        {
            // Ignorée, comme ci-dessous.
        }
    }

    private void Handle(Action<ISessionEvents> action)
    {
        // Une exception qui sortirait d'ici arrêterait le processus.
        try
        {
            action(_events);
        }
        catch (Exception)
        {
            // Ignorée : le message pourrait citer une valeur, et l'agent doit continuer.
        }
    }
}
