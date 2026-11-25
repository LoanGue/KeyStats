using System.Runtime.InteropServices;

namespace KeyStats.Core.Input;

/// <summary>Nature d'un événement d'entrée, tel qu'il est enregistré dans le flux brut.</summary>
public enum EventKind : byte
{
    KeyDown = 1,
    KeyUp = 2,
    /// <summary>Bouton souris ou pavé tactile enfoncé. <see cref="InputEvent.Key"/> = numéro du bouton.</summary>
    MouseDown = 3,
    MouseUp = 4,
    /// <summary>
    /// Remise à zéro de l'état des touches (démarrage, déverrouillage, réveil, changement de bureau).
    /// <see cref="InputEvent.Flags"/> porte l'état relu des verrous (<see cref="LockKeys"/>).
    /// </summary>
    Reset = 5,
    /// <summary>Touche vue relâchée par le système alors qu'on la croyait enfoncée (relâchement perdu).</summary>
    ForcedUp = 6,
    /// <summary>
    /// Interne à l'agent, jamais écrit dans le brut : un changement de fenêtre attend dans le
    /// <see cref="KeyStats.Core.Focus.FocusChannel"/>, à cette place dans l'ordre des frappes.
    /// <see cref="InputEvent.Key"/> porte les 16 bits bas de son numéro.
    /// </summary>
    Focus = 7,
}

/// <summary>État des verrous clavier, porté par les événements <see cref="EventKind.Reset"/>.</summary>
[Flags]
public enum LockKeys : byte
{
    None = 0,
    CapsLock = 1,
    NumLock = 2,
}

/// <summary>Boutons souris (champ <see cref="InputEvent.Key"/> des événements souris).</summary>
public static class MouseButton
{
    public const ushort Left = 1, Right = 2, Middle = 3, X1 = 4, X2 = 5;
}

/// <summary>
/// Un événement d'entrée horodaté à la milliseconde (UTC, époque Unix). 16 octets, sans référence :
/// c'est la structure qui circule dans la file de l'agent, dans l'analyse et dans le format brut.
/// </summary>
[StructLayout(LayoutKind.Sequential)]
public struct InputEvent
{
    public long Ms;
    /// <summary>Touche physique (<see cref="KeyCode"/>) ou bouton souris.</summary>
    public ushort Key;
    /// <summary>Identifiant local du clavier ou de la souris d'origine (table <c>device</c>), 0 si inconnu.</summary>
    public ushort Device;
    public EventKind Kind;
    public byte Flags;

    public InputEvent(long ms, EventKind kind, ushort key, ushort device = 0, byte flags = 0)
    {
        Ms = ms;
        Kind = kind;
        Key = key;
        Device = device;
        Flags = flags;
    }

    public readonly bool IsKeyboard => Kind is EventKind.KeyDown or EventKind.KeyUp or EventKind.ForcedUp;

    /// <summary>Sans le nom de la touche : une trace ne doit pas révéler ce qui a été tapé.</summary>
    public override readonly string ToString() => $"{Ms} {Kind} d{Device} f{Flags}";
}
