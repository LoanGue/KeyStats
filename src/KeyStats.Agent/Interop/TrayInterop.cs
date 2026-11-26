using System.Runtime.InteropServices;

namespace KeyStats.Agent.Interop;

/// <summary>Icône de la zone de notification, son menu, et le dessin de l'icône.</summary>
internal static unsafe partial class TrayApi
{
    public const uint NIM_ADD = 0, NIM_MODIFY = 1, NIM_DELETE = 2, NIM_SETVERSION = 4;
    public const uint NIF_MESSAGE = 0x1, NIF_ICON = 0x2, NIF_TIP = 0x4, NIF_SHOWTIP = 0x80;
    public const uint NOTIFYICON_VERSION_4 = 4;
    public const uint WM_NULL = 0, WM_CONTEXTMENU = 0x007B, WM_LBUTTONUP = 0x0202;
    public const uint MF_STRING = 0, MF_GRAYED = 0x1, MF_SEPARATOR = 0x800;
    public const uint TPM_RIGHTBUTTON = 0x2, TPM_NONOTIFY = 0x80, TPM_RETURNCMD = 0x100;
    public const int SM_CXSMICON = 49;

    [LibraryImport("shell32.dll", EntryPoint = "Shell_NotifyIconW")]
    public static partial int Shell_NotifyIcon(uint message, NOTIFYICONDATAW* data);
    [LibraryImport("user32.dll", EntryPoint = "RegisterWindowMessageW", StringMarshalling = StringMarshalling.Utf16)]
    public static partial uint RegisterWindowMessage(string name);

    [LibraryImport("user32.dll")]
    public static partial nint CreatePopupMenu();
    [LibraryImport("user32.dll", EntryPoint = "AppendMenuW", StringMarshalling = StringMarshalling.Utf16)]
    public static partial int AppendMenu(nint menu, uint flags, nuint id, string? text);
    [LibraryImport("user32.dll")]
    public static partial int TrackPopupMenuEx(nint menu, uint flags, int x, int y, nint hwnd, nint tpm);
    [LibraryImport("user32.dll")]
    public static partial int DestroyMenu(nint menu);
    [LibraryImport("user32.dll")]
    public static partial int SetForegroundWindow(nint hwnd);
    [LibraryImport("user32.dll")]
    public static partial int GetSystemMetrics(int index);

    [LibraryImport("user32.dll", SetLastError = true)]
    public static partial nint CreateIconIndirect(ICONINFO* info);
    [LibraryImport("user32.dll")]
    public static partial int DestroyIcon(nint icon);
    [LibraryImport("gdi32.dll")]
    public static partial nint CreateBitmap(int width, int height, uint planes, uint bitsPerPixel, void* bits);
    [LibraryImport("gdi32.dll")]
    public static partial int DeleteObject(nint obj);
}

internal unsafe struct NOTIFYICONDATAW
{
    public uint cbSize;
    public nint hWnd;
    public uint uID, uFlags, uCallbackMessage;
    public nint hIcon;
    public fixed char szTip[128];
    public uint dwState, dwStateMask;
    public fixed char szInfo[256];
    public uint uVersion;
    public fixed char szInfoTitle[64];
    public uint dwInfoFlags;
    public Guid guidItem;
    public nint hBalloonIcon;
}

internal struct ICONINFO
{
    public int fIcon;
    public uint xHotspot, yHotspot;
    public nint hbmMask, hbmColor;
}
