using KeyStats.Core.Analysis;
using KeyStats.Core.Focus;
using KeyStats.Core.Input;
using KeyStats.Core.Recording;
using KeyStats.Core.Storage;

namespace KeyStats.Core.Tests;

public sealed class RecorderCommandsTests
{
    private const string Text = "bonjour tout le monde, ceci est un essai de frappe assez long ";

    private sealed record Rig(EventRing Ring, RecorderCommands Commands, Recorder Recorder, RingPump Pump, LocalDayClock Clock);

    private static Rig Setup(TempDb tmp)
    {
        var ring = new EventRing(1024);
        var channel = new FocusChannel();
        var clock = new LocalDayClock(TimeZoneInfo.Utc);
        var rec = new Recorder(tmp.Db, null, clock, Typist.Start);
        var commands = new RecorderCommands();
        channel.Publish(new FocusChange { Ms = Typist.Start, Exe = "winword.exe", Title = null, Flags = FocusState.NewWindow }, ring);
        return new Rig(ring, commands, rec, new RingPump(ring, channel, rec, commands), clock);
    }

    private static (long End, int Count) Write(EventRing ring, string text, long startMs)
    {
        var typist = new Typist(startMs).Type(text);
        foreach (var e in typist.Events) Assert.True(ring.TryWrite(e));
        return (typist.Ms, typist.Events.Count);
    }

    [Fact]
    public void Pause_appliquee_a_sa_place_parmi_les_frappes()
    {
        using var tmp = new TempDb();
        var r = Setup(tmp);
        var (end, _) = Write(r.Ring, Text, Typist.Start + 10);
        long pause = end + 100;
        r.Commands.Post(new RecorderCommand(pause, CommandKind.Pause, PauseReason.Locked));
        var (end2, paused) = Write(r.Ring, Text, pause + 100);
        long resume = end2 + 100;
        r.Commands.Post(new RecorderCommand(resume, CommandKind.Resume));
        var (end3, _) = Write(r.Ring, Text, resume + 100);
        r.Pump.Drain();
        Assert.True(r.Pump.Close(end3 + 1000));

        Assert.Equal(paused, r.Recorder.Counters.PausedEvents);
        Assert.Equal(1, tmp.Count($"SELECT COUNT(*) FROM pause WHERE reason = {(int)PauseReason.Locked} AND start_ms = {pause} AND end_ms = {resume}"));
        Assert.Equal(2, tmp.Count("SELECT COUNT(*) FROM session"));
    }

    [Fact]
    public void Tick_laisse_passer_d_abord_une_frappe_encore_en_route()
    {
        using var tmp = new TempDb();
        var r = Setup(tmp);
        long t = Typist.Start + 10_000;
        r.Commands.Post(new RecorderCommand(t, CommandKind.Pause, PauseReason.Locked));
        r.Pump.Tick(t + RingPump.TickLagMs - 1);
        Assert.False(r.Commands.IsEmpty);

        // Horodatée avant la pause, écrite après : elle compte.
        Assert.True(r.Ring.TryWrite(new InputEvent(t - 50, EventKind.KeyDown, KeyCode.Space, 1)));
        r.Pump.Drain();
        Assert.Equal(0, r.Recorder.Counters.PausedEvents);
        Assert.True(r.Ring.TryWrite(new InputEvent(t + 10, EventKind.KeyDown, KeyCode.Space, 1)));
        r.Pump.Drain();
        Assert.Equal(1, r.Recorder.Counters.PausedEvents);
        Assert.True(r.Commands.IsEmpty);
    }

    [Fact]
    public void Fermeture_applique_les_commandes_en_attente()
    {
        using var tmp = new TempDb();
        var r = Setup(tmp);
        var paris = TimeZoneInfo.FindSystemTimeZoneById("Europe/Paris");
        long t = Typist.Start + 10_000;
        r.Commands.Post(new RecorderCommand(t, CommandKind.SetZone, Zone: paris));
        Assert.True(r.Pump.Close(t));

        Assert.Same(paris, r.Clock.Zone);
        Assert.True(r.Commands.IsEmpty);
    }

    [Fact]
    public void Erreur_comptee_pour_la_commande_fautive_seule()
    {
        using var tmp = new TempDb();
        var r = Setup(tmp);
        var (end, _) = Write(r.Ring, Text, Typist.Start + 10);
        r.Pump.Drain();
        r.Commands.Post(new RecorderCommand(end + 10, CommandKind.Flush));
        r.Commands.Post(new RecorderCommand(end + 20, CommandKind.Pause, PauseReason.Snooze));
        // Transaction ouverte : l'écriture du Flush lève, la pause passe quand même.
        using (tmp.Db.BeginTransaction())
            r.Pump.Tick(end + 20 + RingPump.TickLagMs);
        Assert.True(r.Ring.TryWrite(new InputEvent(end + 30, EventKind.KeyDown, KeyCode.Space, 1)));
        r.Pump.Drain();

        Assert.Equal(1, r.Pump.Counters.Errors);
        Assert.Equal(1, r.Recorder.Counters.PausedEvents);
    }

    [Fact]
    public void Commande_incomplete_refusee_a_l_envoi()
    {
        var commands = new RecorderCommands();
        Assert.Throws<ArgumentOutOfRangeException>(() => commands.Post(new RecorderCommand(1, CommandKind.Pause)));
        Assert.Throws<ArgumentOutOfRangeException>(() => commands.Post(new RecorderCommand(1, (CommandKind)99)));
        Assert.Throws<ArgumentNullException>(() => commands.Post(new RecorderCommand(1, CommandKind.SetZone)));
        Assert.True(commands.IsEmpty);
    }
}
