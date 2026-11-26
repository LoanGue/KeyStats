using KeyStats.Core.Recording;
using KeyStats.Core.Storage;

namespace KeyStats.Core.Tests;

public sealed class PauseSourcesTests
{
    private const long T = 1_700_000_000_000;
    private const long Min = 60_000;

    private static List<RecorderCommand> Take(RecorderCommands commands)
    {
        var list = new List<RecorderCommand>();
        while (commands.TryTake(long.MaxValue, out var c)) list.Add(c);
        return list;
    }

    [Fact]
    public void Trois_clics_quinze_minutes_puis_reprise_a_l_heure_prevue()
    {
        var commands = new RecorderCommands();
        var p = new PauseSources(commands);
        Assert.Equal(PauseTransition.Paused, p.Snooze(T));
        Assert.Equal(PauseTransition.None, p.Snooze(T + 1000));
        Assert.Equal(PauseTransition.None, p.Snooze(T + 2000));
        Assert.Equal(T + 15 * Min, p.SnoozeUntil);
        Assert.Equal(PauseTransition.None, p.Tick(T + 15 * Min - 1));

        // La minuterie s'en aperçoit un peu après : la reprise est datée de la fin prévue.
        Assert.Equal(PauseTransition.Resumed, p.Tick(T + 15 * Min + 700));
        Assert.Equal(
            [new RecorderCommand(T, CommandKind.Pause, PauseReason.Snooze), new RecorderCommand(T + 15 * Min, CommandKind.Resume)],
            Take(commands));
        Assert.Equal(0, p.SnoozeUntil);
        Assert.Equal((PauseReason)0, p.Reason);
    }

    [Fact]
    public void Verrouillage_pendant_la_pause_oublie_l_epoque_et_la_pause_reprend_au_deverrouillage()
    {
        var commands = new RecorderCommands();
        var p = new PauseSources(commands);
        p.Snooze(T);
        Assert.Equal(PauseTransition.Changed, p.SetLocked(true, T + Min));
        Assert.Equal(PauseTransition.Changed, p.SetLocked(false, T + 2 * Min));
        Assert.Equal(PauseReason.Snooze, p.Reason);
        Assert.Equal(
            [
                new RecorderCommand(T, CommandKind.Pause, PauseReason.Snooze),
                new RecorderCommand(T + Min, CommandKind.Pause, PauseReason.Locked),
                new RecorderCommand(T + Min, CommandKind.DropEpoch),
                new RecorderCommand(T + 2 * Min, CommandKind.Pause, PauseReason.Snooze),
            ],
            Take(commands));
    }

    [Fact]
    public void Pause_echue_pendant_le_verrouillage_reprise_au_deverrouillage()
    {
        var commands = new RecorderCommands();
        var p = new PauseSources(commands);
        p.Snooze(T);
        p.SetLocked(true, T + Min);
        Assert.Equal(PauseTransition.None, p.Tick(T + 6 * Min));
        Take(commands);
        Assert.Equal(PauseTransition.Resumed, p.SetLocked(false, T + 30 * Min));
        Assert.Equal([new RecorderCommand(T + 30 * Min, CommandKind.Resume)], Take(commands));
    }

    [Fact]
    public void Verrouillage_puis_veille_une_seule_pause()
    {
        var commands = new RecorderCommands();
        var p = new PauseSources(commands);
        Assert.Equal(PauseTransition.Paused, p.SetLocked(true, T));
        Assert.Equal(PauseTransition.None, p.SetSuspended(true, T + Min));
        Assert.Equal(PauseTransition.None, p.SetSuspended(false, T + 2 * Min));
        Assert.Equal(PauseTransition.Resumed, p.SetLocked(false, T + 3 * Min));
        Assert.Equal(
            [
                new RecorderCommand(T, CommandKind.Pause, PauseReason.Locked),
                new RecorderCommand(T, CommandKind.DropEpoch),
                new RecorderCommand(T + 3 * Min, CommandKind.Resume),
            ],
            Take(commands));
    }

    [Fact]
    public void Pause_jusqu_a_reprise_manuelle()
    {
        var commands = new RecorderCommands();
        var p = new PauseSources(commands);
        Assert.Equal(PauseTransition.Paused, p.SnoozeUntilResumedManually(T));
        Assert.Equal(PauseTransition.None, p.Snooze(T + 1000));
        Assert.Equal(PauseTransition.None, p.Tick(T + 1000 * Min));
        Assert.Equal(PauseSources.UntilResumed, p.SnoozeUntil);
        Assert.Equal(PauseTransition.Resumed, p.ResumeNow(T + 1001 * Min));
        Assert.Equal(
            [new RecorderCommand(T, CommandKind.Pause, PauseReason.Snooze), new RecorderCommand(T + 1001 * Min, CommandKind.Resume)],
            Take(commands));
    }

    [Fact]
    public void Reprendre_maintenant_ne_leve_pas_le_verrouillage()
    {
        var commands = new RecorderCommands();
        var p = new PauseSources(commands);
        p.SetLocked(true, T);
        Assert.Equal(PauseTransition.None, p.ResumeNow(T + 1000));
        Assert.Equal(PauseReason.Locked, p.Reason);
    }

    [Fact]
    public void Clic_apres_une_pause_echue_en_ouvre_une_nouvelle()
    {
        var commands = new RecorderCommands();
        var p = new PauseSources(commands);
        p.Snooze(T);
        // Aucune minuterie n'est passée entre la fin prévue et le clic.
        Assert.Equal(PauseTransition.Paused, p.Snooze(T + 7 * Min));
        Assert.Equal(T + 12 * Min, p.SnoozeUntil);
        Assert.Equal(
            [
                new RecorderCommand(T, CommandKind.Pause, PauseReason.Snooze),
                new RecorderCommand(T + 5 * Min, CommandKind.Resume),
                new RecorderCommand(T + 7 * Min, CommandKind.Pause, PauseReason.Snooze),
            ],
            Take(commands));
    }

    [Fact]
    public void Veille_seule_oublie_l_epoque_et_reprend_au_reveil()
    {
        var commands = new RecorderCommands();
        var p = new PauseSources(commands);
        Assert.Equal(PauseTransition.Paused, p.SetSuspended(true, T));
        Assert.Equal(PauseTransition.Resumed, p.SetSuspended(false, T + 60 * Min));
        Assert.Equal(
            [
                new RecorderCommand(T, CommandKind.Pause, PauseReason.Locked),
                new RecorderCommand(T, CommandKind.DropEpoch),
                new RecorderCommand(T + 60 * Min, CommandKind.Resume),
            ],
            Take(commands));
    }

    [Fact]
    public void Pause_echue_pendant_la_veille_reprise_au_reveil()
    {
        var commands = new RecorderCommands();
        var p = new PauseSources(commands);
        p.Snooze(T);
        p.SetSuspended(true, T + Min);
        Take(commands);
        Assert.Equal(PauseTransition.Resumed, p.SetSuspended(false, T + 60 * Min));
        Assert.Equal([new RecorderCommand(T + 60 * Min, CommandKind.Resume)], Take(commands));
    }

    [Fact]
    public void Pause_echue_non_vue_puis_verrouillage()
    {
        var commands = new RecorderCommands();
        var p = new PauseSources(commands);
        p.Snooze(T);
        Assert.Equal(PauseTransition.Paused, p.SetLocked(true, T + 7 * Min));
        Assert.Equal(
            [
                new RecorderCommand(T, CommandKind.Pause, PauseReason.Snooze),
                new RecorderCommand(T + 5 * Min, CommandKind.Resume),
                new RecorderCommand(T + 7 * Min, CommandKind.Pause, PauseReason.Locked),
                new RecorderCommand(T + 7 * Min, CommandKind.DropEpoch),
            ],
            Take(commands));
    }

    [Fact]
    public void Pause_echue_non_vue_puis_reprendre_maintenant()
    {
        var commands = new RecorderCommands();
        var p = new PauseSources(commands);
        p.Snooze(T);
        Assert.Equal(PauseTransition.Resumed, p.ResumeNow(T + 7 * Min));
        Assert.Equal(
            [new RecorderCommand(T, CommandKind.Pause, PauseReason.Snooze), new RecorderCommand(T + 5 * Min, CommandKind.Resume)],
            Take(commands));
    }

    [Fact]
    public void Pause_echue_non_vue_puis_pause_sans_fin()
    {
        var commands = new RecorderCommands();
        var p = new PauseSources(commands);
        p.Snooze(T);
        Assert.Equal(PauseTransition.Paused, p.SnoozeUntilResumedManually(T + 7 * Min));
        Assert.Equal(
            [
                new RecorderCommand(T, CommandKind.Pause, PauseReason.Snooze),
                new RecorderCommand(T + 5 * Min, CommandKind.Resume),
                new RecorderCommand(T + 7 * Min, CommandKind.Pause, PauseReason.Snooze),
            ],
            Take(commands));
    }
}
