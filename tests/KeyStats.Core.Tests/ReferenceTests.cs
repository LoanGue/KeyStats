using KeyStats.Core.Analysis;
using KeyStats.Core.Reference;

namespace KeyStats.Core.Tests;

public sealed class LocalDayClockTests
{
    private static long Utc(int y, int mo, int d, int h, int mi = 0) =>
        new DateTimeOffset(y, mo, d, h, mi, 0, TimeSpan.Zero).ToUnixTimeMilliseconds();

    /// <summary>Fuseau aux règles européennes (UTC+1, heure d'été le dernier dimanche de mars et d'octobre).</summary>
    private static TimeZoneInfo Paris(int startHour = 2, int endHour = 3)
    {
        var start = TimeZoneInfo.TransitionTime.CreateFloatingDateRule(new DateTime(1, 1, 1, startHour, 0, 0), 3, 5, DayOfWeek.Sunday);
        var end = TimeZoneInfo.TransitionTime.CreateFloatingDateRule(new DateTime(1, 1, 1, endHour, 0, 0), 10, 5, DayOfWeek.Sunday);
        var rule = TimeZoneInfo.AdjustmentRule.CreateAdjustmentRule(DateTime.MinValue.Date, DateTime.MaxValue.Date, TimeSpan.FromHours(1), start, end);
        return TimeZoneInfo.CreateCustomTimeZone("Test/Paris", TimeSpan.FromHours(1), "Paris", "CET", "CEST", [rule]);
    }

    [Fact]
    public void Jour_de_23_heures_au_passage_a_l_heure_d_ete()
    {
        var clock = new LocalDayClock(Paris());

        long start = clock.MidnightUtcMs(new DateTime(2026, 3, 29));
        long end = clock.MidnightUtcMs(new DateTime(2026, 3, 30));

        Assert.Equal(Utc(2026, 3, 28, 23), start);
        Assert.Equal(Utc(2026, 3, 29, 22), end);
        Assert.Equal(23 * 3_600_000, end - start);
        Assert.Equal(20260328, clock.DayKey(start - 1));
        Assert.Equal(20260329, clock.DayKey(start));
        Assert.Equal(20260329, clock.DayKey(end - 1));
        Assert.Equal(20260330, clock.DayKey(end));
    }

    [Fact]
    public void Jour_de_25_heures_au_retour_a_l_heure_d_hiver()
    {
        var clock = new LocalDayClock(Paris());

        long start = clock.MidnightUtcMs(new DateTime(2026, 10, 25));
        long end = clock.MidnightUtcMs(new DateTime(2026, 10, 26));

        Assert.Equal(Utc(2026, 10, 24, 22), start);
        Assert.Equal(25 * 3_600_000, end - start);
        // 02:30 locale existe deux fois : les deux instants tombent le même jour.
        Assert.Equal(20261025, clock.DayKey(Utc(2026, 10, 25, 0, 30)));
        Assert.Equal(20261025, clock.DayKey(Utc(2026, 10, 25, 1, 30)));
        Assert.Equal(20261025, clock.DayKey(end - 1));
        Assert.Equal(20261026, clock.DayKey(end));
    }

    [Fact]
    public void Minuit_absent_le_jour_commence_a_la_premiere_heure_valide()
    {
        // Changement d'heure à minuit : 00:00 n'existe pas, le jour commence à 01:00 (heure d'été).
        var clock = new LocalDayClock(Paris(startHour: 0));

        long start = clock.MidnightUtcMs(new DateTime(2026, 3, 29));

        Assert.Equal(Utc(2026, 3, 28, 23), start);
        Assert.Equal(20260328, clock.DayKey(start - 1));
        Assert.Equal(20260329, clock.DayKey(start));
    }

    [Fact]
    public void Cache_du_jour_sans_effet_sur_le_resultat()
    {
        var zone = Paris();
        var cached = new LocalDayClock(zone);
        long t = Utc(2026, 3, 27, 0);
        for (int i = 0; i < 200; i++, t += 1_234_567)
        {
            // Aller-retour d'un jour à l'autre : le cache doit suivre dans les deux sens.
            long before = t - 86_400_000;
            Assert.Equal(new LocalDayClock(zone).DayKey(t), cached.DayKey(t));
            Assert.Equal(new LocalDayClock(zone).DayKey(before), cached.DayKey(before));
        }
    }

    [Fact]
    public void Minutes_et_heures_arrondies_vers_le_bas_meme_avant_1970()
    {
        Assert.Equal(0, LocalDayClock.MinuteOf(59_999));
        Assert.Equal(1, LocalDayClock.MinuteOf(60_000));
        Assert.Equal(-1, LocalDayClock.MinuteOf(-1));
        Assert.Equal(-1, LocalDayClock.HourOf(-1));
        Assert.Equal(new DateTime(2026, 11, 2), LocalDayClock.DateOf(20261102));
    }
}

public sealed class LetterFrequencyTests
{
    [Fact]
    public void Chaque_reference_somme_a_un_et_e_en_tete()
    {
        Assert.NotEmpty(LetterFrequencies.All);
        foreach (var lang in LetterFrequencies.All)
        {
            Assert.Equal(1.0, lang.Letters.Values.Sum(), 6);
            Assert.All(lang.Letters.Values, v => Assert.True(v > 0));
            Assert.Equal('e', lang.Letters.MaxBy(kv => kv.Value).Key);
            Assert.False(string.IsNullOrWhiteSpace(lang.Source));
        }
    }

    [Fact]
    public void Accents_ramenes_a_la_lettre_de_base()
    {
        Assert.Equal('e', LetterFrequencies.Fold('é'));
        Assert.Equal('e', LetterFrequencies.Fold('Ê'));
        Assert.Equal('c', LetterFrequencies.Fold('ç'));
        Assert.Equal('a', LetterFrequencies.Fold('À'));
        Assert.Equal('x', LetterFrequencies.Fold('X'));
    }
}
