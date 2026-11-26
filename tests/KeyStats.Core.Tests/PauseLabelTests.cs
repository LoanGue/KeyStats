using KeyStats.Core.Recording;
using KeyStats.Core.Storage;

namespace KeyStats.Core.Tests;

public sealed class PauseLabelTests
{
    private const long Min = 60_000;
    // 13:20 UTC, soit 14:20 dans un fuseau à UTC+1.
    private static readonly long T = new DateTimeOffset(2026, 11, 26, 13, 20, 0, TimeSpan.Zero).ToUnixTimeMilliseconds();
    private static readonly TimeZoneInfo Zone = TimeZoneInfo.CreateCustomTimeZone("UTC+1", TimeSpan.FromHours(1), "UTC+1", "UTC+1");

    [Fact]
    public void Enregistrement_en_cours() =>
        Assert.Equal("KeyStats — enregistrement en cours", PauseLabel.Format(0, 0, T, Zone));

    [Fact]
    public void Pause_avec_heure_locale_de_reprise() =>
        Assert.Equal("KeyStats — en pause, reprise à 14:35 (dans 15 min)",
            PauseLabel.Format(PauseReason.Snooze, T + 15 * Min, T, Zone));

    [Fact]
    public void Minutes_restantes_arrondies_au_dessus()
    {
        Assert.Equal("KeyStats — en pause, reprise à 14:35 (dans 1 min)",
            PauseLabel.Format(PauseReason.Snooze, T + 15 * Min, T + 15 * Min - 1000, Zone));
        Assert.Equal("KeyStats — en pause, reprise à 14:35 (dans 2 min)",
            PauseLabel.Format(PauseReason.Snooze, T + 15 * Min, T + 13 * Min + 1, Zone));
    }

    [Fact]
    public void Au_dela_d_une_heure_en_heures_et_minutes()
    {
        Assert.Equal("KeyStats — en pause, reprise à 15:20 (dans 1 h 00)",
            PauseLabel.Format(PauseReason.Snooze, T + 60 * Min, T, Zone));
        Assert.Equal("KeyStats — en pause, reprise à 16:05 (dans 1 h 45)",
            PauseLabel.Format(PauseReason.Snooze, T + 105 * Min, T, Zone));
    }

    [Fact]
    public void Pause_sans_fin() =>
        Assert.Equal("KeyStats — en pause jusqu'à reprise manuelle",
            PauseLabel.Format(PauseReason.Snooze, PauseSources.UntilResumed, T, Zone));

    [Fact]
    public void Fin_passee_sans_compte_a_rebours() =>
        Assert.Equal("KeyStats — en pause, reprise à 14:35",
            PauseLabel.Format(PauseReason.Snooze, T + 15 * Min, T + 15 * Min + 400, Zone));

    [Fact]
    public void Heure_locale_apres_le_passage_a_l_heure_d_ete()
    {
        // Le 29 mars 2026 à 01:00 UTC, Paris passe de 02:00 à 03:00 (identifiant Windows, sans conversion IANA).
        var paris = TimeZoneInfo.FindSystemTimeZoneById("Romance Standard Time");
        long now = new DateTimeOffset(2026, 3, 29, 0, 55, 0, TimeSpan.Zero).ToUnixTimeMilliseconds();
        Assert.Equal("KeyStats — en pause, reprise à 03:05 (dans 10 min)",
            PauseLabel.Format(PauseReason.Snooze, now + 10 * Min, now, paris));
    }

    [Theory]
    [InlineData(PauseReason.Elevated)]
    [InlineData(PauseReason.Excluded)]
    [InlineData(PauseReason.Secret)]
    [InlineData(PauseReason.Stopped)]
    public void Motif_hors_pause_exterieure_refuse(PauseReason reason) =>
        Assert.Throws<ArgumentOutOfRangeException>(() => PauseLabel.Format(reason, 0, T, Zone));

    [Fact]
    public void Verrouillage_ignore_la_fin_de_pause_par_l_icone() =>
        Assert.Equal("KeyStats — en pause (session verrouillée ou en veille)",
            PauseLabel.Format(PauseReason.Locked, T + 15 * Min, T, Zone));
}
