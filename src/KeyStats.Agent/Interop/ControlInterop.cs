using System.Runtime.InteropServices;

namespace KeyStats.Agent.Interop;

/// <summary>Passage d'une commande du tube au thread de la fenêtre de session.</summary>
internal static partial class ControlApi
{
    public const uint SMTO_ABORTIFHUNG = 0x2;

    [LibraryImport("user32.dll", EntryPoint = "SendMessageTimeoutW")]
    public static partial nint SendMessageTimeout(nint hwnd, uint msg, nint wParam, nint lParam,
        uint flags, uint timeoutMs, out nint result);
}
