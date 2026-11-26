using System.Runtime.InteropServices;

namespace KeyStats.Agent.Interop;

/// <summary>Messages et appels Win32 du cycle de vie : verrouillage, veille, heure, fin de session.</summary>
internal static unsafe partial class Session
{
    public const uint WM_QUERYENDSESSION = 0x0011, WM_ENDSESSION = 0x0016, WM_TIMECHANGE = 0x001E;
    public const uint WM_POWERBROADCAST = 0x0218, WM_WTSSESSION_CHANGE = 0x02B1;
    public const nint PBT_APMSUSPEND = 0x4, PBT_APMRESUMESUSPEND = 0x7, PBT_APMRESUMEAUTOMATIC = 0x12;
    public const nint WTS_SESSION_LOCK = 0x7, WTS_SESSION_UNLOCK = 0x8;
    public const uint WS_POPUP = 0x80000000, WS_EX_TOOLWINDOW = 0x80;
    private const uint NOTIFY_FOR_THIS_SESSION = 0;

    [LibraryImport("wtsapi32.dll", SetLastError = true)]
    private static partial int WTSRegisterSessionNotification(nint hwnd, uint flags);
    [LibraryImport("wtsapi32.dll")]
    private static partial int WTSUnRegisterSessionNotification(nint hwnd);

    public static bool Register(nint hwnd) => WTSRegisterSessionNotification(hwnd, NOTIFY_FOR_THIS_SESSION) != 0;

    public static void Unregister(nint hwnd) => _ = WTSUnRegisterSessionNotification(hwnd);
}
