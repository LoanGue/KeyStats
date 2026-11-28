using KeyStats.Core.Analysis;
using KeyStats.Core.Crypto;
using KeyStats.Core.Storage;

namespace KeyStats.Core.Recording;

/// <summary>Issue du rejeu d'un jour.</summary>
public enum DayReplay
{
    /// <summary>Statistiques du jour effacées puis recalculées depuis le brut.</summary>
    Done,

    /// <summary>Jour commencé avant le premier brut chiffré : ses statistiques sont gardées telles quelles.</summary>
    NoRaw,
}

/// <summary>
/// Recalcul de l'historique : rejoue le brut chiffré d'un jour local dans un <see cref="Analyzer"/> neuf,
/// avec les réglages donnés (seuils actuels), et remplace les statistiques du jour dans une seule
/// transaction. Les frappes enregistrées ne changent pas. Les touches des applis exclues et des champs
/// secrets n'ont pas de brut : leurs compteurs sont gardés (<see cref="StatsWriter.DeleteDays"/>).
/// Une session à cheval sur minuit est coupée en deux, chaque jour étant rejoué seul.
/// </summary>
public sealed class HistoryReplay
{
    /// <summary>
    /// Un bloc brut s'étend sur une minute environ (écriture toutes les 60 s) : on relit aussi ceux commencés
    /// un peu avant le jour, pour garder leurs frappes d'après minuit, et on écarte celles d'avant.
    /// </summary>
    public const long BlockMarginMs = 10 * 60_000;

    private readonly KeyStatsDb _db;
    private readonly LocalDayClock _clock;
    private readonly AnalysisSettings _settings;

    public HistoryReplay(KeyStatsDb db, LocalDayClock clock, AnalysisSettings settings)
    {
        ArgumentNullException.ThrowIfNull(db);
        ArgumentNullException.ThrowIfNull(clock);
        ArgumentNullException.ThrowIfNull(settings);
        _db = db;
        _clock = clock;
        _settings = settings;
    }

    /// <summary>Rejoue le jour local <paramref name="day"/> (aaaammjj) avec la clé maîtresse déverrouillée.</summary>
    public DayReplay ReplayDay(int day, MasterKey master)
    {
        ArgumentNullException.ThrowIfNull(master);
        if (!HasRawFor(day)) return DayReplay.NoRaw;
        using var tx = _db.BeginTransaction();
        var writer = new StatsWriter(_db);
        var (fromMs, toMs) = writer.DeleteDays(day, day, _clock);
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
        tx.Commit();
        return DayReplay.Done;
    }

    /// <summary>
    /// Faux si le jour a des frappes comptées avant le premier bloc brut (chiffrement mis en place plus
    /// tard) : effacer ce jour perdrait ces frappes, que rien ne permet de refaire.
    /// </summary>
    private bool HasRawFor(int day)
    {
        if (_db.Scalar("SELECT min(start_ms) FROM raw_block") is not long firstRawMs) return false;
        long dayStartMs = _clock.MidnightUtcMs(LocalDayClock.DateOf(day));
        if (firstRawMs <= dayStartMs) return true;
        long dayEndMs = _clock.MidnightUtcMs(LocalDayClock.DateOf(day).AddDays(1));
        using var cmd = _db.Command(
            "SELECT 1 FROM minute_total WHERE ts_min >= $a AND ts_min < $b AND keys > 0 LIMIT 1",
            LocalDayClock.MinuteOf(dayStartMs), LocalDayClock.MinuteOf(Math.Min(firstRawMs, dayEndMs)));
        return cmd.ExecuteScalar() is null;
    }

    private FocusInfo Info(StoredFocus f) =>
        new(f.Ms, f.AppId, f.TitleId ?? 0, f.LayoutId is int id ? _db.GetLayout(id) : null, f.Flags);
}
