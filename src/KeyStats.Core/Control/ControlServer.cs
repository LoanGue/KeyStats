using System.IO.Pipes;

namespace KeyStats.Core.Control;

/// <summary>
/// Côté agent du tube : un client à la fois, une commande par connexion. Le tube n'accepte que
/// l'utilisateur courant, et sa création échoue si une instance du même nom existe déjà (autre agent,
/// ou autre programme qui s'en serait emparé). Le gestionnaire tourne sur un thread du pool ; il ne doit
/// pas attendre l'arrêt du serveur, qui attendrait lui-même la fin du gestionnaire.
/// </summary>
public sealed class ControlServer : IDisposable
{
    private const int MaxBackoffMs = 1000;

    private readonly string _name;
    private readonly Func<AgentCommand, AgentReply> _handler;
    private readonly TimeSpan _clientTimeout;
    // Jamais libéré : sans minuterie, une source d'annulation ne tient aucune ressource.
    private readonly CancellationTokenSource _stop = new();
    private readonly Lock _gate = new();
    private NamedPipeServerStream? _pipe;
    private Task? _loop;

    /// <exception cref="IOException">Le tube existe déjà.</exception>
    public ControlServer(string pipeName, Func<AgentCommand, AgentReply> handler)
        : this(pipeName, handler, TimeSpan.FromSeconds(2))
    {
    }

    /// <param name="clientTimeout">Délai laissé à un client pour envoyer sa commande, puis pour lire la réponse.</param>
    internal ControlServer(string pipeName, Func<AgentCommand, AgentReply> handler, TimeSpan clientTimeout)
    {
        _name = pipeName;
        _handler = handler;
        _clientTimeout = clientTimeout;
        _pipe = Create(pipeName);
    }

    /// <summary>Faux une fois la boucle terminée : arrêt, ou tube perdu et impossible à recréer.</summary>
    public bool Running => Volatile.Read(ref _loop) is { IsCompleted: false };

    public void Start()
    {
        if (_loop is not null) throw new InvalidOperationException("Déjà démarré.");
        Volatile.Write(ref _loop, Task.Run(RunAsync));
    }

    /// <returns>Vrai si la boucle s'est arrêtée dans le délai (un gestionnaire en cours la retient).</returns>
    public bool Stop(TimeSpan timeout)
    {
        _stop.Cancel();
        try
        {
            return _loop is null || _loop.Wait(timeout);
        }
        catch (AggregateException)
        {
            return true;
        }
    }

    public void Dispose()
    {
        _ = Stop(TimeSpan.FromMilliseconds(500));
        // Une boucle encore retenue sort sur ObjectDisposedException à sa prochaine lecture.
        lock (_gate)
        {
            _pipe?.Dispose();
            _pipe = null;
        }
    }

    private static NamedPipeServerStream Create(string name) =>
        new(name, PipeDirection.InOut, 1, PipeTransmissionMode.Byte,
            PipeOptions.Asynchronous | PipeOptions.CurrentUserOnly | PipeOptions.FirstPipeInstance);

    private async Task RunAsync()
    {
        var token = _stop.Token;
        byte[] buffer = new byte[ControlProtocol.MaxLine];
        int failures = 0;
        while (!token.IsCancellationRequested)
        {
            NamedPipeServerStream? pipe;
            lock (_gate) pipe = _pipe;
            if (pipe is null) return;
            try
            {
                await pipe.WaitForConnectionAsync(token).ConfigureAwait(false);
                await ServeAsync(pipe, buffer, token).ConfigureAwait(false);
                failures = 0;
            }
            catch (OperationCanceledException) when (token.IsCancellationRequested)
            {
                return;
            }
            catch (ObjectDisposedException)
            {
                return;
            }
            catch (Exception)
            {
                // Client parti, trop lent, ou tube cassé avant même la connexion.
                failures++;
            }
            if (!Reset(pipe)) return;
            if (failures > 0)
            {
                try
                {
                    await Task.Delay(Math.Min(MaxBackoffMs, 25 << Math.Min(failures, 6)), token).ConfigureAwait(false);
                }
                catch (OperationCanceledException)
                {
                    return;
                }
            }
        }
    }

    /// <summary>
    /// Remet le tube en écoute. Un client parti avant la fin de la connexion laisse le tube dans un état
    /// que <c>Disconnect</c> refuse : il est alors recréé, sous le même nom.
    /// </summary>
    /// <returns>Faux si le tube est perdu (arrêt, ou nom repris entre-temps par un autre programme).</returns>
    private bool Reset(NamedPipeServerStream pipe)
    {
        try
        {
            pipe.Disconnect();
            return true;
        }
        catch (ObjectDisposedException)
        {
            return false;
        }
        catch (Exception e) when (e is IOException or InvalidOperationException)
        {
        }
        lock (_gate)
        {
            if (_pipe != pipe) return false;
            pipe.Dispose();
            try
            {
                _pipe = Create(_name);
                return true;
            }
            catch (Exception)
            {
                _pipe = null;
                return false;
            }
        }
    }

    private async Task ServeAsync(NamedPipeServerStream pipe, byte[] buffer, CancellationToken token)
    {
        int length;
        using (var read = CancellationTokenSource.CreateLinkedTokenSource(token))
        {
            read.CancelAfter(_clientTimeout);
            length = await ControlProtocol.ReadLineAsync(pipe, buffer, read.Token).ConfigureAwait(false);
        }
        // Ligne trop longue : le client écrit peut-être encore, une réponse pourrait l'attendre sans fin. On coupe.
        if (length < 0) return;
        var reply = ControlProtocol.TryParse(buffer.AsSpan(0, length), out AgentCommand command)
            ? Handle(command)
            : AgentReply.Unknown;
        // Délai à part pour la réponse : le temps du gestionnaire (écriture du lot pour flush) n'y compte pas.
        // Il ne dépend pas de l'arrêt : une réponse déjà calculée part même si Dispose est en cours.
        using var client = new CancellationTokenSource(_clientTimeout);
        await pipe.WriteAsync(ControlProtocol.Encode(reply), client.Token).ConfigureAwait(false);
        // Couper tout de suite effacerait la réponse pas encore lue : on attend que le client ferme.
        while (await pipe.ReadAsync(buffer, client.Token).ConfigureAwait(false) > 0)
        {
        }
    }

    private AgentReply Handle(AgentCommand command)
    {
        try
        {
            return _handler(command);
        }
        catch (Exception)
        {
            // Pas de message : il pourrait citer une valeur.
            return AgentReply.Failed;
        }
    }
}
