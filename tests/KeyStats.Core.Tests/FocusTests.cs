using KeyStats.Core.Analysis;
using KeyStats.Core.Focus;
using KeyStats.Core.Input;

namespace KeyStats.Core.Tests;

public sealed class ExclusionListTests
{
    [Theory]
    [InlineData("KeePassXC.exe")]
    [InlineData("keepassxc.EXE")]
    [InlineData(@"C:\Program Files\KeePassXC\KeePassXC.exe")]
    [InlineData("C:/Apps/mstsc.exe")]
    public void Reconnait_le_nom_quel_que_soit_le_dossier_et_la_casse(string exe) =>
        Assert.True(ExclusionList.Default.Contains(exe));

    [Fact]
    public void Ignore_les_autres_et_les_entrees_vides()
    {
        var list = new ExclusionList(["", @"C:\x\", "  Foo.exe "]);
        Assert.Equal(1, list.Count);
        Assert.True(list.Contains("foo.exe"));
        Assert.False(list.Contains("notepad.exe"));
        Assert.False(ExclusionList.Default.Contains("Code.exe"));
    }
}

public sealed class FocusTrackerTests
{
    private readonly FocusTracker _tracker = new(ExclusionList.Default);

    private FocusChange? See(long ms, long window, string exe, string title, bool elevated = false, int pid = 10) =>
        _tracker.Observe(new ForegroundWindow(ms, window, pid, exe, title, elevated));

    [Fact]
    public void Premiere_lecture_puis_doublon()
    {
        var c = See(1, 100, "Code.exe", "a.cs");
        Assert.NotNull(c);
        Assert.Equal(FocusState.NewWindow, c.Flags);
        Assert.Equal("a.cs", c.Title);
        Assert.Null(See(2, 100, "Code.exe", "a.cs"));
    }

    [Fact]
    public void Changement_de_titre_sans_nouvelle_fenetre()
    {
        See(1, 100, "Code.exe", "a.cs");
        var c = See(2, 100, "Code.exe", "b.cs");
        Assert.NotNull(c);
        Assert.Equal(FocusState.None, c.Flags);
        Assert.Equal("b.cs", c.Title);
        Assert.Equal(2, c.Ms);
    }

    [Fact]
    public void Autre_fenetre_ou_autre_processus_meme_titre()
    {
        See(1, 100, "Code.exe", "a.cs");
        Assert.Equal(FocusState.NewWindow, See(2, 200, "Code.exe", "a.cs")!.Flags);
        Assert.Equal(FocusState.NewWindow, See(3, 200, "Code.exe", "a.cs", pid: 11)!.Flags);
    }

    [Fact]
    public void Appli_exclue_sans_titre_ni_changement_de_titre()
    {
        See(1, 100, "Code.exe", "a.cs");
        var c = See(2, 300, @"C:\Apps\KeePassXC.exe", "Base perso - KeePassXC");
        Assert.NotNull(c);
        Assert.Equal(FocusState.NewWindow | FocusState.Excluded, c.Flags);
        Assert.Null(c.Title);
        Assert.True(c.Flags.CountersOnly());
        Assert.Null(See(3, 300, @"C:\Apps\KeePassXC.exe", "Autre entrée"));
        Assert.DoesNotContain("Base", c.ToString(), StringComparison.Ordinal);
    }

    [Fact]
    public void Fenetre_administrateur_et_elevation_qui_change()
    {
        var c = See(1, 100, "cmd.exe", "Admin", elevated: true);
        Assert.Equal(FocusState.NewWindow | FocusState.Elevated, c!.Flags);
        var d = See(2, 100, "cmd.exe", "Admin", elevated: false);
        Assert.Equal(FocusState.None, d!.Flags);
    }

    [Fact]
    public void ToString_ne_contient_pas_le_titre()
    {
        var c = See(1, 100, "Code.exe", "secret-projet.cs")!;
        Assert.DoesNotContain("secret", c.ToString(), StringComparison.Ordinal);
    }
}

public sealed class FocusChannelTests
{
    private static FocusChange Change(long ms) =>
        new() { Ms = ms, Exe = "a.exe", Title = "t", Flags = FocusState.NewWindow };

    private static InputEvent[] Drain(EventRing ring)
    {
        var buf = new InputEvent[ring.Capacity];
        return buf[..ring.Read(buf)];
    }

    [Fact]
    public void Le_marqueur_garde_sa_place_parmi_les_frappes()
    {
        var ring = new EventRing(8);
        var channel = new FocusChannel();
        ring.TryWrite(new InputEvent(1, EventKind.KeyDown, KeyCode.Space));
        var a = Change(2);
        Assert.True(channel.Publish(a, ring));
        ring.TryWrite(new InputEvent(3, EventKind.KeyDown, KeyCode.Space));
        var events = Drain(ring);

        Assert.Equal([EventKind.KeyDown, EventKind.Focus, EventKind.KeyDown], events.Select(e => e.Kind));
        Assert.Same(a, channel.Take(events[1]));
        Assert.Null(channel.Take(events[1]));
    }

    [Fact]
    public void Marqueur_refuse_puis_saute_au_suivant()
    {
        var ring = new EventRing(2);
        var channel = new FocusChannel();
        ring.TryWrite(new InputEvent(0, EventKind.KeyDown, KeyCode.Space));
        ring.TryWrite(new InputEvent(0, EventKind.KeyUp, KeyCode.Space));
        Assert.False(channel.Publish(Change(1), ring));
        Drain(ring);
        var b = Change(2);
        Assert.True(channel.Publish(b, ring));

        Assert.Same(b, channel.Take(Drain(ring)[0]));
        Assert.Null(channel.TakeLatest());
    }

    [Fact]
    public void Numeros_qui_bouclent_sur_16_bits()
    {
        var ring = new EventRing(4);
        var channel = new FocusChannel();
        for (int i = 0; i < 70_000; i++)
        {
            var c = Change(i);
            channel.Publish(c, ring);
            Assert.Same(c, channel.Take(Drain(ring)[0]));
        }
    }

    [Fact]
    public void Dernier_changement_apres_une_perte()
    {
        var ring = new EventRing(2);
        var channel = new FocusChannel();
        channel.Publish(Change(1), ring);
        channel.Publish(Change(2), ring);
        var last = Change(3);
        Assert.False(channel.Publish(last, ring));
        Assert.Same(last, channel.TakeLatest());
        Assert.Null(channel.TakeLatest());
    }
}
