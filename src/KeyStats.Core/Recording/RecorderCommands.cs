using System.Collections.Concurrent;
using KeyStats.Core.Storage;

namespace KeyStats.Core.Recording;

public enum CommandKind : byte
{
    /// <summary>Pause extérieure (<see cref="RecorderCommand.Reason"/>) : la session en cours se termine.</summary>
    Pause = 1,
    Resume = 2,
    /// <summary>Le lot est écrit, puis la clé d'époque oubliée (verrouillage, veille).</summary>
    DropEpoch = 3,
    /// <summary>Nouveau fuseau (<see cref="RecorderCommand.Zone"/>), après <c>TimeZoneInfo.ClearCachedData</c>.</summary>
    SetZone = 4,
    /// <summary>Tout ce qui est en attente est écrit (fin de session Windows, mise en veille).</summary>
    Flush = 5,
}

/// <summary>Commande pour le <see cref="Recorder"/>, datée sur l'échelle des événements.</summary>
public readonly record struct RecorderCommand(long Ms, CommandKind Kind, PauseReason Reason = 0, TimeZoneInfo? Zone = null);

/// <summary>
/// Commandes de cycle de vie (verrouillage, veille, changement d'heure) envoyées par n'importe quel
/// thread au thread d'analyse. La <see cref="RingPump"/> les applique à leur place dans le temps :
/// avant la première frappe plus récente, après les frappes plus anciennes.
/// </summary>
public sealed class RecorderCommands
{
    private readonly ConcurrentQueue<RecorderCommand> _queue = new();
    private readonly Lock _postLock = new();
    private long _posted, _applied;

    /// <summary>
    /// Depuis n'importe quel thread. L'heure vient de <see cref="Input.EventClock.PeekMs"/>, et ne
    /// décroît pas d'une commande à la suivante : la file est lue dans l'ordre d'arrivée, et une
    /// commande datée trop loin retient celles qui la suivent. L'appelant réveille ensuite le consommateur.
    /// </summary>
    public void Post(in RecorderCommand command)
    {
        if (!Enum.IsDefined(command.Kind)) throw new ArgumentOutOfRangeException(nameof(command));
        if (command.Kind == CommandKind.Pause && (command.Reason == 0 || !Enum.IsDefined(command.Reason)))
            throw new ArgumentOutOfRangeException(nameof(command));
        if (command.Kind == CommandKind.SetZone) ArgumentNullException.ThrowIfNull(command.Zone);
        // Compte et file dans le même ordre, pour qu'« appliquées ≥ envoyées » vise bien ces commandes-là.
        lock (_postLock)
        {
            _queue.Enqueue(command);
            Volatile.Write(ref _posted, _posted + 1);
        }
    }

    public bool IsEmpty => _queue.IsEmpty;

    /// <summary>Commandes envoyées depuis la création.</summary>
    public long Posted => Volatile.Read(ref _posted);

    /// <summary>
    /// Commandes appliquées (ou comptées en erreur) par la pompe. Retirée de la file ne suffit pas :
    /// avant la veille, l'appelant attend que <c>Applied</c> rattrape le <see cref="Posted"/> relevé.
    /// </summary>
    public long Applied => Volatile.Read(ref _applied);

    /// <summary>Consommateur seulement, après l'application de la commande prise.</summary>
    internal void MarkApplied() => Volatile.Write(ref _applied, _applied + 1);

    /// <summary>Consommateur seulement : la commande suivante si elle ne date pas d'après <paramref name="beforeMs"/>.</summary>
    internal bool TryTake(long beforeMs, out RecorderCommand command)
    {
        if (_queue.TryPeek(out command) && command.Ms <= beforeMs) return _queue.TryDequeue(out command);
        command = default;
        return false;
    }
}
