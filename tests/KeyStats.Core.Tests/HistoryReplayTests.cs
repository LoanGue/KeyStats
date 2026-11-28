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
        Assert.Equal(DayReplay.Done, replay.ReplayDay(Day, master));

        Assert.Equal(2, tmp.Count("SELECT COUNT(*) FROM session"));
        Assert.Equal(keys, tmp.Count("SELECT SUM(keys) FROM minute_total"));
        Assert.Equal(DayReplay.Done, replay.ReplayDay(Day, master));
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

        Assert.Equal(DayReplay.Done, new HistoryReplay(tmp.Db, Clock, AnalysisSettings.Default).ReplayDay(Day, master));

        Assert.Equal(excluded, tmp.Count("SELECT SUM(excluded_keys) FROM minute_total"));
    }

    [Fact]
    public void Frappes_d_apres_minuit_d_un_bloc_commence_la_veille()
    {
        using var tmp = new TempDb();
        using var master = MasterKey.Generate();
        long midnight = Clock.MidnightUtcMs(new DateTime(2026, 11, 3));
        var rec = Recorder(tmp, master);
        rec.OnFocus(Focus(midnight - 20_000, "winword.exe"));
        long end = Feed(rec, Text + Text, midnight - 15_000);
        Assert.True(rec.Close(end));
        Assert.True(tmp.Count($"SELECT COUNT(*) FROM raw_block WHERE start_ms < {midnight} AND end_ms >= {midnight}") > 0);
        long after = tmp.Count($"SELECT SUM(keys) FROM minute_total WHERE ts_min >= {midnight / 60_000}");
        long before = tmp.Count($"SELECT SUM(keys) FROM minute_total WHERE ts_min < {midnight / 60_000}");

        var replay = new HistoryReplay(tmp.Db, Clock, AnalysisSettings.Default);
        Assert.Equal(DayReplay.Done, replay.ReplayDay(20261103, master));

        Assert.Equal(after, tmp.Count($"SELECT SUM(keys) FROM minute_total WHERE ts_min >= {midnight / 60_000}"));
        Assert.Equal(before, tmp.Count($"SELECT SUM(keys) FROM minute_total WHERE ts_min < {midnight / 60_000}"));
    }

    [Fact]
    public void Jour_sans_brut_garde_tel_quel()
    {
        using var tmp = new TempDb();
        var plain = Recorder(tmp, null);
        Assert.True(plain.Close(RecordTwoPhrases(plain, Typist.Start)));
        using var master = MasterKey.Generate();
        var replay = new HistoryReplay(tmp.Db, Clock, new AnalysisSettings { SessionGapMs = 2_000 });

        Assert.Equal(DayReplay.NoRaw, replay.ReplayDay(Day, master));

        var rec = new Recorder(tmp.Db, new RawArchiveWriter(tmp.Db, master.PublicKey, new TitleHasher(TitleHasher.NewKey())),
            Clock, Typist.Start + 3_600_000);
        Assert.True(rec.Close(RecordTwoPhrases(rec, Typist.Start + 3_600_000)));
        Assert.Equal(DayReplay.NoRaw, replay.ReplayDay(Day, master));
        Assert.Equal(2, tmp.Count("SELECT COUNT(*) FROM session"));
    }
}
