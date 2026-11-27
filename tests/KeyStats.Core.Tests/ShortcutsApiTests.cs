using KeyStats.Core.Analysis;
using KeyStats.Core.Input;
using KeyStats.Core.Stats;
using KeyStats.Core.Text;
using KeyStats.Core.Web;

namespace KeyStats.Core.Tests;

public sealed class ShortcutsApiTests : IDisposable
{
    private const int Today = 20261127;
    private const ShortcutModifiers Ctrl = ShortcutModifiers.Ctrl, Alt = ShortcutModifiers.Alt, Shift = ShortcutModifiers.Shift,
        Win = ShortcutModifiers.Win;
    private readonly TempDb _temp = new();

    public ShortcutsApiTests()
    {
        AddShortcut(20261126, Ctrl, KeyOf('c'), 40);
        AddShortcut(20261125, Ctrl, KeyOf('c'), 10);
        AddShortcut(20261126, Ctrl, KeyOf('v'), 45);
        AddShortcut(20261124, Ctrl | Shift, KeyCode.Tab, 6);
        AddShortcut(20261126, Alt, KeyCode.Tab, 12);
        AddShortcut(20261125, Ctrl | Alt, KeyCode.Delete, 1);
        AddShortcut(20261126, Win, KeyCode.LeftWin, 3);
        AddShortcut(20261125, Win, KeyCode.RightWin, 2);
        AddShortcut(20261126, Win | Shift, KeyOf('s'), 4);
        AddShortcut(20261126, Ctrl, KeyCode.Left, 8);
        AddShortcut(20261118, Ctrl, KeyOf('z'), 999);

        AddMetric(20261126, DayMetric.Overlaps, 60);
        AddMetric(20261126, DayMetric.ShiftOppositeCapitals, 7);
        AddMetric(20261125, DayMetric.ShiftSameSideCapitals, 3);
        AddMetric(20261126, DayMetric.CapsLockCapitals, 50);
        AddMetric(20261124, DayMetric.WordBackspaces, 5);
        AddMetric(20261120, DayMetric.WordBackspaces, 9);

        int[] chord = new int[StatsAccumulator.ChordBuckets];
        (chord[1], chord[2], chord[3], chord[5]) = (900, 80, 15, 1);
        AddHist(20261126, HistogramKind.Chord, 0, chord);
        AddOverlap(20261126, 'e', 's', 30, 8);
        AddOverlap(20261125, 'e', 's', 90, 1);
        AddOverlap(20261126, 'o', 'u', 40, 6);
        AddOverlap(20261126, 'a', 'z', 20, 2);
    }

    private static ushort KeyOf(char c) =>
        LayoutTable.FrenchAzerty.TryFindKey(c, out ushort key, out _) ? key : throw new ArgumentException(c.ToString());

    private void AddShortcut(int day, ShortcutModifiers mods, ushort key, int n) =>
        _temp.Db.Exec($"INSERT INTO day_shortcut VALUES ({day}, {(int)mods}, {key}, {n})");

    private void AddMetric(int day, DayMetric metric, long value) =>
        _temp.Db.Exec($"INSERT INTO day_metric VALUES ({day}, {(int)metric}, {value})");

    private void AddHist(int day, HistogramKind kind, int sub, int[] counts) =>
        _temp.Db.Exec($"INSERT INTO day_hist VALUES ({day}, {(int)kind}, {sub}, X'{Convert.ToHexString(HistogramCodec.Encode(counts))}')");

    private void AddOverlap(int day, char first, char second, double ms, int n)
    {
        var counts = new int[LogScale.Buckets];
        counts[LogScale.Overlap.Bucket(ms)] = n;
        AddHist(day, HistogramKind.OverlapMs, OverlapTracker.Sub(KeyOf(first), KeyOf(second)), counts);
    }

    private Shortcuts Read(string query) => ShortcutsApi.Read(_temp.Db, StatsFilter.Parse(WebQuery.Parse(query), Today, 20261101)!);

    [Fact]
    public void Raccourcis_les_plus_utilises_win_gauche_et_droite_reunis()
    {
        var s = Read("period=week");
        Assert.Equal(("2026-11-21", "2026-11-27", false), (s.From, s.To, s.DayTablesUnfiltered));
        // La semaine d'avant (Ctrl+Z) ne compte pas ; Ctrl+V passe avant Ctrl+C, sommé sur deux jours.
        Assert.Equal(
            ["Ctrl+C:50", "Ctrl+V:45", "Alt+Tab:12", "Ctrl+Gauche:8", "Ctrl+Maj+Tab:6", "Win:5", "Win+Maj+S:4", "Ctrl+Alt+Suppr:1"],
            s.Top.Select(t => string.Join('+', t.Keys) + ":" + t.Count));
    }

    [Fact]
    public void Touches_tenues_ensemble_et_nature_des_chevauchements()
    {
        var s = Read("period=week");
        Assert.Equal([(1, 900L), (2, 80L), (3, 15L), (4, 0L), (5, 1L)], s.Chords.Select(c => (c.Keys, c.Presses)));
        // Raccourcis : tous sauf Win seul (5), plus Ctrl+Retour arrière de la semaine (5). Verr. Maj ne compte pas.
        Assert.Equal(new OverlapSplit(60, 10, 126 + 5), s.Overlap);
    }

    [Fact]
    public void Paires_qui_se_chevauchent_le_plus_avec_leur_mediane()
    {
        var s = Read("period=week");
        // « az » (2 fois) est sous le seuil.
        Assert.Equal([("E", "S", 9L), ("O", "U", 6L)], s.Pairs.Select(p => (p.First, p.Second, p.Count)));
        Assert.Equal(LogScale.Overlap.Middle(LogScale.Overlap.Bucket(30)), s.Pairs[0].MedianMs, 6);
        Assert.Equal(17, s.Durations.Bins.Sum(b => b.Count));
        Assert.Equal(LogScale.Overlap.Middle(LogScale.Overlap.Bucket(30)), s.Durations.MedianMs!.Value, 6);
    }

    [Fact]
    public void Combinaisons_par_modificateur_et_groupe_de_touches()
    {
        var s = Read("period=week");
        Assert.Equal(ShortcutTables.GroupNames, s.Groups);
        Assert.Equal(["Ctrl", "Alt", "Ctrl + Alt", "Win"], s.Combos.Select(r => r.Modifier));
        ComboCell Cell(string mod, string group) =>
            s.Combos.Single(r => r.Modifier == mod).Cells[Array.IndexOf(ShortcutTables.GroupNames, group)];

        Assert.Equal(new ComboCell(95, "2026-11-26"), Cell("Ctrl", "Lettres"));
        Assert.Equal(new ComboCell(8, "2026-11-26"), Cell("Ctrl", "Flèches"));
        Assert.Equal(new ComboCell(6, "2026-11-24"), Cell("Ctrl", "Tab"));
        Assert.Equal(new ComboCell(5, "2026-11-24"), Cell("Ctrl", "Retour arr."));
        Assert.Equal(new ComboCell(12, "2026-11-26"), Cell("Alt", "Tab"));
        Assert.Equal(new ComboCell(1, "2026-11-25"), Cell("Ctrl + Alt", "Suppr"));
        Assert.Equal(new ComboCell(0, null), Cell("Ctrl", "Suppr"));
        // Win seul n'entre dans aucun groupe ; Win+Maj+S est une lettre.
        Assert.Equal(new ComboCell(4, "2026-11-26"), Cell("Win", "Lettres"));
        Assert.Equal(4, s.Combos.Single(r => r.Modifier == "Win").Cells.Sum(c => c.Count));
    }

    [Fact]
    public void Groupes_de_touches()
    {
        Assert.Equal(0, ShortcutTables.Group(KeyOf('q')));
        Assert.Equal(-1, ShortcutTables.Group(KeyOf('ù')));
        Assert.Equal(1, ShortcutTables.Group(0x02));
        Assert.Equal(1, ShortcutTables.Group(KeyCode.Num0));
        Assert.Equal(3, ShortcutTables.Group(KeyCode.F12));
        Assert.Equal(6, ShortcutTables.Group(KeyCode.NumEnter));
        Assert.Equal(-1, ShortcutTables.Group(KeyCode.Home));
    }

    [Fact]
    public void Periode_vide()
    {
        var s = Read("period=day");
        Assert.Empty(s.Top);
        Assert.Empty(s.Chords);
        Assert.Empty(s.Pairs);
        Assert.Null(s.Durations.MedianMs);
        Assert.Equal(new OverlapSplit(0, 0, 0), s.Overlap);
        Assert.All(s.Combos, r => Assert.All(r.Cells, c => Assert.Equal(new ComboCell(0, null), c)));
    }

    [Fact]
    public void Filtre_de_session_signale()
    {
        Assert.True(Read("period=week&app=3").DayTablesUnfiltered);
    }

    public void Dispose() => _temp.Dispose();
}
