using KeyStats.Core.Analysis;
using KeyStats.Core.Crypto;
using KeyStats.Core.Storage;

namespace KeyStats.Core.Recording;

/// <summary>Issue du rejeu d'un groupe de jours.</summary>
public enum DayReplay
{
    /// <summary>Statistiques des jours effacées puis recalculées depuis le brut.</summary>
    Done,

    /// <summary>Un jour du groupe a des frappes comptées avant le premier brut chiffré : statistiques gardées telles quelles.</summary>
    NoRaw,

    /// <summary>Le brut relu compte moins de touches que les statistiques (brut perdu à l'écriture) : rejeu annulé, statistiques gardées.</summary>
    Incomplete,

    /// <summary>
    /// Des sessions enregistrées relient plus de <see cref="HistoryReplay.MaxSpreadDays"/> jours de suite :
    /// le groupe n'a pas de bord libre, rien n'est rejoué.
    /// </summary>
    TooWide,

    /// <summary>Le groupe touche un jour que l'agent écrit encore (aujourd'hui) : rien n'est rejoué.</summary>
    Current,
}

/// <param name="Outcome">Issue du rejeu.</param>
/// <param name="FromDay">Premier jour du groupe rejoué (aaaammjj).</param>
/// <param name="ToDay">Dernier jour du groupe rejoué.</param>
public sealed record ReplayResult(DayReplay Outcome, int FromDay, int ToDay);

/// <summary>
/// Recalcul de l'historique : rejoue le brut chiffré d'un jour local dans un <see cref="Analyzer"/> neuf,
/// avec les réglages donnés (seuils actuels), et remplace les statistiques dans une seule transaction.
/// Une session est rangée dans le jour où elle commence : un jour est donc rejoué avec ses voisins tant
/// qu'une session enregistrée traverse le minuit qui les sépare. Les frappes enregistrées ne changent pas.
/// Les touches des applis exclues et des champs secrets n'ont pas de brut : leurs compteurs sont gardés
/// (<see cref="StatsWriter.DeleteDays"/>).
/// </summary>
public sealed class HistoryReplay
{
    /// <summary>
    /// Un bloc brut s'étend sur une minute environ (écriture toutes les 60 s) : on relit aussi ceux commencés
    /// un peu avant le groupe, pour garder leurs frappes d'après minuit, et on écarte celles d'avant.
    /// </summary>
    public const long BlockMarginMs = 10 * 60_000;

    /// <summary>Jours ajoutés au plus de chaque côté pour suivre une session à cheval sur minuit.</summary>
    public const int MaxSpreadDays = 7;

    private readonly KeyStatsDb _db;
    private readonly LocalDayClock _clock;
    private readonly AnalysisSettings _settings;
    private readonly int _maxSpreadDays;

    public HistoryReplay(KeyStatsDb db, LocalDayClock clock, AnalysisSettings settings)
        : this(db, clock, settings, MaxSpreadDays)
    {
    }

    internal HistoryReplay(KeyStatsDb db, LocalDayClock clock, AnalysisSettings settings, int maxSpreadDays)
    {
        ArgumentNullException.ThrowIfNull(db);
        ArgumentNullException.ThrowIfNull(clock);
        ArgumentNullException.ThrowIfNull(settings);
        _db = db;
        _clock = clock;
        _settings = settings;
        _maxSpreadDays = maxSpreadDays;
    }

    /// <summary>
    /// Rejoue le jour local <paramref name="day"/> (aaaammjj), avec les jours que ses sessions touchent. Un groupe
    /// qui atteint <paramref name="beforeDay"/> (le jour en cours) n'est pas rejoué : l'agent y écrit encore.
    /// </summary>
    public ReplayResult Replay(int day, MasterKey master, int beforeDay = int.MaxValue)
    {
        ArgumentNullException.ThrowIfNull(master);
        var (fromDay, toDay, bounded) = GroupOf(day);
        if (!bounded) return new(DayReplay.TooWide, fromDay, toDay);
        if (toDay >= beforeDay) return new(DayReplay.Current, fromDay, toDay);
        for (var d = LocalDayClock.DateOf(fromDay); LocalDayClock.KeyOf(d) <= toDay; d = d.AddDays(1))
        {
            if (!HasRawFor(LocalDayClock.KeyOf(d))) return new(DayReplay.NoRaw, fromDay, toDay);
        }
        using var tx = _db.BeginTransaction();
        long fromMs = Midnight(fromDay, 0), toMs = Midnight(toDay, 1);
        long keys = Keys(fromMs, toMs);
        var writer = new StatsWriter(_db);
        writer.DeleteDays(fromDay, toDay, _clock);
        var analyzer = new Analyzer(new StatsAccumulator(_clock), _settings);
        var focus = _db.GetFocus(fromMs - BlockMarginMs, toMs);
        int next = 0;
        using (var reader = new RawArchiveReader(_db, master))
        {
            foreach (var block in _db.GetRawBlocks(fromMs - BlockMarginMs, toMs))
            {
                if (block.EndMs < fromMs) continue;
                // Les fenêtres ouvertes jusqu'à celle du bloc, dans l'ordre où l'agent les a vues.
                while (next < focus.Count && (focus[next].Ms <= block.StartMs || focus[next].Id <= block.FocusId))
                    analyzer.SetFocus(Info(focus[next++]));
                foreach (var e in reader.ReadBlock(block))
                {
                    if (e.Ms >= fromMs && e.Ms < toMs) analyzer.Process(e);
                }
            }
        }
        while (next < focus.Count) analyzer.SetFocus(Info(focus[next++]));
        analyzer.Tick(toMs);
        analyzer.EndAll();
        writer.Write(analyzer.Stats);
        // Transaction abandonnée sans validation : tout revient comme avant.
        if (Keys(fromMs, toMs) < keys) return new(DayReplay.Incomplete, fromDay, toDay);
        tx.Commit();
        return new(DayReplay.Done, fromDay, toDay);
    }

    /// <summary>
    /// Le jour et ses voisins reliés par une session enregistrée qui traverse minuit. <c>Bounded</c> est faux
    /// si une session traverse encore un bord une fois le plafond atteint.
    /// </summary>
    public (int FromDay, int ToDay, bool Bounded) GroupOf(int day)
    {
        var from = LocalDayClock.DateOf(day);
        var to = from;
        for (int i = 0; i < _maxSpreadDays && Crosses(_clock.MidnightUtcMs(from)); i++) from = from.AddDays(-1);
        for (int i = 0; i < _maxSpreadDays && Crosses(_clock.MidnightUtcMs(to.AddDays(1))); i++) to = to.AddDays(1);
        bool bounded = !Crosses(_clock.MidnightUtcMs(from)) && !Crosses(_clock.MidnightUtcMs(to.AddDays(1)));
        return (LocalDayClock.KeyOf(from), LocalDayClock.KeyOf(to), bounded);
    }

    private bool Crosses(long midnightMs)
    {
        using var cmd = _db.Command("SELECT 1 FROM session WHERE start_ms < $m AND end_ms > $m LIMIT 1", midnightMs);
        return cmd.ExecuteScalar() is not null;
    }

    private long Keys(long fromMs, long toMs)
    {
        using var cmd = _db.Command("SELECT coalesce(sum(keys), 0) FROM minute_total WHERE ts_min >= $a AND ts_min < $b",
            LocalDayClock.MinuteOf(fromMs), LocalDayClock.MinuteOf(toMs));
        return (long)cmd.ExecuteScalar()!;
    }

    /// <summary>
    /// Faux si le jour a des frappes comptées avant le premier bloc brut (chiffrement mis en place plus
    /// tard) : effacer ce jour perdrait ces frappes, que rien ne permet de refaire.
    /// </summary>
    private bool HasRawFor(int day)
    {
        if (_db.Scalar("SELECT min(start_ms) FROM raw_block") is not long firstRawMs) return false;
        long dayStartMs = Midnight(day, 0);
        return firstRawMs <= dayStartMs || Keys(dayStartMs, Math.Min(firstRawMs, Midnight(day, 1))) == 0;
    }

    private long Midnight(int day, int addDays) => _clock.MidnightUtcMs(LocalDayClock.DateOf(day).AddDays(addDays));

    private FocusInfo Info(StoredFocus f) =>
        new(f.Ms, f.AppId, f.TitleId ?? 0, f.LayoutId is int id ? _db.GetLayout(id) : null, f.Flags);
}
