using KeyStats.Core.Analysis;
using KeyStats.Core.Storage;
using KeyStats.Core.Web;

namespace KeyStats.Core.Stats;

/// <param name="Keys">Touches tenues puis touche finale, noms affichables (« Ctrl », « Maj », « C ») ; « Win » seul pour le menu Démarrer.</param>
public sealed record ShortcutUse(IReadOnlyList<string> Keys, long Count);

/// <param name="Keys">Nombre de touches enfoncées à l'appui, celle-ci comprise (1 : touche seule).</param>
public sealed record ChordCount(int Keys, long Presses);

/// <param name="Rolls">Appuis sur une touche de caractère avant le relâchement de la précédente.</param>
/// <param name="ShiftHeld">Majuscules faites en tenant Maj (Verr. Maj exclu).</param>
/// <param name="Shortcuts">Raccourcis avec Ctrl, Alt ou Win, Ctrl+Retour arrière compris, Win seul exclu.</param>
public sealed record OverlapSplit(long Rolls, long ShiftHeld, long Shortcuts);

/// <param name="First">Touche tenue.</param>
/// <param name="Second">Touche enfoncée avant son relâchement.</param>
/// <param name="MedianMs">Durée médiane du chevauchement de cette paire.</param>
public sealed record OverlapPair(string First, string Second, long Count, double MedianMs);

/// <param name="LastDay">Dernier jour (aaaa-mm-jj) de la période où la combinaison a servi ; null si jamais.</param>
public sealed record ComboCell(long Count, string? LastDay);

/// <param name="Modifier">Touche tenue : Ctrl, Alt, Ctrl + Alt (ou AltGr), Win.</param>
/// <param name="Cells">Une case par groupe de <see cref="Shortcuts.Groups"/>, dans le même ordre.</param>
public sealed record ComboRow(string Modifier, IReadOnlyList<ComboCell> Cells);

/// <param name="DayTablesUnfiltered">Vrai avec un filtre d'appli, de nature ou de clavier : toute la page vient des tables par jour, qui l'ignorent.</param>
/// <param name="Top">Les <see cref="ShortcutsApi.MaxShortcuts"/> raccourcis les plus utilisés, du plus fréquent au moins fréquent.</param>
/// <param name="Chords">Appuis selon le nombre de touches tenues, de 1 au dernier nombre observé (15 = 15 ou plus).</param>
/// <param name="Durations">Durée des chevauchements entre deux touches de caractère, cases logarithmiques de 5 ms à 1 s.</param>
/// <param name="Pairs">
/// Paires de touches de caractère qui se chevauchent le plus souvent, au moins <see cref="ShortcutsApi.MinPairCount"/> fois,
/// <see cref="ShortcutsApi.MaxPairs"/> au plus.
/// </param>
/// <param name="Groups">Groupes de touches en colonne de <see cref="Combos"/>.</param>
public sealed record Shortcuts(
    string From, string To, bool DayTablesUnfiltered, IReadOnlyList<ShortcutUse> Top, IReadOnlyList<ChordCount> Chords,
    OverlapSplit Overlap, TimeDistribution Durations, IReadOnlyList<OverlapPair> Pairs, IReadOnlyList<string> Groups,
    IReadOnlyList<ComboRow> Combos);

/// <summary>
/// Route <c>/api/shortcuts</c> : raccourcis les plus utilisés, touches tenues ensemble, chevauchements
/// (nature, durée, paires) et combinaisons de chaque modificateur avec chaque groupe de touches.
/// </summary>
public static class ShortcutsApi
{
    public const string Path = "/api/shortcuts";
    public const int MaxShortcuts = 8;
    public const int MaxPairs = 5;
    public const int MinPairCount = 5;

    public static void Register(WebRouter router, StatsContext stats)
    {
        ArgumentNullException.ThrowIfNull(router);
        ArgumentNullException.ThrowIfNull(stats);
        router.Get(Path, q => stats.Respond(q, f => Read(stats.Db, f), StatsJson.Default.Shortcuts));
    }

    public static Shortcuts Read(KeyStatsDb db, StatsFilter filter)
    {
        ArgumentNullException.ThrowIfNull(db);
        ArgumentNullException.ThrowIfNull(filter);
        var range = filter.Range;
        var rows = ShortcutTables.Rows(db, range);
        var m = TypoDayTables.Metrics(db, range);
        long wordBackspaces = m.GetValueOrDefault(DayMetric.WordBackspaces);
        var split = new OverlapSplit(m.GetValueOrDefault(DayMetric.Overlaps),
            m.GetValueOrDefault(DayMetric.ShiftOppositeCapitals) + m.GetValueOrDefault(DayMetric.ShiftSameSideCapitals),
            ShortcutTables.HeldCount(rows) + wordBackspaces);
        var overlaps = DayHistograms.BySub(db, range, HistogramKind.OverlapMs);
        return new Shortcuts(StatsFilter.Iso(range.FromDay), StatsFilter.Iso(range.ToDay), filter.HasSessionFilter,
            ShortcutTables.Top(rows, MaxShortcuts), ShortcutTables.Chords(db, range), split,
            ShortcutTables.Durations(overlaps), ShortcutTables.Pairs(overlaps, MinPairCount, MaxPairs), ShortcutTables.GroupNames,
            ShortcutTables.Combos(rows, wordBackspaces > 0 ? (wordBackspaces, ShortcutTables.LastDay(db, range, DayMetric.WordBackspaces)) : null));
    }
}
