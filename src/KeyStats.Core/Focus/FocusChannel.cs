using System.Collections.Concurrent;
using KeyStats.Core.Input;

namespace KeyStats.Core.Focus;

/// <summary>
/// Passe les changements de fenêtre du thread de capture au thread d'analyse sans perdre leur place
/// parmi les frappes : le changement va dans une file à part, et un événement
/// <see cref="EventKind.Focus"/> qui porte son numéro va dans la file des événements.
/// Si ce marqueur est refusé (file pleine), le changement est sauté au marqueur suivant ;
/// <see cref="TakeLatest"/> sert après une perte, quand l'ordre n'est de toute façon plus garanti.
/// </summary>
public sealed class FocusChannel
{
    private readonly ConcurrentQueue<FocusChange> _queue = new();
    private long _nextSeq = 1;

    /// <summary>Producteur seulement. Faux si le marqueur n'a pas trouvé de place.</summary>
    public bool Publish(FocusChange change, EventRing ring)
    {
        change.Seq = _nextSeq++;
        _queue.Enqueue(change);
        return ring.TryWrite(new InputEvent(change.Ms, EventKind.Focus, (ushort)change.Seq));
    }

    /// <summary>Consommateur seulement : le changement de ce marqueur, ceux qui le précèdent étant écartés.</summary>
    public FocusChange? Take(in InputEvent marker)
    {
        while (_queue.TryPeek(out var head))
        {
            // Comparaison sur 16 bits, modulo : la tête n'a jamais 32 768 changements d'avance.
            short ahead = (short)((ushort)head.Seq - marker.Key);
            if (ahead > 0) return null; // marqueur déjà servi
            _queue.TryDequeue(out _);
            if (ahead == 0) return head;
        }
        return null;
    }

    /// <summary>Consommateur seulement : vide la file et rend le dernier changement.</summary>
    public FocusChange? TakeLatest()
    {
        FocusChange? last = null;
        while (_queue.TryDequeue(out var c)) last = c;
        return last;
    }
}
