using KeyStats.Core.Input;

namespace KeyStats.Core.Analysis;

/// <summary>
/// Façon dont une majuscule a été obtenue : Maj de la main opposée à la lettre, Maj du même côté, ou Verr. Maj.
/// Les mains sont celles du doigté AZERTY standard (<see cref="KeyboardGeometry"/>), donc indicatives.
/// </summary>
internal static class CapitalShift
{
    /// <summary>
    /// Compteur journalier de la majuscule <paramref name="c"/> tapée sur <paramref name="key"/> ; null si ce n'en est pas une.
    /// </summary>
    public static DayMetric? Classify(ReadOnlySpan<bool> down, bool capsLock, ushort key, char c)
    {
        if (!char.IsLetter(c) || !char.IsUpper(c)) return null;
        bool left = down[KeyCode.LeftShift], right = down[KeyCode.RightShift];
        if (!left && !right) return capsLock ? DayMetric.CapsLockCapitals : null;
        if (!KeyboardGeometry.TryFind(key, out var placement) || placement.Finger == Finger.None) return null;
        bool opposite = placement.IsLeftHand ? right : left;
        return opposite ? DayMetric.ShiftOppositeCapitals : DayMetric.ShiftSameSideCapitals;
    }
}
