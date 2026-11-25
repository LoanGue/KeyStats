using System.Text;
using KeyStats.Core.Analysis;
using KeyStats.Core.Crypto;
using KeyStats.Core.Focus;
using KeyStats.Core.Input;
using KeyStats.Core.Recording;
using KeyStats.Core.Storage;

namespace KeyStats.Core.Tests;

public sealed class RecorderTests
{
    private const string Title = "Rapport annuel confidentiel";
    private const string Text = "bonjour tout le monde, ceci est un essai de frappe assez long ";

    private static FocusChange Focus(long ms, string exe, string? title = Title, FocusState flags = FocusState.NewWindow) =>
        new() { Ms = ms, Exe = exe, Title = title, Flags = flags };

    private static Recorder Recorder(TempDb tmp, MasterKey? master) =>
        new(tmp.Db, master is null ? null : new RawArchiveWriter(tmp.Db, master.PublicKey, new TitleHasher(TitleHasher.NewKey())),
            new LocalDayClock(TimeZoneInfo.Utc), Typist.Start);

    private static long Feed(Recorder rec, string text, long startMs)
    {
        var typist = new Typist(startMs).Type(text);
        foreach (var e in typist.Events) rec.OnEvent(e);
        return typist.Ms;
    }

    [Fact]
    public void Sans_chiffrement_seules_les_statistiques_sont_ecrites()
    {
        using var tmp = new TempDb();
        var rec = Recorder(tmp, null);
        rec.OnFocus(Focus(Typist.Start, "winword.exe"));
        long end = Feed(rec, Text, Typist.Start + 10);
        Assert.True(rec.Close(end));

        Assert.True(tmp.Count("SELECT SUM(keys) FROM minute_total") > 0);
        Assert.Equal(1, tmp.Count("SELECT COUNT(*) FROM focus WHERE title_id IS NULL"));
        Assert.Equal(0, tmp.Count("SELECT COUNT(*) FROM raw_block"));
        Assert.Equal(0, tmp.Count("SELECT COUNT(*) FROM window_title"));
    }

    [Fact]
    public void Brut_et_titre_chiffres_rattaches_a_la_fenetre()
    {
        using var tmp = new TempDb();
        using var master = MasterKey.Generate();
        var rec = Recorder(tmp, master);
        rec.OnFocus(Focus(Typist.Start, "winword.exe"));
        long end = Feed(rec, Text, Typist.Start + 10);
        rec.OnFocus(Focus(end + 100, "notepad.exe", "Autre"));
        end = Feed(rec, Text, end + 200);
        Assert.True(rec.Close(end));

        Assert.Equal(2, tmp.Count("SELECT COUNT(*) FROM window_title"));
        Assert.Equal(2, tmp.Count("SELECT COUNT(DISTINCT focus_id) FROM raw_block WHERE focus_id IS NOT NULL"));
        Assert.Equal(0, tmp.Count("SELECT COUNT(*) FROM raw_block b JOIN focus f ON f.id = b.focus_id WHERE b.start_ms < f.ts_ms"));
        byte[] bytes = tmp.ReadAllBytesClosed();
        foreach (var enc in (Encoding[])[Encoding.UTF8, Encoding.Unicode])
            Assert.Equal(-1, bytes.AsSpan().IndexOf(enc.GetBytes(Title)));
    }

    [Fact]
    public void Appli_exclue_compteurs_seulement_et_pause_notee()
    {
        using var tmp = new TempDb();
        using var master = MasterKey.Generate();
        var rec = Recorder(tmp, master);
        rec.OnFocus(Focus(Typist.Start, "keepass.exe", null, FocusState.NewWindow | FocusState.Excluded));
        long end = Feed(rec, "motdepasse", Typist.Start + 10);
        rec.OnFocus(Focus(end + 100, "notepad.exe"));
        Assert.True(rec.Close(end + 200));

        Assert.Equal(0, tmp.Count("SELECT COUNT(*) FROM raw_block"));
        Assert.True(tmp.Count("SELECT SUM(excluded_keys) FROM minute_total") > 0);
        Assert.Equal(1, tmp.Count($"SELECT COUNT(*) FROM pause WHERE reason = {(int)PauseReason.Excluded} AND start_ms = {Typist.Start} AND end_ms = {end + 100}"));
    }

    [Fact]
    public void Ecriture_toutes_les_60_secondes()
    {
        using var tmp = new TempDb();
        var rec = Recorder(tmp, null);
        rec.OnFocus(Focus(Typist.Start, "winword.exe"));
        Feed(rec, Text, Typist.Start + 10);
        long batches = rec.Counters.Batches;

        rec.Tick(Typist.Start + 30_000);
        Assert.Equal(batches, rec.Counters.Batches);
        rec.Tick(Typist.Start + Recording.Recorder.FlushIntervalMs + 1);
        Assert.Equal(batches + 1, rec.Counters.Batches);
        Assert.True(tmp.Count("SELECT SUM(keys) FROM minute_total") > 0);
    }

    [Fact]
    public void Pause_exterieure_rien_n_est_compte()
    {
        using var tmp = new TempDb();
        var rec = Recorder(tmp, null);
        rec.OnFocus(Focus(Typist.Start, "winword.exe"));
        rec.BeginPause(PauseReason.Snooze, Typist.Start + 5);
        long end = Feed(rec, Text, Typist.Start + 10);
        rec.EndPause(end + 1);
        Assert.True(rec.Close(end + 2));

        Assert.True(rec.Counters.PausedEvents > 0);
        Assert.Equal(0, tmp.Count("SELECT COUNT(*) FROM minute_total WHERE keys > 0"));
        Assert.Equal(1, tmp.Count($"SELECT COUNT(*) FROM pause WHERE reason = {(int)PauseReason.Snooze} AND end_ms = {end + 1}"));
    }

    [Fact]
    public void Marqueurs_de_la_file_resolus_par_le_canal()
    {
        using var tmp = new TempDb();
        var rec = Recorder(tmp, null);
        var ring = new EventRing(64);
        var channel = new FocusChannel();
        Assert.True(channel.Publish(Focus(Typist.Start, "winword.exe"), ring));
        var batch = new InputEvent[64];
        int n = ring.Read(batch);
        rec.Consume(batch.AsSpan(0, n), channel);
        rec.Consume([new InputEvent(Typist.Start + 1, EventKind.Focus, 999)], channel);
        Assert.True(rec.Close(Typist.Start + 2));

        Assert.Equal(1, tmp.Count("SELECT COUNT(*) FROM focus f JOIN app a ON a.id = f.app_id WHERE a.exe = 'winword.exe'"));
        Assert.Equal(1, rec.Counters.UnknownMarkers);
    }

    [Fact]
    public void Appli_inconnue_acceptee()
    {
        using var tmp = new TempDb();
        var rec = Recorder(tmp, null);
        rec.OnFocus(Focus(Typist.Start, "", null, FocusState.NewWindow | FocusState.Excluded));
        Assert.True(rec.Close(Typist.Start + 1));
        Assert.Equal(0, rec.Counters.WriteErrors);
        Assert.Equal(1, tmp.Count("SELECT COUNT(*) FROM focus"));
    }
}
