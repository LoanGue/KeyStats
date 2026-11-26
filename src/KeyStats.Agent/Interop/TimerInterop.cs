using System.Runtime.InteropServices;

namespace KeyStats.Agent.Interop;

/// <summary>Minuterie de la fenêtre de capture (recalage périodique de l'horloge).</summary>
internal static unsafe partial class Timers
{
    public const uint WM_TIMER = 0x0113;

    [LibraryImport("user32.dll", SetLastError = true)]
    public static partial nuint SetTimer(nint hwnd, nuint id, uint elapseMs, nint proc);
    [LibraryImport("user32.dll")]
    public static partial int KillTimer(nint hwnd, nuint id);
}
