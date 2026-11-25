using KeyStats.Core.Input;

namespace KeyStats.Core.Tests;

public sealed class EventRingTests
{
    private static InputEvent Ev(long ms) => new(ms, EventKind.KeyDown, KeyCode.Space);

    [Fact]
    public void Capacite_hors_puissance_de_deux_refusee()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => new EventRing(100));
        Assert.Throws<ArgumentOutOfRangeException>(() => new EventRing(1));
    }

    [Fact]
    public void Ordre_conserve_a_travers_le_bouclage()
    {
        var ring = new EventRing(4);
        var buf = new InputEvent[3];
        long next = 0, expected = 0;
        for (int round = 0; round < 10; round++)
        {
            for (int i = 0; i < 3; i++) Assert.True(ring.TryWrite(Ev(next++)));
            int n = ring.Read(buf);
            Assert.Equal(3, n);
            for (int i = 0; i < n; i++) Assert.Equal(expected++, buf[i].Ms);
        }
        Assert.Equal(0, ring.Count);
        Assert.Equal(0, ring.Dropped);
    }

    [Fact]
    public void File_pleine_refuse_et_compte_sans_ecraser()
    {
        var ring = new EventRing(4);
        for (int i = 0; i < 4; i++) Assert.True(ring.TryWrite(Ev(i)));
        Assert.False(ring.TryWrite(Ev(99)));
        Assert.False(ring.TryWrite(Ev(100)));
        Assert.Equal(2, ring.Dropped);

        var buf = new InputEvent[8];
        Assert.Equal(4, ring.Read(buf));
        Assert.Equal([0L, 1, 2, 3], buf[..4].Select(e => e.Ms));
        Assert.True(ring.TryWrite(Ev(5)));
    }

    [Fact]
    public void Lecture_limitee_a_la_taille_du_tampon()
    {
        var ring = new EventRing(8);
        for (int i = 0; i < 5; i++) ring.TryWrite(Ev(i));
        var buf = new InputEvent[2];
        Assert.Equal(2, ring.Read(buf));
        Assert.Equal(3, ring.Count);
        Assert.Equal(2, ring.Read(buf));
        Assert.Equal(2, buf[0].Ms);
    }

    [Fact]
    public async Task Producteur_et_consommateur_concurrents_sans_perte_ni_desordre()
    {
        const int total = 200_000;
        var ring = new EventRing(64);
        var producer = Task.Run(() =>
        {
            for (long i = 0; i < total;)
                if (ring.TryWrite(Ev(i))) i++;
                else Thread.SpinWait(10);
        }, TestContext.Current.CancellationToken);
        var buf = new InputEvent[16];
        long expected = 0;
        while (expected < total)
        {
            int n = ring.Read(buf);
            for (int i = 0; i < n; i++) Assert.Equal(expected++, buf[i].Ms);
        }
        await producer;
    }
}

public sealed class RawInputDecoderTests
{
    private const ushort VkA = 0x41, VkNumLock = 0x90, VkPause = 0x13;

    [Fact]
    public void Appui_et_relachement_decodes()
    {
        Assert.True(RawInputDecoder.Keyboard(0x10, 0, VkA, true, out var kind, out var key));
        Assert.Equal(EventKind.KeyDown, kind);
        Assert.Equal((ushort)0x10, key);

        Assert.True(RawInputDecoder.Keyboard(0x10, RawInputDecoder.KeyBreak, VkA, true, out kind, out key));
        Assert.Equal(EventKind.KeyUp, kind);
    }

    [Fact]
    public void Prefixe_E0_distingue_les_touches_etendues()
    {
        Assert.True(RawInputDecoder.Keyboard(0x38, RawInputDecoder.KeyE0, 0x12, true, out _, out var key));
        Assert.Equal(KeyCode.RightAlt, key);
        Assert.True(RawInputDecoder.Keyboard(0x1C, RawInputDecoder.KeyE0 | RawInputDecoder.KeyBreak, 0x0D, true, out var kind, out key));
        Assert.Equal(KeyCode.NumEnter, key);
        Assert.Equal(EventKind.KeyUp, kind);
    }

    [Fact]
    public void Frappe_injectee_ignoree()
    {
        Assert.False(RawInputDecoder.Keyboard(0x10, 0, VkA, fromDevice: false, out _, out _));
    }

    [Fact]
    public void Faux_code_virtuel_ignore()
    {
        // Pause arrive en deux messages : E1 1D (gardé), puis 45 avec le code virtuel 0xFF (écarté),
        // qui sinon passerait pour Verr Num.
        Assert.True(RawInputDecoder.Keyboard(0x1D, RawInputDecoder.KeyE1, VkPause, true, out _, out var key));
        Assert.Equal(KeyCode.Pause, key);
        Assert.False(RawInputDecoder.Keyboard(0x45, 0, 0xFF, true, out _, out _));
        Assert.True(RawInputDecoder.Keyboard(0x45, 0, VkNumLock, true, out _, out key));
        Assert.Equal(KeyCode.NumLock, key);
    }

    [Fact]
    public void Touche_sans_scan_code_gardee_par_son_code_virtuel()
    {
        Assert.True(RawInputDecoder.Keyboard(0, 0, 0xAD, true, out _, out var key));
        Assert.Equal((ushort)(KeyCode.VirtualKey | 0xAD), key);
        Assert.False(RawInputDecoder.Keyboard(0, 0, 0, true, out _, out _));
    }

    [Fact]
    public void Boutons_souris_dans_l_ordre_des_bits()
    {
        Span<InputEvent> buf = stackalloc InputEvent[RawInputDecoder.MaxMouseEvents];
        // gauche relâché (0x2), droit appuyé (0x4), X2 appuyé (0x100)
        int n = RawInputDecoder.MouseButtons(0x2 | 0x4 | 0x100, 42, 3, buf);
        Assert.Equal(3, n);
        Assert.Equal((EventKind.MouseUp, MouseButton.Left), (buf[0].Kind, buf[0].Key));
        Assert.Equal((EventKind.MouseDown, MouseButton.Right), (buf[1].Kind, buf[1].Key));
        Assert.Equal((EventKind.MouseDown, MouseButton.X2), (buf[2].Kind, buf[2].Key));
        Assert.All(buf[..n].ToArray(), e => Assert.Equal((42L, (ushort)3), (e.Ms, e.Device)));
    }

    [Fact]
    public void Mouvement_et_molette_ignores()
    {
        Span<InputEvent> buf = stackalloc InputEvent[RawInputDecoder.MaxMouseEvents];
        Assert.Equal(0, RawInputDecoder.MouseButtons(0, 1, 1, buf));
        Assert.Equal(0, RawInputDecoder.MouseButtons(0x400 | 0x800, 1, 1, buf)); // molettes
        Assert.Equal(10, RawInputDecoder.MouseButtons(0x3FF, 1, 1, buf));
    }
}

public sealed class EventClockTests
{
    private long _ticks, _utc;

    private EventClock Clock() => new(() => _ticks, () => _utc, frequency: 10_000);

    [Fact]
    public void Ms_UTC_suivent_le_compteur()
    {
        _utc = 1_700_000_000_000;
        var clock = Clock();
        _ticks += 25_000; // 2,5 s
        Assert.Equal(_utc + 2_500, clock.NowMs());
    }

    [Fact]
    public void Recalage_ne_fait_jamais_reculer_le_temps()
    {
        _utc = 1_000_000;
        var clock = Clock();
        _ticks += 50_000;
        Assert.Equal(1_005_000, clock.NowMs());

        _utc = 1_002_000; // horloge système remise en arrière
        clock.Resync();
        Assert.Equal(1_005_000, clock.NowMs());
        _ticks += 40_000;
        Assert.Equal(1_006_000, clock.NowMs());

        _utc = 2_000_000; // et en avant
        clock.Resync();
        Assert.Equal(2_000_000, clock.NowMs());
    }
}
