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
    public void Sans_chiffrement_statistiques_seules_toutes_les_60_secondes()
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
        Assert.Equal(0, tmp.Count("SELECT COUNT(*) FROM raw_block b JOIN focus f ON f.id = b.focus_id WHERE b.start_ms < f.ts_ms"
            + " OR EXISTS (SELECT 1 FROM focus n WHERE n.ts_ms > f.ts_ms AND n.ts_ms <= b.end_ms)"));
        byte[] bytes = tmp.ReadAllBytesClosed();
        foreach (var enc in (Encoding[])[Encoding.UTF8, Encoding.Unicode])
            Assert.Equal(-1, bytes.AsSpan().IndexOf(enc.GetBytes(Title)));
    }

    [Theory]
    [InlineData("keepass.exe", FocusState.Excluded, PauseReason.Excluded)]
    [InlineData("", FocusState.Excluded, PauseReason.Excluded)]
    [InlineData("chrome.exe", FocusState.Secret, PauseReason.Secret)]
    public void Fenetre_compteurs_seulement_et_pause_notee(string exe, FocusState flags, PauseReason reason)
    {
        using var tmp = new TempDb();
        using var master = MasterKey.Generate();
        var rec = Recorder(tmp, master);
        rec.OnFocus(Focus(Typist.Start, exe, null, FocusState.NewWindow | flags));
        long end = Feed(rec, "motdepasse", Typist.Start + 10);
        rec.OnFocus(Focus(end + 100, "notepad.exe"));
        Assert.True(rec.Close(end + 200));

        Assert.Equal(0, tmp.Count("SELECT COUNT(*) FROM raw_block"));
        Assert.True(tmp.Count("SELECT SUM(excluded_keys) FROM minute_total") > 0);
        Assert.Equal(1, tmp.Count($"SELECT COUNT(*) FROM pause WHERE reason = {(int)reason} AND start_ms = {Typist.Start} AND end_ms = {end + 100}"));
    }

    [Fact]
    public void Pause_exterieure_session_conclue_puis_rien_n_est_compte()
    {
        using var tmp = new TempDb();
        var rec = Recorder(tmp, null);
        rec.OnFocus(Focus(Typist.Start, "winword.exe"));
        long start = Feed(rec, Text, Typist.Start + 10) + 5;
        rec.BeginPause(PauseReason.Snooze, start);
        Assert.True(rec.Flush(start));
        Assert.Equal(1, tmp.Count("SELECT COUNT(*) FROM session"));
        long keys = tmp.Count("SELECT SUM(keys) FROM minute_total");
        long end = Feed(rec, Text, start + 5);
        rec.EndPause(end + 1);
        Assert.True(rec.Close(end + 2));

        Assert.Equal(new Typist().Type(Text).Events.Count, rec.Counters.PausedEvents);
        Assert.Equal(keys, tmp.Count("SELECT SUM(keys) FROM minute_total"));
        Assert.Equal(1, tmp.Count($"SELECT COUNT(*) FROM pause WHERE reason = {(int)PauseReason.Snooze} AND start_ms = {start} AND end_ms = {end + 1}"));
    }

    [Fact]
    public void Remise_a_zero_pendant_la_pause_relache_les_touches()
    {
        using var tmp = new TempDb();
        var rec = Recorder(tmp, null);
        rec.OnFocus(Focus(Typist.Start, "winword.exe"));
        var held = new Typist(Typist.Start + 10).Down(KeyCode.LeftCtrl);
        foreach (var e in held.Events) rec.OnEvent(e);
        rec.BeginPause(PauseReason.Locked, held.Ms + 10);
        rec.OnEvent(new Typist(held.Ms + 20).Reset().Events[0]);
        rec.EndPause(held.Ms + 30);
        Assert.True(rec.Close(Feed(rec, "abc", held.Ms + 40)));

        Assert.Equal(0, tmp.Count("SELECT COUNT(*) FROM day_shortcut"));
    }

    private static void Fail(TempDb tmp, string table) =>
        tmp.Db.Exec($"CREATE TEMP TRIGGER fail_{table} BEFORE INSERT ON {table} BEGIN SELECT RAISE(ABORT, 'x'); END");

    private static void Heal(TempDb tmp, string table) => tmp.Db.Exec($"DROP TRIGGER fail_{table}");

    [Fact]
    public void Ecriture_ratee_tout_garde_et_rien_en_double()
    {
        using var control = new TempDb();
        var expected = Recorder(control, null);
        expected.OnFocus(Focus(Typist.Start, "winword.exe"));
        expected.Close(Feed(expected, Text, Typist.Start + 10));

        using var tmp = new TempDb();
        var rec = Recorder(tmp, null);
        rec.OnFocus(Focus(Typist.Start, "winword.exe"));
        long end = Feed(rec, Text, Typist.Start + 10);
        Fail(tmp, "minute_total");
        Assert.False(rec.Flush(end));
        Heal(tmp, "minute_total");
        Assert.True(rec.Close(end));

        Assert.Equal(1, rec.Counters.WriteErrors);
        Assert.Equal(control.Count("SELECT SUM(keys) FROM minute_total"), tmp.Count("SELECT SUM(keys) FROM minute_total"));
    }

    [Fact]
    public void Focus_rate_le_brut_reste_a_la_fenetre_precedente()
    {
        using var tmp = new TempDb();
        using var master = MasterKey.Generate();
        var rec = Recorder(tmp, master);
        rec.OnFocus(Focus(Typist.Start, "winword.exe"));
        long end = Feed(rec, Text, Typist.Start + 10);
        long switchMs = end + 100;
        Fail(tmp, "focus");
        rec.OnFocus(Focus(switchMs, "notepad.exe", null));
        end = Feed(rec, Text, switchMs + 100);
        Heal(tmp, "focus");
        Assert.True(rec.Close(end));

        Assert.Equal(1, tmp.Count("SELECT COUNT(*) FROM focus"));
        Assert.Equal(1, tmp.Count("SELECT COUNT(*) FROM raw_block"));
        Assert.Equal(1, tmp.Count($"SELECT COUNT(*) FROM raw_block WHERE focus_id = (SELECT id FROM focus) AND end_ms < {switchMs}"));
    }

    [Fact]
    public void Commit_rate_au_changement_de_fenetre_aucun_brut_orphelin()
    {
        using var tmp = new TempDb();
        using var master = MasterKey.Generate();
        var rec = Recorder(tmp, master);
        rec.OnFocus(Focus(Typist.Start, "winword.exe"));
        // Clé étrangère différée violée : l'insertion passe, le commit échoue.
        tmp.Db.Exec("CREATE TEMP TRIGGER bad AFTER INSERT ON focus BEGIN INSERT INTO focus(ts_ms, app_id) VALUES (0, -1); END");
        tmp.Db.Exec("PRAGMA defer_foreign_keys = ON"); // remis à zéro par chaque commit : après le trigger
        rec.OnFocus(Focus(Typist.Start + 1, "notepad.exe"));
        Assert.Equal(1, rec.Counters.WriteErrors);
        Assert.Equal(1, tmp.Count("SELECT COUNT(*) FROM focus"));
        tmp.Db.Exec("DROP TRIGGER bad");
        Assert.True(rec.Close(Feed(rec, Text, Typist.Start + 10)));
        Assert.Equal(0, tmp.Count("SELECT COUNT(*) FROM raw_block"));
    }

    [Fact]
    public void Tampon_plein_et_base_refusee_brut_perdu_et_compte()
    {
        using var tmp = new TempDb();
        using var master = MasterKey.Generate();
        var rec = Recorder(tmp, master);
        Feed(rec, "avant la fenetre", Typist.Start - 5000);
        rec.OnFocus(Focus(Typist.Start, "winword.exe"));
        Fail(tmp, "raw_block");
        Feed(rec, new string('a', Recording.Recorder.RawCapacity / 2 + 10), Typist.Start + 10);
        Assert.Equal(Recording.Recorder.RawCapacity, rec.Counters.RawDropped);
        Assert.False(rec.Close(Typist.Start + 10_000_000));
        Assert.True(rec.Counters.RawDropped > Recording.Recorder.RawCapacity);
        Heal(tmp, "raw_block");
        Assert.True(rec.Close(Typist.Start + 10_000_000));
        Assert.Equal(0, tmp.Count($"SELECT COUNT(*) FROM raw_block WHERE start_ms < {Typist.Start}"));
    }

    [Fact]
    public void Cle_maitresse_illisible_statistiques_seulement()
    {
        using var tmp = new TempDb();
        var rec = new Recorder(tmp.Db, new RawArchiveWriter(tmp.Db, new byte[65], new TitleHasher(TitleHasher.NewKey())),
            new LocalDayClock(TimeZoneInfo.Utc), Typist.Start);
        rec.OnFocus(Focus(Typist.Start, "winword.exe"));
        rec.OnFocus(Focus(Typist.Start + 1, "winword.exe"));
        Assert.True(rec.Close(Feed(rec, Text, Typist.Start + 10)));

        Assert.False(rec.Encrypting);
        Assert.True(tmp.Count("SELECT SUM(keys) FROM minute_total") > 0);
        Assert.Equal(0, tmp.Count("SELECT COUNT(*) FROM raw_block") + tmp.Count("SELECT COUNT(*) FROM window_title"));
    }

    [Fact]
    public void Avant_la_premiere_fenetre_compteurs_seulement()
    {
        using var tmp = new TempDb();
        var rec = Recorder(tmp, null);
        Assert.True(rec.Close(Feed(rec, Text + Text, Typist.Start + 10)));

        Assert.True(tmp.Count("SELECT SUM(excluded_keys) FROM minute_total") > 0);
        Assert.Equal(0, tmp.Count("SELECT COUNT(*) FROM session") + tmp.Count("SELECT COUNT(*) FROM key_minute"));
    }

    [Fact]
    public void Erreur_de_programmation_remonte_et_la_fenetre_reste_fermee()
    {
        using var tmp = new TempDb();
        var rec = Recorder(tmp, null);
        rec.OnFocus(Focus(Typist.Start, "winword.exe"));
        using (tmp.Db.BeginTransaction())
            Assert.Throws<InvalidOperationException>(() => rec.OnFocus(Focus(Typist.Start + 1, "chrome.exe", null, FocusState.NewWindow | FocusState.Secret)));
        Assert.True(rec.Close(Feed(rec, Text, Typist.Start + 10)));

        Assert.Equal(0, rec.Counters.WriteErrors);
        Assert.True(tmp.Count("SELECT SUM(excluded_keys) FROM minute_total") > 0);
        Assert.Equal(0, tmp.Count("SELECT COUNT(*) FROM session") + tmp.Count("SELECT COUNT(*) FROM key_minute"));
    }

    [Fact]
    public void Oubli_de_l_epoque_le_brut_suivant_en_change()
    {
        using var tmp = new TempDb();
        using var master = MasterKey.Generate();
        var rec = Recorder(tmp, master);
        rec.OnFocus(Focus(Typist.Start, "winword.exe"));
        long end = Feed(rec, Text, Typist.Start + 10);
        rec.DropEpoch(end);
        Assert.Equal(1, tmp.Count("SELECT COUNT(*) FROM raw_block"));
        Assert.True(rec.Close(Feed(rec, Text, end + 100)));

        Assert.Equal(2, tmp.Count("SELECT COUNT(DISTINCT epoch_id) FROM raw_block"));
    }

    [Fact]
    public void Changement_de_fuseau_frappes_suivantes_dans_le_nouveau_jour()
    {
        using var tmp = new TempDb();
        var rec = Recorder(tmp, null);
        rec.OnFocus(Focus(Typist.Start, "winword.exe"));
        long end = Feed(rec, Text, Typist.Start + 10);
        int hour = DateTimeOffset.FromUnixTimeMilliseconds(Typist.Start).UtcDateTime.Hour;
        var zone = TimeZoneInfo.CreateCustomTimeZone("test", TimeSpan.FromHours(hour >= 10 ? 14 : -12), "test", "test");
        long batches = rec.Counters.Batches;
        rec.SetZone(zone, end);
        Assert.Equal(batches + 1, rec.Counters.Batches);
        Assert.True(rec.Close(Feed(rec, Text, end + 100)));

        Assert.Equal(2, tmp.Count("SELECT COUNT(DISTINCT day) FROM session"));
        Assert.Equal(2, tmp.Count("SELECT COUNT(DISTINCT day) FROM day_char"));
    }

    [Fact]
    public void Horloge_qui_recule_ecriture_60_secondes_apres_le_recul()
    {
        using var tmp = new TempDb();
        var rec = Recorder(tmp, null);
        rec.OnFocus(Focus(Typist.Start, "winword.exe"));
        long end = Feed(rec, Text, Typist.Start + 10);
        rec.Tick(end + Recording.Recorder.FlushIntervalMs);
        long batches = rec.Counters.Batches;
        Feed(rec, Text, end + Recording.Recorder.FlushIntervalMs + 10);
        long back = Typist.Start - 10_000;
        rec.Tick(back);
        rec.Tick(back);
        rec.Tick(back + Recording.Recorder.FlushIntervalMs - 1);
        Assert.Equal(batches, rec.Counters.Batches);
        rec.Tick(back + Recording.Recorder.FlushIntervalMs);
        Assert.Equal(batches + 1, rec.Counters.Batches);
    }
}
