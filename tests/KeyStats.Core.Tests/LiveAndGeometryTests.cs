using KeyStats.Core.Analysis;
using KeyStats.Core.Input;
using KeyStats.Core.Text;

namespace KeyStats.Core.Tests;

public sealed class LiveMeterTests
{
    private const long T0 = 1_000_000;

    /// <summary>Tape <paramref name="text"/> à intervalle régulier ; renvoie l'heure du dernier caractère.</summary>
    private static long Type(LiveMeter meter, string text, long start, int stepMs)
    {
        long ms = start;
        foreach (char c in text)
        {
            meter.OnChar(ms, c);
            ms += stepMs;
        }
        return ms - stepMs;
    }

    [Fact]
    public void Frappes_isolees_ne_montrent_pas_le_compteur()
    {
        var meter = new LiveMeter();
        long last = Type(meter, "zqsdz", T0, 1500);
        Assert.Equal(LiveMode.Hidden, meter.Read(last + 100).Mode);
    }

    [Fact]
    public void Texte_regulier_a_200_ms_par_caractere_donne_60_wpm()
    {
        var meter = new LiveMeter();
        long last = Type(meter, "bonjour tout le monde ici", T0, 200);

        var reading = meter.Read(last + 100);
        Assert.Equal(LiveMode.Typing, reading.Mode);
        Assert.Equal(300, reading.Cpm, 1);
        Assert.Equal(60, reading.Wpm, 1);
        Assert.Equal(1, reading.Accuracy);
    }

    [Fact]
    public void Bilan_apres_4_s_sans_frappe_puis_disparition()
    {
        var meter = new LiveMeter();
        long last = Type(meter, "bonjour tout le monde ici", T0, 200);
        meter.OnTypos(1);

        var summary = meter.Read(last + LiveMeter.IdleMs + 500);
        Assert.Equal(LiveMode.Summary, summary.Mode);
        Assert.Equal(1 - 1.0 / 25, summary.Accuracy, 6);
        Assert.Equal(LiveMode.Hidden, meter.Read(last + LiveMeter.IdleMs + LiveMeter.SummaryMs + 1).Mode);
    }

    [Fact]
    public void Pause_au_dela_de_2_s_exclue_de_la_vitesse()
    {
        var meter = new LiveMeter();
        long last = Type(meter, "bonjour tout", T0, 200);
        last = Type(meter, " le monde ici", last + 3000, 200);

        Assert.Equal(300, meter.Read(last + 100).Cpm, 1);
    }

    [Fact]
    public void Reset_cache_le_compteur()
    {
        var meter = new LiveMeter();
        long last = Type(meter, "bonjour tout le monde ici", T0, 200);
        meter.Reset();
        Assert.Equal(LiveMode.Hidden, meter.Read(last + 100).Mode);
    }

    [Fact]
    public void Apparition_signalee_une_fois_par_rafale()
    {
        var meter = new LiveMeter();
        int shown = 0;
        meter.Shown = () => shown++;
        long last = Type(meter, "bonjour tout le monde ici", T0, 200);
        Assert.Equal(1, shown);

        Assert.Equal(LiveMode.Hidden, meter.Read(last + LiveMeter.IdleMs + LiveMeter.SummaryMs + 1).Mode);
        Type(meter, "et encore une phrase ici", last + 20_000, 200);
        Assert.Equal(2, shown);
    }

    [Fact]
    public void Reprise_pendant_le_bilan_ne_signale_pas_de_nouveau()
    {
        var meter = new LiveMeter();
        int shown = 0;
        meter.Shown = () => shown++;
        long last = Type(meter, "bonjour tout le monde ici", T0, 200);
        long resume = last + LiveMeter.IdleMs + 1000;
        Assert.Equal(LiveMode.Summary, meter.Read(resume).Mode);
        Type(meter, "et encore une phrase ici", resume, 200);
        Assert.Equal(1, shown);
    }

    [Fact]
    public void Frappes_isolees_ne_signalent_rien()
    {
        var meter = new LiveMeter();
        int shown = 0;
        meter.Shown = () => shown++;
        Type(meter, "zqsdz", T0, 1500);
        Assert.Equal(0, shown);
    }
}

public sealed class LiveDisplayTests
{
    [Fact]
    public void Texte_pendant_la_frappe_et_au_bilan()
    {
        Assert.Equal(new LiveText("63 wpm", "315 cpm"), LiveDisplay.Text(LiveMode.Typing, 62.6, 314.8, 0.9));
        Assert.Equal(new LiveText("63 wpm · 96\u00A0%", "bilan"), LiveDisplay.Text(LiveMode.Summary, 63.4, 317, 0.958));
        Assert.Equal(new LiveText("", ""), LiveDisplay.Text(LiveMode.Hidden, 60, 300, 1));
    }

    [Fact]
    public void Image_lissee_pendant_la_frappe_bilan_tel_quel()
    {
        var smoother = new LiveSmoother();
        Assert.Equal(new LiveText("60 wpm", "300 cpm"), LiveDisplay.Frame(new(LiveMode.Typing, 60, 300, 1), smoother, 0));
        // Une constante de temps plus tard, 63 % du chemin vers 80 : wpm et cpm suivent ensemble.
        Assert.Equal(new LiveText("73 wpm", "363 cpm"), LiveDisplay.Frame(new(LiveMode.Typing, 80, 400, 1), smoother, 1000));
        Assert.Equal(LiveDisplay.Text(LiveMode.Summary, 80, 400, 0.95), LiveDisplay.Frame(new(LiveMode.Summary, 80, 400, 0.95), smoother, 1250));
    }

    [Fact]
    public void Image_sans_vitesse_garde_le_cpm_lu()
    {
        var smoother = new LiveSmoother();
        Assert.Equal(new LiveText("0 wpm", "12 cpm"), LiveDisplay.Frame(new(LiveMode.Typing, 0, 12, 1), smoother, 0));
    }

    [Fact]
    public void Coins_arrondis_lisses_sur_un_pixel()
    {
        // Rayon 10 dans 40 × 20 : centre de l'arrondi en (10, 10), pixel à cheval sur le bord.
        float edge = LiveImage.RoundRectCoverage(10 - 7.07f, 10 - 7.07f, 40, 20, 10);
        Assert.InRange(edge, 0.3f, 0.7f);
        Assert.Equal(0f, LiveImage.RoundRectCoverage(0.5f, 0.5f, 40, 20, 10));
        Assert.Equal(1f, LiveImage.RoundRectCoverage(20, 10, 40, 20, 10));
    }

    [Fact]
    public void Image_trop_petite_refusee() =>
        Assert.Throws<ArgumentOutOfRangeException>(() => LiveImage.Compose(new uint[10], 4, 4));

    [Fact]
    public void Image_fond_arrondi_et_texte_opaque_en_alpha_premultiplie()
    {
        const int w = 40, h = 20;
        var pixels = new uint[w * h];
        pixels[10 * w + 20] = 0xFFFFFF;
        LiveImage.Compose(pixels, w, h);

        Assert.Equal(0u, pixels[0]);
        Assert.Equal(0u, pixels[w * h - 1]);
        Assert.Equal(0xFFFFFFFFu, pixels[10 * w + 20]);
        uint bg = pixels[10 * w + 5];
        Assert.Equal((uint)(0.86f * 255 + 0.5f), bg >> 24);
        Assert.Equal((uint)(0x1C * 0.86f + 0.5f), (bg >> 16) & 0xFF);
    }

    [Fact]
    public void Coin_bas_droit_de_la_zone_de_travail()
    {
        // Barre des tâches de 48 px en bas d'un écran 1920 × 1080.
        var work = new ScreenRect(0, 0, 1920, 1032);
        Assert.Equal((1920 - 150 - 12, 1032 - 52 - 12), LiveDisplay.BottomRight(work, 150, 52, 12));
    }

    [Theory]
    [InlineData(150, 96, 150)]
    [InlineData(150, 144, 225)]
    [InlineData(52, 120, 65)]
    public void Mise_a_l_echelle_selon_les_ppp(int value, int dpi, int expected) =>
        Assert.Equal(expected, LiveDisplay.Scale(value, dpi));

    [Fact]
    public void Lissage_part_de_la_premiere_valeur_et_rattrape_la_cible()
    {
        var smoother = new LiveSmoother();
        Assert.Equal(60, smoother.Next(60, 0));
        // Après une constante de temps, 63 % de l'écart est rattrapé.
        Assert.Equal(60 + 20 * (1 - Math.Exp(-1)), smoother.Next(80, 1000), 9);
        smoother.Reset();
        Assert.Equal(40, smoother.Next(40, 5000));
    }
}

public sealed class KeyboardGeometryTests
{
    private const ushort A = 0x10, Z = 0x11, E = 0x12, R = 0x13, Q = 0x1E, S = 0x1F, D = 0x20, J = 0x24;

    [Fact]
    public void Chaque_touche_est_placee_une_seule_fois_et_retrouvee()
    {
        var codes = new HashSet<ushort>();
        foreach (var key in KeyboardGeometry.Keys)
        {
            Assert.True(codes.Add(key.Code), $"Touche 0x{key.Code:X} placée deux fois.");
            Assert.True(KeyboardGeometry.TryFind(key.Code, out var found));
            Assert.Equal(key, found);
        }
        Assert.False(KeyboardGeometry.TryFind(0x1FF, out _));
    }

    [Fact]
    public void Voisines_sur_la_rangee_et_entre_rangees_decalees()
    {
        Assert.True(KeyboardGeometry.AreNeighbors(E, Z));
        Assert.True(KeyboardGeometry.AreNeighbors(E, R));
        Assert.True(KeyboardGeometry.AreNeighbors(E, S));
        Assert.True(KeyboardGeometry.AreNeighbors(E, D));
        Assert.True(KeyboardGeometry.AreNeighbors(A, Q));
        Assert.False(KeyboardGeometry.AreNeighbors(E, A));
        Assert.False(KeyboardGeometry.AreNeighbors(E, J));
        Assert.False(KeyboardGeometry.AreNeighbors(E, E));
    }

    [Fact]
    public void Doigte_azerty_standard()
    {
        Assert.Equal(Finger.LeftPinky, KeyboardGeometry.FingerOf(A));
        Assert.Equal(Finger.LeftPinky, KeyboardGeometry.FingerOf(Q));
        Assert.Equal(Finger.LeftMiddle, KeyboardGeometry.FingerOf(E));
        Assert.Equal(Finger.RightIndex, KeyboardGeometry.FingerOf(J));
        Assert.Equal(Finger.None, KeyboardGeometry.FingerOf(0x1FF));
        Assert.True(KeyboardGeometry.TryFind(J, out var j) && j.IsRightHand && !j.IsLeftHand);
    }

    [Fact]
    public void Pave_numerique_a_droite_du_bloc_principal()
    {
        Assert.True(KeyboardGeometry.TryFind(KeyCode.Enter, out var enter));
        Assert.True(KeyboardGeometry.TryFind(KeyCode.Num5, out var num5));
        Assert.True(num5.X > enter.X + enter.Width);
    }
}

public sealed class LayoutTableTests
{
    [Fact]
    public void Aller_retour_serialisation_identique()
    {
        var layout = LayoutTable.FrenchAzerty;
        var back = LayoutTable.Deserialize(layout.Serialize());

        Assert.Equal(layout.Id, back.Id);
        Assert.Equal(layout.HasAltGr, back.HasAltGr);
        Assert.Equal(layout.ComputeHash(), back.ComputeHash());
        for (ushort key = 1; key < KeyCode.ScanCodeLimit; key++)
        {
            for (int slot = 0; slot < LayoutTable.SlotCount; slot++)
            {
                Assert.Equal(layout.Get(key, slot, out bool dead), back.Get(key, slot, out bool backDead));
                Assert.Equal(dead, backDead);
            }
        }
    }

    [Fact]
    public void Touche_morte_conservee()
    {
        var back = LayoutTable.Deserialize(LayoutTable.FrenchAzerty.Serialize());
        Assert.Equal('^', back.Get(0x1A, 0, out bool dead));
        Assert.True(dead);
    }

    [Fact]
    public void Donnees_tronquees_ou_version_inconnue_refusees()
    {
        byte[] data = LayoutTable.FrenchAzerty.Serialize();
        Assert.Throws<InvalidDataException>(() => LayoutTable.Deserialize(data.AsSpan(0, data.Length / 2)));
        data[0] ^= 0xFF;
        Assert.Throws<InvalidDataException>(() => LayoutTable.Deserialize(data));
        Assert.Throws<InvalidDataException>(() => LayoutTable.Deserialize([]));
    }
}
