using KeyStats.Core.Analysis;
using KeyStats.Core.Focus;
using KeyStats.Core.Input;
using KeyStats.Core.Recording;

namespace KeyStats.Core.Tests;

public sealed class RingWriterTests
{
    private static InputEvent Key(long ms) => new(ms, EventKind.KeyDown, KeyCode.Space);

    private static InputEvent[] Drain(EventRing ring)
    {
        var buf = new InputEvent[ring.Capacity];
        return buf[..ring.Read(buf)];
    }

    [Fact]
    public void Apres_une_perte_la_remise_a_zero_prend_la_place_du_trou()
    {
        var ring = new EventRing(4);
        var writer = new RingWriter(ring, () => LockKeys.CapsLock);
        for (int i = 0; i < 4; i++) Assert.True(writer.Write(Key(i)));
        Assert.False(writer.Write(Key(4)));
        Assert.True(writer.ResetOwed);

        Assert.Equal(4, Drain(ring).Length);
        Assert.True(writer.Write(Key(5)));
        var events = Drain(ring);

        Assert.Equal([EventKind.Reset, EventKind.KeyDown], events.Select(e => e.Kind));
        Assert.Equal(5, events[0].Ms);
        Assert.Equal((byte)LockKeys.CapsLock, events[0].Flags);
        Assert.False(writer.ResetOwed);
    }

    [Fact]
    public void Une_seule_place_l_evenement_est_perdu_et_la_remise_reste_due()
    {
        var ring = new EventRing(2);
        var writer = new RingWriter(ring, () => LockKeys.None);
        writer.MarkLost();
        Assert.True(writer.Write(Key(1))); // remise + événement : file pleine
        Assert.False(writer.Write(Key(2)));
        Assert.Equal(1, ring.Read(new InputEvent[1]));

        Assert.False(writer.Write(Key(3))); // une place, il en faut deux
        Assert.True(writer.ResetOwed);
        Assert.Equal(2, ring.Dropped);
        Assert.Equal(1, ring.Count); // rien d'écrit : pas de remise orpheline
    }

    [Fact]
    public void Marqueur_perdu_remise_a_zero_avant_la_frappe_suivante()
    {
        var ring = new EventRing(8);
        var writer = new RingWriter(ring, () => LockKeys.NumLock);
        writer.MarkLost();
        writer.Write(Key(7));

        Assert.Equal([EventKind.Reset, EventKind.KeyDown], Drain(ring).Select(e => e.Kind));
    }
}

public sealed class EventClockPeekTests
{
    private long _ticks, _utc = 1_000_000;

    [Fact]
    public void Lecture_d_un_autre_thread_sur_la_meme_echelle()
    {
        var clock = new EventClock(() => _ticks, () => _utc, frequency: 10_000);
        _ticks += 20_000;
        Assert.Equal(1_002_000, clock.PeekMs());
        Assert.Equal(1_002_000, clock.NowMs());

        _utc = 5_000_000;
        clock.Resync();
        Assert.Equal(5_000_000, clock.PeekMs());
        Assert.Equal(5_000_000, clock.NowMs());
    }

    [Fact]
    public void Recalage_en_arriere_apres_une_lecture_sans_frappe_le_temps_ne_recule_pas()
    {
        var clock = new EventClock(() => _ticks, () => _utc, frequency: 10_000);
        _ticks += 360_000; // 36 s sans frappe, lues par l'autre thread
        long peek = clock.PeekMs();
        Assert.Equal(1_036_000, peek);

        _utc = 1_000_000; // horloge système reculée
        clock.Resync();
        Assert.True(clock.PeekMs() >= peek);
        Assert.True(clock.NowMs() >= peek);
    }
}

public sealed class RingPumpTests
{
    private const string Text = "bonjour tout le monde, ceci est un essai de frappe assez long ";

    private static FocusChange Focus(long ms, string exe, FocusState flags = FocusState.NewWindow) =>
        new() { Ms = ms, Exe = exe, Title = null, Flags = flags };

    private static (EventRing Ring, FocusChannel Channel, Recorder Recorder, RingPump Pump) Setup(TempDb tmp)
    {
        var ring = new EventRing(512);
        var channel = new FocusChannel();
        var rec = new Recorder(tmp.Db, null, new LocalDayClock(TimeZoneInfo.Utc), Typist.Start);
        return (ring, channel, rec, new RingPump(ring, channel, rec));
    }

    private static long Write(EventRing ring, string text, long startMs)
    {
        var typist = new Typist(startMs).Type(text);
        foreach (var e in typist.Events) Assert.True(ring.TryWrite(e));
        return typist.Ms;
    }

    [Fact]
    public void Marqueurs_remplaces_par_la_fenetre_et_inconnus_comptes()
    {
        using var tmp = new TempDb();
        var (ring, channel, rec, pump) = Setup(tmp);
        Assert.True(channel.Publish(Focus(Typist.Start, "winword.exe"), ring));
        long end = Write(ring, Text, Typist.Start + 10);
        ring.TryWrite(new InputEvent(end, EventKind.Focus, 999));
        int written = ring.Count;
        pump.Drain();
        Assert.True(pump.Close(end + 1000));

        Assert.Equal(new PumpCounters(written, 0, 1), pump.Counters);
        Assert.Equal(1, tmp.Count("SELECT COUNT(*) FROM focus"));
        Assert.Equal(1, tmp.Count("SELECT COUNT(*) FROM session"));
    }

    [Fact]
    public void Erreur_comptee_pour_l_evenement_fautif_seul()
    {
        using var tmp = new TempDb();
        var (ring, channel, rec, pump) = Setup(tmp);
        channel.Publish(Focus(Typist.Start, "winword.exe"), ring);
        pump.Drain();
        // Écriture refusée par une transaction ouverte : OnFocus lève, les frappes suivantes passent.
        channel.Publish(Focus(Typist.Start + 1, "chrome.exe", FocusState.NewWindow | FocusState.Secret), ring);
        long end = Write(ring, Text, Typist.Start + 10);
        using (tmp.Db.BeginTransaction())
            pump.Drain();
        Assert.True(pump.Close(end + 1000));

        Assert.Equal(1, pump.Counters.Errors);
        Assert.True(tmp.Count("SELECT SUM(excluded_keys) FROM minute_total") > 0);
        Assert.Equal(0, tmp.Count("SELECT COUNT(*) FROM session"));
    }

    [Fact]
    public void Tick_en_retard_et_jamais_avant_le_dernier_evenement()
    {
        using var tmp = new TempDb();
        var (ring, channel, rec, pump) = Setup(tmp);
        channel.Publish(Focus(Typist.Start, "winword.exe"), ring);
        Write(ring, Text, Typist.Start + 10);
        pump.Drain();
        long batches = rec.Counters.Batches;

        pump.Tick(Typist.Start + Recorder.FlushIntervalMs + RingPump.TickLagMs - 1);
        Assert.Equal(batches, rec.Counters.Batches);
        pump.Tick(Typist.Start + Recorder.FlushIntervalMs + RingPump.TickLagMs);
        Assert.Equal(batches + 1, rec.Counters.Batches);
        // Horloge en arrière : l'heure reste celle du dernier événement, le Recorder ne recule pas
        // et n'écrit pas 60 s après l'heure reculée.
        Write(ring, Text, Typist.Start + Recorder.FlushIntervalMs + 100);
        pump.Drain();
        pump.Tick(Typist.Start);
        pump.Tick(Typist.Start + Recorder.FlushIntervalMs);
        Assert.Equal(batches + 1, rec.Counters.Batches);
    }
}
