using KeyStats.Core.Analysis;
using KeyStats.Core.Input;
using KeyStats.Core.Text;

namespace KeyStats.Core.Tests;

public sealed class OverlapTrackerTests
{
    private static ushort KeyOf(char c) =>
        LayoutTable.FrenchAzerty.TryFindKey(c, out ushort key, out _) ? key : throw new ArgumentException(c.ToString());

    /// <summary>Histogrammes de chevauchement par paire (touche tenue, touche enfoncée par-dessus).</summary>
    private static Dictionary<(ushort, ushort), int[]> Pairs(StatsAccumulator stats) =>
        stats.Histograms.Where(kv => kv.Key.Kind == HistogramKind.OverlapMs)
            .ToDictionary(kv => OverlapTracker.Pair(kv.Key.Sub), kv => kv.Value);

    private static int Bucket(double ms) => LogScale.Overlap.Bucket(ms);

    [Fact]
    public void Roulement_mesure_du_second_appui_au_premier_relachement()
    {
        // Appui toutes les 60 ms, tenu 90 ms : chaque touche chevauche la suivante pendant 30 ms.
        var stats = new Typist { Step = 60, HoldMs = 90 }.Type("aze").Analyze();
        var pairs = Pairs(stats);

        Assert.Equal(2, pairs.Count);
        Assert.Equal(1, pairs[(KeyOf('a'), KeyOf('z'))][Bucket(30)]);
        Assert.Equal(1, pairs[(KeyOf('z'), KeyOf('e'))][Bucket(30)]);
        Assert.Equal(2, pairs.Values.Sum(h => h.Sum()));
    }

    [Fact]
    public void Touche_relachee_avant_la_premiere_compte_sa_propre_duree()
    {
        // a tenue, z enfoncée 40 ms plus tard et relâchée 50 ms après, a relâchée ensuite.
        var t = new Typist { Step = 40 };
        t.Down(KeyOf('a'));
        t.HoldMs = 50;
        t.Key(KeyOf('z')).Wait(20).Up(KeyOf('a'));
        var pairs = Pairs(t.Analyze());

        Assert.Equal(1, pairs[(KeyOf('a'), KeyOf('z'))][Bucket(50)]);
    }

    [Fact]
    public void Sans_chevauchement_rien_n_est_mesure()
    {
        var stats = new Typist().Type("azerty").Analyze();
        Assert.Empty(Pairs(stats));
    }

    [Fact]
    public void Relachement_perdu_abandonne_la_mesure()
    {
        var t = new Typist { Step = 40 };
        t.Down(KeyOf('a')).Down(KeyOf('z')).ForcedUp(KeyOf('a')).ForcedUp(KeyOf('z'));
        Assert.Empty(Pairs(t.Analyze()));
    }

    [Fact]
    public void Majuscule_et_raccourci_ne_sont_pas_des_chevauchements_de_lettres()
    {
        var stats = new Typist().Type("Azur").Combo(KeyCode.LeftCtrl, KeyOf('c')).Analyze();
        Assert.Empty(Pairs(stats));
    }

    [Fact]
    public void Au_dela_de_la_limite_les_chevauchements_ne_sont_plus_suivis()
    {
        var stats = new StatsAccumulator(new LocalDayClock(TimeZoneInfo.Utc));
        var tracker = new OverlapTracker(stats, 2000);
        for (ushort k = 1; k <= OverlapTracker.MaxPending + 2; k++) tracker.Start(100, k, 0);
        tracker.Release(100, 20);

        Assert.Equal(OverlapTracker.MaxPending, stats.Histograms.Count);
    }

    [Fact]
    public void Trois_touches_tenues_ensemble_mesurent_chaque_derniere_paire()
    {
        // a, z, e enfoncées à 40 ms d'écart et toutes tenues 120 ms : a–z puis z–e.
        var stats = new Typist { Step = 40, HoldMs = 120 }.Type("aze").Analyze();
        Assert.Equal([(KeyOf('a'), KeyOf('z')), (KeyOf('z'), KeyOf('e'))], Pairs(stats).Keys.Order());
    }

    [Fact]
    public void Touche_anterieure_deja_relachee_ne_fait_pas_de_paire()
    {
        // a tenue ; z tapée puis relâchée ; e enfoncée alors que a est encore tenue : seule a–z est mesurée.
        var t = new Typist { Step = 40, HoldMs = 20 };
        t.Down(KeyOf('a')).Key(KeyOf('z')).Key(KeyOf('e')).Up(KeyOf('a'));
        var stats = t.Analyze();
        Assert.Equal([(KeyOf('a'), KeyOf('z'))], Pairs(stats).Keys);
        Assert.Equal(2, stats.Metrics.Where(kv => kv.Key.Metric == DayMetric.Overlaps).Sum(kv => kv.Value));
    }

    [Fact]
    public void Chevauchement_plus_long_que_le_plafond_ignore()
    {
        // z enfoncée pendant que a est tenue, et tenue elle-même 3 s : au-delà du plafond de 1 s.
        var t = new Typist { Step = 40, HoldMs = 3000 };
        t.Down(KeyOf('a')).Key(KeyOf('z')).Wait(4000).Up(KeyOf('a'));
        Assert.Empty(Pairs(t.Analyze(new AnalysisSettings { HoldCapMs = 1000 })));
    }

    [Theory]
    [InlineData(FocusState.Excluded)]
    [InlineData(FocusState.Secret)]
    public void Touche_tenue_en_entrant_dans_une_appli_exclue_n_est_pas_mesuree(FocusState mode)
    {
        long t = Typist.Start;
        var stats = Run(a =>
        {
            a.Process(new InputEvent(t, EventKind.KeyDown, KeyOf('a'), 1));
            a.SetFocus(new FocusInfo(t + 20, 7, 0, null, FocusState.NewWindow | mode));
            a.Process(new InputEvent(t + 40, EventKind.KeyDown, KeyOf('z'), 1));
            a.Process(new InputEvent(t + 80, EventKind.KeyUp, KeyOf('z'), 1));
            a.Process(new InputEvent(t + 100, EventKind.KeyUp, KeyOf('a'), 1));
        });
        Assert.Empty(Pairs(stats));
    }

    [Fact]
    public void Touche_tenue_en_sortant_d_une_appli_exclue_n_est_pas_mesuree()
    {
        long t = Typist.Start;
        var stats = Run(a =>
        {
            a.SetFocus(new FocusInfo(t - 100, 7, 0, null, FocusState.NewWindow | FocusState.Excluded));
            a.Process(new InputEvent(t, EventKind.KeyDown, KeyOf('a'), 1));
            a.SetFocus(new FocusInfo(t + 20, 1, 0, null, FocusState.NewWindow));
            a.Process(new InputEvent(t + 40, EventKind.KeyDown, KeyOf('z'), 1));
            a.Process(new InputEvent(t + 80, EventKind.KeyUp, KeyOf('z'), 1));
            a.Process(new InputEvent(t + 100, EventKind.KeyUp, KeyOf('a'), 1));
        });
        Assert.Empty(Pairs(stats));
    }

    private static StatsAccumulator Run(Action<Analyzer> script)
    {
        var stats = new StatsAccumulator(new LocalDayClock(TimeZoneInfo.Utc));
        var analyzer = new Analyzer(stats);
        analyzer.SetFocus(new FocusInfo(Typist.Start - 1000, 1, 0, null, FocusState.NewWindow));
        script(analyzer);
        analyzer.EndAll();
        return stats;
    }

    [Fact]
    public void Paire_retrouvee_depuis_la_sous_cle()
    {
        int sub = OverlapTracker.Sub(KeyCode.LeftShift, KeyCode.Delete);
        Assert.Equal((KeyCode.LeftShift, KeyCode.Delete), OverlapTracker.Pair(sub));
    }
}
