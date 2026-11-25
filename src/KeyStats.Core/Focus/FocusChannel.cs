using System.Collections.Concurrent;
using KeyStats.Core.Input;

namespace KeyStats.Core.Focus;

/// <summary>
/// Passe les changements de fenêtre du thread de capture au thread d'analyse sans perdre leur place
/// parmi les frappes : le changement va dans une file à part, et un événement
/// <see cref="EventKind.Focus"/> qui porte son numéro va dans la file des événements.
/// Si ce marqueur est refusé (file pleine), le changement reste en attente, et le producteur le
/// republie avant d'écrire la frappe suivante : aucune frappe n'arrive sous la fenêtre précédente.
/// </summary>
public sealed class FocusChannel
{
    /// <summary>Au-delà, le consommateur est bloqué : on garde le plus ancien, la suite est perdue.</summary>
    public const int MaxQueued = 4096;

    private readonly ConcurrentQueue<FocusChange> _queue = new();
    private long _nextSeq = 1;
    private FocusChange? _pending;

    /// <summary>Producteur seulement : un changement attend encore son marqueur.</summary>
    public bool HasPending => _pending is not null;

    /// <summary>Producteur seulement. Faux si le marqueur n'a pas trouvé de place, ou si la file est pleine.</summary>
    public bool Publish(FocusChange change, EventRing ring)
    {
        if (_queue.Count >= MaxQueued) return false;
        change.Seq = _nextSeq++;
        _queue.Enqueue(change);
        _pending = change; // remplace un changement plus ancien resté sans marqueur, écarté au Take
        return RepublishPending(ring);
    }

    /// <summary>Producteur seulement, avant chaque écriture tant que <see cref="HasPending"/> est vrai.</summary>
    public bool RepublishPending(EventRing ring)
    {
        if (_pending is not { } p) return true;
        if (!ring.TryWrite(new InputEvent(p.Ms, EventKind.Focus, (ushort)p.Seq))) return false;
        _pending = null;
        return true;
    }

    /// <summary>Consommateur seulement : le changement de ce marqueur, ceux qui le précèdent étant écartés.</summary>
    public FocusChange? Take(in InputEvent marker)
    {
        while (_queue.TryPeek(out var head))
        {
            // Comparaison sur 16 bits, modulo : la file est bornée bien en deçà de 32 768.
            short ahead = (short)((ushort)head.Seq - marker.Key);
            if (ahead > 0) return null; // marqueur déjà servi
            _queue.TryDequeue(out _);
            if (ahead == 0) return head;
        }
        return null;
    }
}
