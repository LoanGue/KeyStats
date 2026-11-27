using KeyStats.Core.Analysis;
using KeyStats.Core.Input;
using KeyStats.Core.Text;

namespace KeyStats.Core.Tests;

public sealed class CapitalShiftTests
{
    private static long Metric(StatsAccumulator stats, DayMetric metric) =>
        stats.Metrics.Where(kv => kv.Key.Metric == metric).Sum(kv => kv.Value);

    private static ushort KeyOf(char c) =>
        LayoutTable.FrenchAzerty.TryFindKey(c, out ushort key, out _) ? key : throw new ArgumentException(c.ToString());

    [Fact]
    public void Maj_gauche_compte_opposee_pour_la_main_droite_et_meme_cote_pour_la_gauche()
    {
        // Le Typist prend toujours Maj gauche : P et M sont à droite, A et Z à gauche.
        var stats = new Typist().Type("Pomme Mai Azur Zinc").Analyze();
        Assert.Equal(2, Metric(stats, DayMetric.ShiftOppositeCapitals));
        Assert.Equal(2, Metric(stats, DayMetric.ShiftSameSideCapitals));
        Assert.Equal(0, Metric(stats, DayMetric.CapsLockCapitals));
    }

    [Fact]
    public void Maj_droite_avec_une_lettre_de_gauche_est_opposee()
    {
        var stats = new Typist().Combo(KeyCode.RightShift, KeyOf('a')).Type("ller").Analyze();
        Assert.Equal(1, Metric(stats, DayMetric.ShiftOppositeCapitals));
        Assert.Equal(0, Metric(stats, DayMetric.ShiftSameSideCapitals));
    }

    [Fact]
    public void Verr_Maj_compte_chaque_majuscule_sans_touche_Maj()
    {
        var stats = new Typist().Key(KeyCode.CapsLock).Key(KeyOf('o')).Key(KeyOf('k')).Analyze();
        Assert.Equal(2, Metric(stats, DayMetric.CapsLockCapitals));
        Assert.Equal(0, Metric(stats, DayMetric.ShiftOppositeCapitals) + Metric(stats, DayMetric.ShiftSameSideCapitals));
    }

    [Fact]
    public void Chiffres_et_ponctuation_avec_Maj_ne_sont_pas_des_majuscules()
    {
        // Sur l'AZERTY, les chiffres et le point demandent Maj.
        var stats = new Typist().Type("2024. Fin").Analyze();
        Assert.Equal(1, Metric(stats, DayMetric.ShiftSameSideCapitals));
        Assert.Equal(0, Metric(stats, DayMetric.ShiftOppositeCapitals));
    }

    [Fact]
    public void Majuscule_repetee_par_appui_maintenu_compte_une_fois()
    {
        var stats = new Typist().Down(KeyCode.LeftShift).HoldKey(KeyOf('p'), repeats: 3).Analyze();
        Assert.Equal(1, Metric(stats, DayMetric.ShiftOppositeCapitals));
    }

    [Fact]
    public void Deux_touches_Maj_enfoncees_comptent_comme_Maj_opposee()
    {
        var stats = new Typist().Combo(KeyCode.LeftShift, KeyCode.RightShift, KeyOf('a')).Analyze();
        Assert.Equal(1, Metric(stats, DayMetric.ShiftOppositeCapitals));
        Assert.Equal(0, Metric(stats, DayMetric.ShiftSameSideCapitals));
    }

    [Theory]
    [InlineData(FocusState.Excluded)]
    [InlineData(FocusState.Secret)]
    public void Majuscules_d_une_appli_exclue_ou_d_un_mot_de_passe_ne_sont_pas_comptees(FocusState state)
    {
        var t = new Typist().Type("Pomme").Key(KeyCode.CapsLock).Type("kiwi");
        var stats = new StatsAccumulator(new LocalDayClock(TimeZoneInfo.Utc));
        var analyzer = new Analyzer(stats);
        analyzer.SetFocus(new FocusInfo(Typist.Start - 1000, 1, 0, null, FocusState.NewWindow | state));
        foreach (var e in t.Events) analyzer.Process(e);
        analyzer.EndAll();

        long capitals = Metric(stats, DayMetric.ShiftOppositeCapitals) + Metric(stats, DayMetric.ShiftSameSideCapitals)
            + Metric(stats, DayMetric.CapsLockCapitals);
        Assert.Equal(0, capitals);
    }

    [Fact]
    public void Verr_Maj_pressee_dans_une_appli_exclue_reste_suivie()
    {
        var t = new Typist().Key(KeyCode.CapsLock);
        var stats = new StatsAccumulator(new LocalDayClock(TimeZoneInfo.Utc));
        var analyzer = new Analyzer(stats);
        analyzer.SetFocus(new FocusInfo(Typist.Start - 1000, 1, 0, null, FocusState.NewWindow | FocusState.Excluded));
        foreach (var e in t.Events) analyzer.Process(e);

        int from = t.Events.Count;
        analyzer.SetFocus(new FocusInfo(t.Ms, 2, 0, null, FocusState.NewWindow));
        t.Key(KeyOf('o')).Key(KeyOf('k'));
        for (int i = from; i < t.Events.Count; i++) analyzer.Process(t.Events[i]);
        analyzer.EndAll();

        Assert.Equal(2, Metric(stats, DayMetric.CapsLockCapitals));
    }
}
