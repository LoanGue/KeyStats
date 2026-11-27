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
}
