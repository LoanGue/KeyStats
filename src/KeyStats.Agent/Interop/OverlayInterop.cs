using System.Runtime.InteropServices;

namespace KeyStats.Agent.Interop;

/// <summary>Fenêtre calque du compteur en direct : style, position, dessin GDI avec alpha par pixel.</summary>
internal static unsafe partial class OverlayApi
{
    public const uint WS_EX_TOPMOST = 0x8, WS_EX_TRANSPARENT = 0x20, WS_EX_TOOLWINDOW = 0x80;
    public const uint WS_EX_LAYERED = 0x80000, WS_EX_NOACTIVATE = 0x08000000;
    public const uint WS_POPUP = 0x80000000;
    public const int SW_HIDE = 0, SW_SHOWNOACTIVATE = 4;
    /// <summary>Exclue des captures et du partage d'écran (Windows 10 2004 et après).</summary>
    public const uint WDA_EXCLUDEFROMCAPTURE = 0x11;
    public const uint MONITOR_DEFAULTTONEAREST = 2;
    public const int MDT_EFFECTIVE_DPI = 0;
    public const uint ULW_ALPHA = 0x2;
    public const byte AC_SRC_OVER = 0, AC_SRC_ALPHA = 1;
    public const uint DT_CENTER = 0x1, DT_VCENTER = 0x4, DT_SINGLELINE = 0x20, DT_NOPREFIX = 0x800, DT_CALCRECT = 0x400;
    public const int TRANSPARENT = 1, FW_SEMIBOLD = 600, FW_NORMAL = 400;
    public const uint DEFAULT_CHARSET = 1, ANTIALIASED_QUALITY = 4;

    [LibraryImport("user32.dll")]
    public static partial int ShowWindow(nint hwnd, int command);
    [LibraryImport("user32.dll")]
    public static partial int SetWindowDisplayAffinity(nint hwnd, uint affinity);
    [LibraryImport("user32.dll")]
    public static partial int UpdateLayeredWindow(nint hwnd, nint dstDc, POINT* dst, SIZE* size, nint srcDc,
        POINT* src, uint key, BLENDFUNCTION* blend, uint flags);
    [LibraryImport("user32.dll")]
    public static partial nint MonitorFromWindow(nint hwnd, uint flags);
    [LibraryImport("user32.dll", EntryPoint = "GetMonitorInfoW")]
    public static partial int GetMonitorInfo(nint monitor, MONITORINFO* info);
    [LibraryImport("shcore.dll")]
    public static partial int GetDpiForMonitor(nint monitor, int type, uint* dpiX, uint* dpiY);
    [LibraryImport("user32.dll", EntryPoint = "DrawTextW")]
    public static partial int DrawText(nint dc, char* text, int length, RECT* rect, uint format);

    [LibraryImport("gdi32.dll")]
    public static partial nint CreateCompatibleDC(nint dc);
    [LibraryImport("gdi32.dll")]
    public static partial int DeleteDC(nint dc);
    [LibraryImport("gdi32.dll")]
    public static partial nint CreateDIBSection(nint dc, BITMAPINFOHEADER* info, uint usage, void** bits, nint section, uint offset);
    [LibraryImport("gdi32.dll")]
    public static partial nint SelectObject(nint dc, nint obj);
    [LibraryImport("gdi32.dll", EntryPoint = "CreateFontW")]
    public static partial nint CreateFont(int height, int width, int escapement, int orientation, int weight,
        uint italic, uint underline, uint strikeOut, uint charSet, uint outPrecision, uint clipPrecision,
        uint quality, uint pitchAndFamily, char* face);
    [LibraryImport("gdi32.dll")]
    public static partial uint SetTextColor(nint dc, uint color);
    [LibraryImport("gdi32.dll")]
    public static partial int SetBkMode(nint dc, int mode);
    [LibraryImport("gdi32.dll")]
    public static partial int GdiFlush();
}

internal struct POINT
{
    public int X, Y;
}

internal struct SIZE
{
    public int Cx, Cy;
}

internal struct RECT
{
    public int Left, Top, Right, Bottom;
}

internal struct MONITORINFO
{
    public uint cbSize;
    public RECT rcMonitor, rcWork;
    public uint dwFlags;
}

internal struct BLENDFUNCTION
{
    public byte BlendOp, BlendFlags, SourceConstantAlpha, AlphaFormat;
}

internal struct BITMAPINFOHEADER
{
    public uint biSize;
    public int biWidth, biHeight;
    public ushort biPlanes, biBitCount;
    public uint biCompression, biSizeImage;
    public int biXPelsPerMeter, biYPelsPerMeter;
    public uint biClrUsed, biClrImportant;
}
