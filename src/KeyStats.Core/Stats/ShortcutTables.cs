using KeyStats.Core.Analysis;
using KeyStats.Core.Input;
using KeyStats.Core.Storage;

namespace KeyStats.Core.Stats;

/// <summary>Un raccourci de <c>day_shortcut</c> sommé sur la période, avec son dernier jour d'usage.</summary>
internal readonly record struct ShortcutRow(ShortcutModifiers Mods, ushort Key, long Count, int LastDay)
{
    /// <summary>Win appuyée et relâchée seule (menu Démarrer).</summary>
    public bool WinAlone => Key is KeyCode.LeftWin or KeyCode.RightWin;
}

/// <summary>Parties de <c>/api/shortcuts</c> lues dans les tables par jour, sans filtre de session.</summary>
internal static class ShortcutTables
{
    private const string InRange = "day >= $from AND day <= $to";

    public static readonly string[] GroupNames =
        ["Lettres", "Chiffres", "Flèches", "F1 – F12", "Retour arr.", "Suppr", "Entrée", "Tab", "Espace"];

    private const int BackspaceGroup = 4;

    private static readonly (string Name, Func<ShortcutModifiers, bool> Holds)[] s_modifiers =
    [
        ("Ctrl", m => m.HasFlag(ShortcutModifiers.Ctrl) && !m.HasFlag(ShortcutModifiers.Alt)),
        ("Alt", m => m.HasFlag(ShortcutModifiers.Alt) && !m.HasFlag(ShortcutModifiers.Ctrl)),
        ("Ctrl + Alt", m => m.HasFlag(ShortcutModifiers.Ctrl | ShortcutModifiers.Alt)),
        ("Win", m => m.HasFlag(ShortcutModifiers.Win)),
    ];

    public static List<ShortcutRow> Rows(KeyStatsDb db, DayRange range)
    {
        using var cmd = db.Command(
            $"SELECT mods, key, TOTAL(n), MAX(day) FROM day_shortcut WHERE {InRange} AND n > 0 GROUP BY mods, key",
            range.FromDay, range.ToDay);
        using var r = cmd.ExecuteReader();
        var list = new List<ShortcutRow>();
        while (r.Read()) list.Add(new ShortcutRow((ShortcutModifiers)r.GetInt32(0), (ushort)r.GetInt32(1), (long)r.GetDouble(2), r.GetInt32(3)));
        return list;
    }

    /// <summary>Raccourcis faits en tenant une touche : tous sauf Win seul.</summary>
    public static long HeldCount(List<ShortcutRow> rows) => rows.Where(r => !r.WinAlone).Sum(r => r.Count);

    /// <summary>Touches du raccourci dans l'ordre d'usage : Win, Ctrl, Alt, Maj, puis la touche finale.</summary>
    public static List<string> Keys(ShortcutRow row)
    {
        var keys = new List<string>(4);
        if (row.Mods.HasFlag(ShortcutModifiers.Win)) keys.Add("Win");
        if (row.Mods.HasFlag(ShortcutModifiers.Ctrl)) keys.Add("Ctrl");
        if (row.Mods.HasFlag(ShortcutModifiers.Alt)) keys.Add("Alt");
        if (row.Mods.HasFlag(ShortcutModifiers.Shift)) keys.Add("Maj");
        if (!row.WinAlone) keys.Add(KeyCode.ShortName(row.Key));
        return keys;
    }

    /// <summary>Les plus utilisés ; deux touches de même nom (Win gauche et droite) ne font qu'un raccourci.</summary>
    public static List<ShortcutUse> Top(List<ShortcutRow> rows, int max) =>
        [.. rows.Select(r => (Keys: Keys(r), r.Count))
            .GroupBy(x => string.Join('+', x.Keys), StringComparer.Ordinal)
            .Select(g => new ShortcutUse(g.First().Keys, g.Sum(x => x.Count)))
            .OrderByDescending(s => s.Count).ThenBy(s => string.Join('+', s.Keys), StringComparer.Ordinal)
            .Take(max)];

    public static List<ChordCount> Chords(KeyStatsDb db, DayRange range)
    {
        var counts = DayHistograms.Sum(db, range, HistogramKind.Chord);
        int last = counts.AsSpan().LastIndexOfAnyExcept(0);
        var list = new List<ChordCount>();
        for (int keys = 1; keys <= last; keys++) list.Add(new ChordCount(keys, counts[keys]));
        return list;
    }

    public static TimeDistribution Durations(Dictionary<int, int[]> overlaps)
    {
        var sum = new int[LogScale.Buckets];
        foreach (var counts in overlaps.Values)
            for (int i = 0; i < sum.Length; i++) sum[i] += counts[i];
        return TimeDistribution.Of(LogScale.Overlap, sum);
    }

    public static List<OverlapPair> Pairs(Dictionary<int, int[]> overlaps, int minCount, int max) =>
        [.. overlaps.Select(kv => (kv.Key, Counts: kv.Value, Total: kv.Value.Sum(c => (long)c)))
            .Where(p => p.Total >= minCount)
            .OrderByDescending(p => p.Total).ThenBy(p => p.Key)
            .Take(max)
            .Select(p =>
            {
                var (first, second) = OverlapTracker.Pair(p.Key);
                return new OverlapPair(KeyCode.Name(first), KeyCode.Name(second), p.Total, LogScale.Overlap.Median(p.Counts));
            })];

    /// <summary>Groupe de <see cref="GroupNames"/> de la touche, -1 hors groupe.</summary>
    public static int Group(ushort key)
    {
        switch (key)
        {
            case KeyCode.Up or KeyCode.Down or KeyCode.Left or KeyCode.Right: return 2;
            case KeyCode.Backspace: return BackspaceGroup;
            case KeyCode.Delete: return 5;
            case KeyCode.Enter or KeyCode.NumEnter: return 6;
            case KeyCode.Tab: return 7;
            case KeyCode.Space: return 8;
        }
        if (KeyCode.IsFunctionKey(key)) return 3;
        if (key is >= 0x02 and <= 0x0B || KeyCode.NumpadDigit(key) >= 0) return 1;
        string name = KeyCode.Name(key);
        return name.Length == 1 && char.IsAsciiLetterUpper(name[0]) ? 0 : -1;
    }

    /// <param name="wordBackspace">Ctrl+Retour arrière, compté à part par l'analyse : nombre et dernier jour.</param>
    public static List<ComboRow> Combos(List<ShortcutRow> rows, (long Count, int Day)? wordBackspace)
    {
        var cells = new (long Count, int Last)[s_modifiers.Length, GroupNames.Length];
        foreach (var row in rows)
        {
            int g = Group(row.Key);
            if (g < 0) continue;
            for (int m = 0; m < s_modifiers.Length; m++)
            {
                if (!s_modifiers[m].Holds(row.Mods)) continue;
                cells[m, g].Count += row.Count;
                cells[m, g].Last = Math.Max(cells[m, g].Last, row.LastDay);
            }
        }
        if (wordBackspace is var (n, day))
        {
            cells[0, BackspaceGroup].Count += n;
            cells[0, BackspaceGroup].Last = Math.Max(cells[0, BackspaceGroup].Last, day);
        }
        return [.. s_modifiers.Select((mod, m) => new ComboRow(mod.Name, [.. Enumerable.Range(0, GroupNames.Length).Select(g =>
            new ComboCell(cells[m, g].Count, cells[m, g].Count > 0 ? StatsFilter.Iso(cells[m, g].Last) : null))]))];
    }

    /// <summary>Dernier jour de la période où <paramref name="metric"/> est non nul ; 0 si aucun.</summary>
    public static int LastDay(KeyStatsDb db, DayRange range, DayMetric metric)
    {
        using var cmd = db.Command($"SELECT MAX(day) FROM day_metric WHERE {InRange} AND metric = {(int)metric} AND value > 0",
            range.FromDay, range.ToDay);
        return cmd.ExecuteScalar() is long day ? (int)day : 0;
    }
}
