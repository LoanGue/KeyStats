using System.Runtime.InteropServices;

namespace KeyStats.Agent.Interop;

/// <summary>Appels Win32 du suivi de la fenêtre active.</summary>
internal static unsafe partial class WinEvents
{
    public const uint EVENT_SYSTEM_FOREGROUND = 0x0003, EVENT_OBJECT_NAMECHANGE = 0x800C;
    public const uint WINEVENT_OUTOFCONTEXT = 0x0, WINEVENT_SKIPOWNPROCESS = 0x2;
    public const int OBJID_WINDOW = 0, CHILDID_SELF = 0;

    [LibraryImport("user32.dll")]
    public static partial nint SetWinEventHook(uint eventMin, uint eventMax, nint module,
        delegate* unmanaged<nint, uint, nint, int, int, uint, uint, void> proc, uint processId, uint threadId, uint flags);
    [LibraryImport("user32.dll")]
    public static partial int UnhookWinEvent(nint hook);
    [LibraryImport("user32.dll")]
    public static partial nint GetForegroundWindow();
    [LibraryImport("user32.dll")]
    public static partial uint GetWindowThreadProcessId(nint hwnd, uint* processId);
    [LibraryImport("user32.dll", EntryPoint = "GetWindowTextW")]
    public static partial int GetWindowText(nint hwnd, char* text, int max);
}

internal static unsafe partial class Process32
{
    public const uint PROCESS_QUERY_LIMITED_INFORMATION = 0x1000, TOKEN_QUERY = 0x8;
    public const int TokenElevation = 20;

    [LibraryImport("kernel32.dll", SetLastError = true)]
    public static partial nint OpenProcess(uint access, int inherit, uint processId);
    [LibraryImport("kernel32.dll", EntryPoint = "QueryFullProcessImageNameW", SetLastError = true)]
    public static partial int QueryFullProcessImageName(nint process, uint flags, char* name, uint* size);
    [LibraryImport("kernel32.dll")]
    public static partial int CloseHandle(nint handle);
    [LibraryImport("advapi32.dll", SetLastError = true)]
    public static partial int OpenProcessToken(nint process, uint access, nint* token);
    [LibraryImport("advapi32.dll", SetLastError = true)]
    public static partial int GetTokenInformation(nint token, int infoClass, void* info, uint size, uint* returned);
}
