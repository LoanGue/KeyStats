using KeyStats.Core.Analysis;
using KeyStats.Core.Input;
using KeyStats.Core.Text;

namespace KeyStats.Core.Stats;

/// <summary>Parties de <c>/api/rhythm</c> lues dans les durées d'appui par touche (<see cref="HistogramKind.HoldMs"/>).</summary>
internal static class KeyLoad
{
    private static readonly string[] s_names =
        ["leftPinky", "leftRing", "leftMiddle", "leftIndex", "leftThumb", "rightThumb", "rightIndex", "rightMiddle", "rightRing", "rightPinky"];

    /// <summary>Durées d'appui des touches qui écrivent : les modificateurs, tenus exprès, fausseraient la médiane.</summary>
    public static TimeDistribution Hold(Dictionary<int, int[]> holds)
    {
        var counts = new int[LogScale.Buckets];
        foreach (var (key, h) in holds)
        {
            if (!Writes((ushort)key)) continue;
            for (int i = 0; i < counts.Length; i++) counts[i] += h[i];
        }
        return TimeDistribution.Of(LogScale.Hold, counts);
    }

    /// <summary>Appuis par doigt, toutes touches du plan comprises (Maj, Entrée, pavé…).</summary>
    public static List<RhythmFinger> Fingers(Dictionary<int, int[]> holds)
    {
        var presses = new long[s_names.Length];
        foreach (var (key, h) in holds)
        {
            var finger = KeyboardGeometry.FingerOf((ushort)key);
            if (finger == Finger.None) continue;
            foreach (int c in h) presses[(int)finger - 1] += c;
        }
        return [.. s_names.Select((name, i) => new RhythmFinger(name, presses[i]))];
    }

    private static bool Writes(ushort key) =>
        key == KeyCode.Space || LayoutTable.FrenchAzerty.Get(key, 0, out bool dead) != '\0' || dead;
}
