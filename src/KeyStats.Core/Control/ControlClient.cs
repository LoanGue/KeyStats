using System.IO.Pipes;

namespace KeyStats.Core.Control;

/// <summary>Côté client du tube de l'agent : tableau de bord, second lancement, désinstallation.</summary>
public static class ControlClient
{
    /// <summary>
    /// Envoie une commande et attend la réponse, le tout dans <paramref name="timeout"/>.
    /// Le serveur doit appartenir à l'utilisateur courant.
    /// </summary>
    /// <returns>La réponse, ou null si aucun agent n'a répondu à temps.</returns>
    public static async Task<AgentReply?> SendAsync(string pipeName, AgentCommand command, TimeSpan timeout)
    {
        using var cts = new CancellationTokenSource(timeout);
        try
        {
            using var pipe = new NamedPipeClientStream(".", pipeName, PipeDirection.InOut,
                PipeOptions.Asynchronous | PipeOptions.CurrentUserOnly);
            await pipe.ConnectAsync(cts.Token).ConfigureAwait(false);
            await pipe.WriteAsync(ControlProtocol.Encode(command), cts.Token).ConfigureAwait(false);
            byte[] buffer = new byte[ControlProtocol.MaxLine];
            int length = await ControlProtocol.ReadLineAsync(pipe, buffer, cts.Token).ConfigureAwait(false);
            return length >= 0 && ControlProtocol.TryParse(buffer.AsSpan(0, length), out AgentReply reply) ? reply : null;
        }
        catch (Exception e) when (e is OperationCanceledException or IOException or TimeoutException or UnauthorizedAccessException)
        {
            return null;
        }
    }
}
