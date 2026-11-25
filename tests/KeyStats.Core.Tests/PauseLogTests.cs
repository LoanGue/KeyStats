using KeyStats.Core.Recording;
using KeyStats.Core.Storage;

namespace KeyStats.Core.Tests;

public sealed class PauseLogTests
{
    private static List<(long Start, long End, long Reason)> Rows(TempDb tmp)
    {
        var rows = new List<(long, long, long)>();
        using var cmd = tmp.Db.Connection.CreateCommand();
        cmd.CommandText = "SELECT start_ms, end_ms, reason FROM pause ORDER BY start_ms, reason";
        using var r = cmd.ExecuteReader();
        while (r.Read()) rows.Add((r.GetInt64(0), r.GetInt64(1), r.GetInt64(2)));
        return rows;
    }

    [Fact]
    public void Periode_ecrite_seulement_une_fois_terminee()
    {
        using var tmp = new TempDb();
        var log = new PauseLog();
        log.SetFocusReason(100, PauseReason.Elevated);
        Assert.False(log.HasDone);

        log.SetFocusReason(500, 0);
        Assert.True(log.HasDone);
        log.Write(tmp.Db);
        log.Clear();
        Assert.False(log.HasDone);
        Assert.Equal([(100L, 500L, (long)PauseReason.Elevated)], Rows(tmp));
    }

    [Fact]
    public void Sources_independantes_et_changement_de_motif()
    {
        using var tmp = new TempDb();
        var log = new PauseLog();
        log.SetFocusReason(100, PauseReason.Excluded);
        log.Begin(200, PauseReason.Locked);
        log.SetFocusReason(300, PauseReason.Secret);
        Assert.True(log.External);
        log.End(400);
        Assert.False(log.External);
        log.CloseAll(600);
        log.Write(tmp.Db);

        Assert.Equal(
            [(100L, 300L, (long)PauseReason.Excluded), (200L, 400L, (long)PauseReason.Locked), (300L, 600L, (long)PauseReason.Secret)],
            Rows(tmp));
    }

    [Fact]
    public void Meme_motif_ou_duree_nulle_rien_de_plus()
    {
        using var tmp = new TempDb();
        var log = new PauseLog();
        log.Begin(100, PauseReason.Snooze);
        log.Begin(200, PauseReason.Snooze);
        log.End(300);
        log.SetFocusReason(400, PauseReason.Elevated);
        log.SetFocusReason(400, 0);
        log.End(500);
        log.Write(tmp.Db);

        Assert.Equal([(100L, 300L, (long)PauseReason.Snooze)], Rows(tmp));
    }

    [Fact]
    public void Nouveau_motif_exterieur_ferme_le_precedent()
    {
        using var tmp = new TempDb();
        var log = new PauseLog();
        log.Begin(100, PauseReason.Locked);
        log.Begin(300, PauseReason.Snooze);
        log.End(700);
        log.Write(tmp.Db);

        Assert.Equal([(100L, 300L, (long)PauseReason.Locked), (300L, 700L, (long)PauseReason.Snooze)], Rows(tmp));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(99)]
    public void Pause_sans_motif_connu_refusee(int reason) =>
        Assert.Throws<ArgumentOutOfRangeException>(() => new PauseLog().Begin(1, (PauseReason)reason));
}
