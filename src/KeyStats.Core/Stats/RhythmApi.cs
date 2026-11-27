using KeyStats.Core.Analysis;
using KeyStats.Core.Storage;
using KeyStats.Core.Web;

namespace KeyStats.Core.Stats;

/// <param name="Finger">Nom du doigt : <c>leftPinky</c> … <c>rightPinky</c>.</param>
/// <param name="Presses">Touches relâchées attribuées à ce doigt par le doigté AZERTY standard.</param>
public sealed record RhythmFinger(string Finger, long Presses);

/// <param name="Opposite">Majuscules faites avec la touche Maj de l'autre main que la lettre.</param>
/// <param name="SameSide">Majuscules faites avec la touche Maj du même côté.</param>
/// <param name="CapsLock">Majuscules faites sous Verr. Maj.</param>
public sealed record RhythmCapitals(long Opposite, long SameSide, long CapsLock);

/// <param name="DayTablesUnfiltered">Vrai avec un filtre d'appli, de nature ou de clavier : toute la page vient des tables par jour, qui l'ignorent.</param>
/// <param name="MedianDelayMs">Délai médian entre deux caractères d'une session ; null sans enchaînement mesuré.</param>
/// <param name="Bigrams">
/// Enchaînements tapés au moins <see cref="RhythmApi.MinCount"/> fois : les <see cref="RhythmApi.MaxBigrams"/> plus coûteux,
/// plus les <see cref="RhythmApi.MaxBigrams"/> au plus fort taux de faute, du plus coûteux au moins coûteux.
/// </param>
/// <param name="Hold">Durée d'appui des touches de caractère et de l'espace, cases de 10 ms à 2 s.</param>
/// <param name="CharPresses">Appuis sur une touche de caractère, répétitions exclues.</param>
/// <param name="Overlaps">Parmi eux, appuis faits avant le relâchement de la touche de caractère précédente.</param>
/// <param name="Fingers">Les dix doigts, de l'auriculaire gauche à l'auriculaire droit.</param>
public sealed record Rhythm(
    string From, string To, bool DayTablesUnfiltered, double? MedianDelayMs, IReadOnlyList<RhythmBigram> Bigrams,
    TimeDistribution Hold, long CharPresses, long Overlaps, IReadOnlyList<RhythmFinger> Fingers, RhythmFlow Flow, RhythmCapitals Capitals);

/// <summary>
/// Route <c>/api/rhythm</c> : bigrammes qui ralentissent, durées d'appui, chevauchement, et doigté AZERTY standard
/// (charge par doigt, même doigt, alternance des mains, côté de la touche Maj). Le doigté est indicatif.
/// </summary>
public static class RhythmApi
{
    public const string Path = "/api/rhythm";
    public const int MinCount = 10;
    public const int MaxBigrams = 20;

    public static void Register(WebRouter router, StatsContext stats)
    {
        ArgumentNullException.ThrowIfNull(router);
        ArgumentNullException.ThrowIfNull(stats);
        router.Get(Path, q => stats.Respond(q, f => Read(stats.Db, f), StatsJson.Default.Rhythm));
    }

    public static Rhythm Read(KeyStatsDb db, StatsFilter filter)
    {
        ArgumentNullException.ThrowIfNull(db);
        ArgumentNullException.ThrowIfNull(filter);
        var range = filter.Range;
        var bigrams = BigramTable.Read(db, range);
        var holds = DayHistograms.BySub(db, range, HistogramKind.HoldMs);
        var m = TypoDayTables.Metrics(db, range);
        return new Rhythm(StatsFilter.Iso(range.FromDay), StatsFilter.Iso(range.ToDay), filter.HasSessionFilter, bigrams.MedianMs,
            bigrams.Ranked(MinCount, MaxBigrams), KeyLoad.Hold(holds), m.GetValueOrDefault(DayMetric.CharPresses),
            m.GetValueOrDefault(DayMetric.Overlaps), KeyLoad.Fingers(holds), bigrams.Flow(),
            new RhythmCapitals(m.GetValueOrDefault(DayMetric.ShiftOppositeCapitals), m.GetValueOrDefault(DayMetric.ShiftSameSideCapitals),
                m.GetValueOrDefault(DayMetric.CapsLockCapitals)));
    }
}
