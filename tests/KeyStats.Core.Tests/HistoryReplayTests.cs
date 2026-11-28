using KeyStats.Core.Analysis;
using KeyStats.Core.Crypto;
using KeyStats.Core.Focus;
using KeyStats.Core.Recording;
using KeyStats.Core.Storage;

namespace KeyStats.Core.Tests;

public sealed class HistoryReplayTests
{
    private const int Day = 20261102;
    private const string Text = "bonjour tout le monde, ceci est un essai de frappe assez long ";
    private static readonly LocalDayClock Clock = new(TimeZoneInfo.Utc);

    private static FocusChange Focus(long ms, string exe, FocusState flags = FocusState.NewWindow) =>
        new() { Ms = ms, Exe = exe, Title = "Brouillon", Flags = flags };

    private static Recorder Recorder(TempDb tmp, MasterKey? master) =>
        new(tmp.Db, master is null ? null : new RawArchiveWriter(tmp.Db, master.PublicKey, new TitleHasher(TitleHasher.NewKey())),
            Clock, Typist.Start);

    private static long Feed(Recorder rec, string text, long startMs)
    {
        var typist = new Typist(startMs).Type(text);
        foreach (var e in typist.Events) rec.OnEvent(e);
        return typist.Ms;
    }

    /// <summary>Deux phrases séparées de 3 s : une session avec l'écart par défaut (5 s).</summary>
    private static long RecordTwoPhrases(Recorder rec, long startMs)
    {
        rec.OnFocus(Focus(startMs, "winword.exe"));
        long end = Feed(rec, Text, startMs + 10);
        return Feed(rec, Text, end + 3_000);
    }

    [Fact]
    public void Seuils_actuels_appliques_au_brut_du_jour()
    {
        using var tmp = new TempDb();
        using var master = MasterKey.Generate();
        var rec = Recorder(tmp, master);
        Assert.True(rec.Close(RecordTwoPhrases(rec, Typist.Start)));
        Assert.Equal(1, tmp.Count("SELECT COUNT(*) FROM session"));
        long keys = tmp.Count("SELECT SUM(keys) FROM minute_total");

        var replay = new HistoryReplay(tmp.Db, Clock, new AnalysisSettings { SessionGapMs = 2_000 });
        Assert.Equal(DayReplay.Done, replay.Replay(Day, master).Outcome);

        Assert.Equal(2, tmp.Count("SELECT COUNT(*) FROM session"));
        Assert.Equal(keys, tmp.Count("SELECT SUM(keys) FROM minute_total"));
        Assert.Equal(DayReplay.Done, replay.Replay(Day, master).Outcome);
        Assert.Equal(2, tmp.Count("SELECT COUNT(*) FROM session"));
        Assert.Equal(keys, tmp.Count("SELECT SUM(keys) FROM minute_total"));
    }

    [Fact]
    public void Compteurs_des_applis_exclues_gardes()
    {
        using var tmp = new TempDb();
        using var master = MasterKey.Generate();
        var rec = Recorder(tmp, master);
        long end = RecordTwoPhrases(rec, Typist.Start);
        rec.OnFocus(Focus(end + 100, "keepass.exe", FocusState.NewWindow | FocusState.Excluded));
        Assert.True(rec.Close(Feed(rec, "motdepasse", end + 200)));
        long excluded = tmp.Count("SELECT SUM(excluded_keys) FROM minute_total");
        Assert.True(excluded > 0);

        Assert.Equal(DayReplay.Done, new HistoryReplay(tmp.Db, Clock, AnalysisSettings.Default).Replay(Day, master).Outcome);

        Assert.Equal(excluded, tmp.Count("SELECT SUM(excluded_keys) FROM minute_total"));
    }

    /// <summary>Par jour : sessions, caractères des sessions, caractères du jour et touches par minute.</summary>
    private static string Snapshot(TempDb tmp, long midnight) =>
        $"{tmp.Count("SELECT COUNT(*) FROM session WHERE day = 20261102")} {tmp.Count("SELECT COUNT(*) FROM session WHERE day = 20261103")}"
        + $" {tmp.Count("SELECT coalesce(SUM(chars), 0) FROM session WHERE day = 20261102")} {tmp.Count("SELECT coalesce(SUM(chars), 0) FROM session WHERE day = 20261103")}"
        + $" {tmp.Count("SELECT coalesce(SUM(n), 0) FROM day_char WHERE day = 20261102")} {tmp.Count("SELECT coalesce(SUM(n), 0) FROM day_char WHERE day = 20261103")}"
        + $" {tmp.Count($"SELECT SUM(keys) FROM minute_total WHERE ts_min < {midnight / 60_000}")} {tmp.Count($"SELECT SUM(keys) FROM minute_total WHERE ts_min >= {midnight / 60_000}")}";

    [Fact]
    public void Session_a_cheval_sur_minuit_rejouee_avec_la_veille()
    {
        using var tmp = new TempDb();
        using var master = MasterKey.Generate();
        long midnight = Clock.MidnightUtcMs(new DateTime(2026, 11, 3));
        var rec = Recorder(tmp, master);
        rec.OnFocus(Focus(midnight - 20_000, "winword.exe"));
        Assert.True(rec.Close(Feed(rec, Text + Text, midnight - 15_000)));
        Assert.Equal(1, tmp.Count($"SELECT COUNT(*) FROM session WHERE start_ms < {midnight} AND end_ms > {midnight}"));
        Assert.True(tmp.Count($"SELECT COUNT(*) FROM raw_block WHERE start_ms < {midnight} AND end_ms >= {midnight}") > 0);
        string before = Snapshot(tmp, midnight);

        var replay = new HistoryReplay(tmp.Db, Clock, AnalysisSettings.Default);
        Assert.Equal(new ReplayResult(DayReplay.Done, 20261102, 20261103), replay.Replay(20261103, master));
        Assert.Equal(before, Snapshot(tmp, midnight));
        Assert.Equal(new ReplayResult(DayReplay.Done, 20261102, 20261103), replay.Replay(20261102, master));
        Assert.Equal(before, Snapshot(tmp, midnight));
    }

    [Fact]
    public void Brut_perdu_annule_le_rejeu()
    {
        using var tmp = new TempDb();
        using var master = MasterKey.Generate();
        var rec = Recorder(tmp, master);
        long end = RecordTwoPhrases(rec, Typist.Start);
        rec.OnFocus(Focus(end + 100, "notepad.exe"));
        Assert.True(rec.Close(Feed(rec, Text, end + 200)));
        Assert.True(tmp.Count("SELECT COUNT(*) FROM raw_block") > 1);
        tmp.Db.Exec("DELETE FROM raw_block WHERE id = (SELECT max(id) FROM raw_block)");
        long keys = tmp.Count("SELECT SUM(keys) FROM minute_total");

        var replay = new HistoryReplay(tmp.Db, Clock, new AnalysisSettings { SessionGapMs = 2_000 });
        Assert.Equal(DayReplay.Incomplete, replay.Replay(Day, master).Outcome);

        Assert.Equal(2, tmp.Count("SELECT COUNT(*) FROM session"));
        Assert.Equal(keys, tmp.Count("SELECT SUM(keys) FROM minute_total"));
    }

    [Fact]
    public void Jour_sans_brut_garde_tel_quel()
    {
        using var tmp = new TempDb();
        var plain = Recorder(tmp, null);
        Assert.True(plain.Close(RecordTwoPhrases(plain, Typist.Start)));
        using var master = MasterKey.Generate();
        var replay = new HistoryReplay(tmp.Db, Clock, new AnalysisSettings { SessionGapMs = 2_000 });

        Assert.Equal(DayReplay.NoRaw, replay.Replay(Day, master).Outcome);

        var rec = new Recorder(tmp.Db, new RawArchiveWriter(tmp.Db, master.PublicKey, new TitleHasher(TitleHasher.NewKey())),
            Clock, Typist.Start + 3_600_000);
        Assert.True(rec.Close(RecordTwoPhrases(rec, Typist.Start + 3_600_000)));
        Assert.Equal(DayReplay.NoRaw, replay.Replay(Day, master).Outcome);
        Assert.Equal(2, tmp.Count("SELECT COUNT(*) FROM session"));
    }
}
