using System.Runtime.InteropServices;

namespace KeyStats.Agent.Interop;

/// <summary>Appels Win32 de la capture. Retours BOOL laissés en <c>int</c> pour rester sans marshalling.</summary>
internal static unsafe partial class User32
{
    public const uint WM_DESTROY = 0x0002, WM_CLOSE = 0x0010, WM_INPUT = 0x00FF;
    public const uint RIDEV_REMOVE = 0x1, RIDEV_INPUTSINK = 0x100;
    public const uint RID_INPUT = 0x10000003;
    public const uint RIM_TYPEMOUSE = 0, RIM_TYPEKEYBOARD = 1;
    public const int ERROR_CLASS_ALREADY_EXISTS = 1410;
    public const nint HWND_MESSAGE = -3;

    [LibraryImport("user32.dll", EntryPoint = "RegisterClassExW", SetLastError = true)]
    public static partial ushort RegisterClassEx(WNDCLASSEXW* wc);
    [LibraryImport("user32.dll", EntryPoint = "UnregisterClassW")]
    public static partial int UnregisterClass(char* className, nint instance);
    [LibraryImport("user32.dll", EntryPoint = "CreateWindowExW", SetLastError = true)]
    public static partial nint CreateWindowEx(uint exStyle, char* className, char* windowName, uint style,
        int x, int y, int width, int height, nint parent, nint menu, nint instance, nint param);

    [LibraryImport("user32.dll", SetLastError = true)]
    public static partial int DestroyWindow(nint hwnd);
    [LibraryImport("user32.dll", EntryPoint = "DefWindowProcW")]
    public static partial nint DefWindowProc(nint hwnd, uint msg, nint wParam, nint lParam);
    [LibraryImport("user32.dll", EntryPoint = "GetMessageW")]
    public static partial int GetMessage(MSG* msg, nint hwnd, uint filterMin, uint filterMax);
    [LibraryImport("user32.dll", EntryPoint = "DispatchMessageW")]
    public static partial nint DispatchMessage(MSG* msg);
    [LibraryImport("user32.dll", EntryPoint = "PostMessageW", SetLastError = true)]
    public static partial int PostMessage(nint hwnd, uint msg, nint wParam, nint lParam);
    [LibraryImport("user32.dll")]
    public static partial void PostQuitMessage(int exitCode);
    [LibraryImport("user32.dll", SetLastError = true)]
    public static partial int RegisterRawInputDevices(RAWINPUTDEVICE* devices, uint count, uint size);
    [LibraryImport("user32.dll")]
    public static partial uint GetRawInputData(nint rawInput, uint command, void* data, uint* size, uint headerSize);
}

internal static unsafe partial class Kernel32
{
    private const int ThreadPowerThrottling = 3;
    private const uint THREAD_POWER_THROTTLING_EXECUTION_SPEED = 0x1;

    [LibraryImport("kernel32.dll", EntryPoint = "GetModuleHandleW")]
    public static partial nint GetModuleHandle(char* moduleName);
    [LibraryImport("kernel32.dll")]
    private static partial nint GetCurrentThread();

    [LibraryImport("kernel32.dll")]
    private static partial int SetThreadInformation(nint thread, int infoClass, void* info, uint size);

    /// <summary>
    /// Sort le thread courant du mode économie d'énergie (EcoQoS), qui espacerait les réveils et donc
    /// les horodatages. Sans effet avant Windows 10 1709 : l'échec est ignoré.
    /// </summary>
    public static void OptOutOfEcoQos()
    {
        var state = new THREAD_POWER_THROTTLING_STATE { Version = 1, ControlMask = THREAD_POWER_THROTTLING_EXECUTION_SPEED };
        _ = SetThreadInformation(GetCurrentThread(), ThreadPowerThrottling, &state, (uint)sizeof(THREAD_POWER_THROTTLING_STATE));
    }
}

internal unsafe struct WNDCLASSEXW
{
    public uint cbSize, style;
    public delegate* unmanaged<nint, uint, nint, nint, nint> lpfnWndProc;
    public int cbClsExtra, cbWndExtra;
    public nint hInstance, hIcon, hCursor, hbrBackground;
    public char* lpszMenuName, lpszClassName;
    public nint hIconSm;
}

internal struct MSG
{
    public nint hwnd;
    public uint message;
    public nint wParam, lParam;
    public uint time;
    public int ptX, ptY;
    public uint lPrivate;
}

internal struct RAWINPUTDEVICE
{
    public ushort usUsagePage, usUsage;
    public uint dwFlags;
    public nint hwndTarget;
}

// Structures remplies par Windows : jamais assignées côté C#.
#pragma warning disable CS0649
internal struct RAWINPUTHEADER
{
    public uint dwType, dwSize;
    public nint hDevice, wParam;
}

internal struct RAWKEYBOARD
{
    public ushort MakeCode, Flags, Reserved, VKey;
    public uint Message, ExtraInformation;
}

/// <summary>Seuls les boutons servent ; le reste (déplacement, molette) est couvert par la taille.</summary>
[StructLayout(LayoutKind.Explicit, Size = 24)]
internal struct RAWMOUSE
{
    [FieldOffset(4)] public ushort usButtonFlags;
}
#pragma warning restore CS0649

internal struct THREAD_POWER_THROTTLING_STATE
{
    public uint Version, ControlMask, StateMask;
}
