using KeyStats.Core.Storage;

namespace KeyStats.Core.Tests;

public sealed class BackupPlanTests
{
    private static readonly TimeZoneInfo Utc0 = TimeZoneInfo.Utc;

    // 26 novembre 2026, 10 h UTC : le créneau de 3 h du jour est passé.
    private static readonly long Now = new DateTimeOffset(2026, 11, 26, 10, 0, 0, TimeSpan.Zero).ToUnixTimeMilliseconds();
    private const long Minute = 60_000;

    [Fact]
    public void Sans_miroir_due_des_la_premiere_verification()
    {
        Assert.True(new BackupPlan(null).ShouldRun(Now, 0, Utc0));
    }

    [Fact]
    public void Apres_un_succes_pas_de_seconde_sauvegarde_le_meme_jour()
    {
        var plan = new BackupPlan(null);
        plan.Succeeded(Now);
        Assert.False(plan.ShouldRun(Now + 10 * Minute, 10 * Minute, Utc0));
        Assert.False(plan.ShouldRun(Now + 16 * 60 * Minute, 16 * 60 * Minute, Utc0));
        // Le lendemain après 3 h, de nouveau due.
        Assert.True(plan.ShouldRun(Now + 24 * 60 * Minute, 24 * 60 * Minute, Utc0));
    }

    [Fact]
    public void Apres_un_echec_reessai_une_heure_plus_tard_seulement()
    {
        var plan = new BackupPlan(null);
        plan.Failed(1000);
        Assert.False(plan.ShouldRun(Now + 10 * Minute, 1000 + 10 * Minute, Utc0));
        Assert.False(plan.ShouldRun(Now + 59 * Minute, 1000 + BackupPlan.RetryMs - 1, Utc0));
        Assert.True(plan.ShouldRun(Now + 60 * Minute, 1000 + BackupPlan.RetryMs, Utc0));
    }

    [Fact]
    public void Report_insensible_a_une_horloge_reculee()
    {
        // L'heure murale recule d'un jour après l'échec : seule l'horloge monotone compte pour le report.
        var plan = new BackupPlan(null);
        plan.Failed(0);
        Assert.True(plan.ShouldRun(Now - 24 * 60 * Minute, BackupPlan.RetryMs, Utc0));
    }

    [Fact]
    public void Un_succes_leve_le_report()
    {
        var plan = new BackupPlan(Now - 2 * 24 * 60 * Minute);
        plan.Failed(0);
        plan.Succeeded(Now);
        // Le lendemain, due sans attendre la fin d'un report désormais caduc.
        long tomorrow = Now + 24 * 60 * Minute;
        Assert.True(plan.ShouldRun(tomorrow, 1, Utc0));
    }
}
