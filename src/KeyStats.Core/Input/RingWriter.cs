namespace KeyStats.Core.Input;

/// <summary>
/// Côté producteur de l'<see cref="EventRing"/>. Après une perte, le premier événement écrit est
/// précédé d'une remise à zéro : elle tombe exactement à la place du trou, là où un relâchement a pu
/// disparaître. Sans place pour les deux, l'événement est perdu lui aussi et la remise reste due.
/// Les marqueurs de fenêtre, écrits par le <see cref="Focus.FocusChannel"/>, peuvent se glisser entre
/// le trou et la remise : sans effet, un changement de fenêtre ne touche pas l'état des touches.
/// </summary>
public sealed class RingWriter(EventRing ring, Func<LockKeys> readLocks)
{
    private bool _resetOwed;

    public bool ResetOwed => _resetOwed;

    /// <summary>Événement perdu ailleurs (marqueur de fenêtre sans place) : la remise à zéro est due.</summary>
    public void MarkLost() => _resetOwed = true;

    public bool Write(in InputEvent e)
    {
        if (_resetOwed)
        {
            if (ring.Capacity - ring.Count < 2)
            {
                ring.CountDropped();
                return false;
            }
            ring.TryWrite(new InputEvent(e.Ms, EventKind.Reset, 0, 0, (byte)readLocks()));
            _resetOwed = false;
        }
        if (ring.TryWrite(e)) return true;
        _resetOwed = true;
        return false;
    }
}
