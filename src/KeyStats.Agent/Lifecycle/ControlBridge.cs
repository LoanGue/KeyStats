using System.Diagnostics;
using KeyStats.Agent.Interop;
using KeyStats.Core.Control;

namespace KeyStats.Agent.Lifecycle;

/// <summary>
/// Tube de commande de l'agent. Chaque commande reçue est passée au thread de la fenêtre de session
/// par <c>SendMessageTimeout</c>, puisque <see cref="Core.Recording.PauseSources"/> n'a qu'un thread appelant.
/// </summary>
internal sealed class ControlBridge : IDisposable
{
    /// <summary>Après <see cref="Tray.TrayIcon.CallbackMessage"/> dans la plage <c>WM_APP</c>.</summary>
    public const uint ControlMessage = 0x8000 + 3;

    // Le serveur laisse 2 s par client : la réponse doit partir avant.
    private const uint WindowTimeoutMs = 1500;

    private readonly nint _hwnd;
    private readonly ControlServer? _server;

    private ControlBridge(nint hwnd, string pipeName)
    {
        _hwnd = hwnd;
        try
        {
            _server = new ControlServer(pipeName, Forward);
            _server.Start();
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            // Nom déjà pris par un autre programme, ou par un autre compte : l'agent tourne sans tube.
            _server?.Dispose();
            _server = null;
        }
    }

    /// <summary>Le mutex garantit déjà un seul agent par base : un nom pris ne vient pas d'un autre agent.</summary>
    public static ControlBridge Start(nint hwnd, string dbPath) =>
        new(hwnd, ControlProtocol.PipeName(Process.GetCurrentProcess().SessionId, dbPath));

    public bool Listening => _server is not null;

    public void Dispose() => _server?.Dispose();

    private AgentReply Forward(AgentCommand command)
    {
        if (ControlApi.SendMessageTimeout(_hwnd, ControlMessage, (nint)command, 0,
                ControlApi.SMTO_ABORTIFHUNG, WindowTimeoutMs, out nint result) == 0)
            return AgentReply.Failed;
        return (ulong)result <= byte.MaxValue && Enum.IsDefined((AgentReply)(byte)result) ? (AgentReply)(byte)result : AgentReply.Failed;
    }
}
