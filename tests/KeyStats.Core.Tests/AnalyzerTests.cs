using KeyStats.Core.Analysis;
using KeyStats.Core.Input;
using KeyStats.Core.Text;

namespace KeyStats.Core.Tests;

public sealed class AnalyzerTests
{
    private const string Prose = "le chat dort sur le canapé du salon depuis ce matin";

    private static int Day(long ms) => new LocalDayClock(TimeZoneInfo.Utc).DayKey(ms);

    private static int CharCount(StatsAccumulator stats, char c) =>
        stats.Chars.Where(kv => kv.Key.Ch == c).Sum(kv => kv.Value);

    private static long Metric(StatsAccumulator stats, DayMetric metric) =>
        stats.Metrics.Where(kv => kv.Key.Metric == metric).Sum(kv => kv.Value);

    [Fact]
    public void Texte_regulier_a_300_ms_par_touche_donne_200_cpm_soit_40_wpm()
    {
        var stats = new Typist().Type(Prose).Analyze();

        var s = Assert.Single(stats.PendingSessions);
        Assert.Equal(SessionKind.Prose, s.Kind);
        Assert.Equal(Prose.Length, s.Chars);
        Assert.Equal(200, s.GrossCpm, 6);
        Assert.Equal(40, s.GrossCpm / 5, 6);
        Assert.Equal(200, s.RealCpm, 6);
        Assert.Equal((Prose.Length - 1) * 300L, s.ActiveMs);
        Assert.Equal(1, s.AppId);
        Assert.Equal(1, s.DeviceId);
    }

    [Fact]
    public void Pause_de_plus_de_2_s_exclue_du_temps_actif_mais_pas_du_debit_reel()
    {
        const string a = "le chat dort sur le canapé ", b = "du salon depuis ce matin";
        var stats = new Typist().Type(a).Wait(3000).Type(b).Analyze();

        var s = Assert.Single(stats.PendingSessions);
        int n = a.Length + b.Length;
        Assert.Equal((n - 2) * 300L, s.ActiveMs);
        Assert.Equal((n - 1) * 300L + 3000, s.DurationMs);
        Assert.Equal(200, s.GrossCpm, 6);
        Assert.True(s.RealCpm < 190);
    }

    [Fact]
    public void Pause_de_plus_de_5_s_coupe_la_session()
    {
        var stats = new Typist().Type(Prose).Wait(6000).Type(Prose).Analyze();
        Assert.Equal(2, stats.PendingSessions.Count);
    }

    [Fact]
    public void Cinq_frappes_isolees_ne_font_pas_de_session()
    {
        var t = new Typist();
        foreach (char c in "abcde") t.TypeChar(c).Wait(10_000);
        var stats = t.Analyze();

        Assert.Empty(stats.PendingSessions);
        Assert.Equal(5, Metric(stats, DayMetric.CharsTyped));
        Assert.Equal(1, stats.Chars[(Day(Typist.Start), SessionKind.None, 'a')]);
    }

    [Fact]
    public void Martelement_zqsd_ne_fait_pas_de_session()
    {
        var t = new Typist { Step = 150 };
        for (int i = 0; i < 40; i++) t.Type("zqsd");
        t.HoldKey(0x11, repeats: 30); // z maintenu
        var stats = t.Analyze();

        Assert.Empty(stats.PendingSessions);
    }

    [Fact]
    public void Session_chqt_compte_une_faute_q_pour_a_et_un_effacement_collateral()
    {
        var stats = new Typist().Type("le chqt").Backspace(2).Type("at dort sur le canapé du salon depuis ce matin").Analyze();

        var s = Assert.Single(stats.PendingSessions);
        Assert.Equal(SessionKind.Prose, s.Kind);
        Assert.Equal(1, s.Typos);
        Assert.Equal(1, s.Collateral);
        Assert.Equal(1, s.Corrections);
        int day = Day(Typist.Start);
        Assert.Equal(1, stats.ErrorPairs[(day, 'q', 'a', ErrorType.Substitution)]);
        Assert.Equal(1, stats.Bigrams[(day, 'h', 'a')].Errors);
        Assert.Equal(1, Metric(stats, DayMetric.Typos));
        Assert.Equal(1, Metric(stats, DayMetric.Substitutions));
        Assert.Equal(2, Metric(stats, DayMetric.Backspaces));
    }

    [Fact]
    public void Retour_arriere_maintenu_est_compte()
    {
        var stats = new Typist().Type("le chat dort sur le canapé xxxxxxx").HoldKey(KeyCode.Backspace, repeats: 6)
            .Type(" du salon depuis ce matin").Analyze();

        Assert.Equal(1, Metric(stats, DayMetric.HeldBackspace));
        Assert.Equal(6, Metric(stats, DayMetric.Repeats));
        Assert.Equal(7, Metric(stats, DayMetric.Backspaces));
        Assert.Equal(6, Metric(stats, DayMetric.BackspaceRepeats));
    }

    [Fact]
    public void Ctrl_retour_arriere_n_est_pas_un_raccourci()
    {
        var stats = new Typist().Type("le chat dort sur le canapé bleu").Combo(KeyCode.LeftCtrl, KeyCode.Backspace)
            .Type("du salon depuis ce matin").Analyze();

        Assert.Empty(stats.Shortcuts);
        Assert.Equal(1, Metric(stats, DayMetric.WordBackspaces));
        Assert.Equal(1, Metric(stats, DayMetric.WordDeletes));
        Assert.Equal(4, Metric(stats, DayMetric.WordBackspaceChars));
    }

    [Fact]
    public void AltGr_a_donne_arobase_et_pas_un_raccourci()
    {
        var stats = new Typist().Type("@").Analyze();

        Assert.Empty(stats.Shortcuts);
        Assert.Equal(1, CharCount(stats, '@'));
        // Le faux Ctrl gauche que Windows ajoute à AltGr n'est pas compté comme une touche.
        Assert.DoesNotContain(stats.KeyMinutes.Keys, k => k.Key == KeyCode.LeftCtrl);
        Assert.Contains(stats.KeyMinutes.Keys, k => k.Key == KeyCode.RightAlt);
    }

    [Fact]
    public void Ctrl_C_est_un_raccourci()
    {
        var stats = new Typist().Combo(KeyCode.LeftCtrl, 0x2E).Analyze();

        Assert.Equal(1, stats.Shortcuts[(Day(Typist.Start), ShortcutModifiers.Ctrl, 0x2E)]);
        Assert.Equal(0, CharCount(stats, 'c'));
        Assert.Contains(stats.KeyMinutes.Keys, k => k.Key == KeyCode.LeftCtrl);
    }

    [Fact]
    public void Ctrl_Maj_T_garde_tous_ses_modificateurs()
    {
        var stats = new Typist().Combo(KeyCode.LeftCtrl, KeyCode.LeftShift, 0x14).Analyze();

        var key = Assert.Single(stats.Shortcuts.Keys);
        Assert.Equal(ShortcutModifiers.Ctrl | ShortcutModifiers.Shift, key.Mods);
        Assert.Equal(0x14, key.Key);
    }

    [Fact]
    public void Majuscules_et_chiffres_de_la_rangee_du_haut_ne_sont_pas_des_raccourcis()
    {
        var stats = new Typist().Type("Paris 2026").Analyze();

        Assert.Empty(stats.Shortcuts);
        Assert.Equal(1, CharCount(stats, 'P'));
        Assert.Equal(2, CharCount(stats, '2'));
    }

    [Fact]
    public void Accent_circonflexe_puis_e_donne_e_circonflexe()
    {
        var stats = new Typist().Type("fête").Analyze();

        Assert.Equal(1, CharCount(stats, 'ê'));
        Assert.Equal(0, CharCount(stats, '^'));
        Assert.Equal(4, Metric(stats, DayMetric.CharsTyped));
    }

    [Fact]
    public void Trema_par_touche_morte_avec_maj()
    {
        var stats = new Typist().Type("Noël").Analyze();
        Assert.Equal(1, CharCount(stats, 'ë'));
    }

    [Fact]
    public void Touche_morte_sans_composition_sort_l_accent_puis_la_lettre()
    {
        // ^ puis x : Windows écrit « ^x ».
        var stats = new Typist().Key(0x1A).Key(0x2D).Analyze();

        Assert.Equal(1, CharCount(stats, '^'));
        Assert.Equal(1, CharCount(stats, 'x'));
    }

    [Fact]
    public void Alt_0156_donne_oe()
    {
        var stats = new Typist().AltCode("0156").Analyze();

        Assert.Equal(1, CharCount(stats, 'œ'));
        Assert.Empty(stats.Shortcuts);
    }

    [Fact]
    public void Alt_144_donne_E_accent_aigu_en_page_OEM()
    {
        var stats = new Typist().AltCode("144").Analyze();
        Assert.Equal(1, CharCount(stats, 'É'));
    }

    [Fact]
    public void Pave_numerique_avec_verr_num_donne_des_chiffres()
    {
        var stats = new Typist().Reset(LockKeys.NumLock).Key(KeyCode.Num1).Key(KeyCode.Num2).Analyze();
        Assert.Equal(1, CharCount(stats, '1'));
        Assert.Equal(1, CharCount(stats, '2'));
    }

    [Fact]
    public void Relachement_de_Win_perdu_puis_remise_a_zero_pas_de_faux_raccourcis()
    {
        var stats = new Typist().Down(KeyCode.LeftWin).Reset().Type("ceci est du texte").Analyze();

        Assert.Empty(stats.Shortcuts);
        Assert.Equal(2, CharCount(stats, 'c'));
    }

    [Fact]
    public void Sans_remise_a_zero_Win_reste_enfonce()
    {
        var stats = new Typist().Down(KeyCode.LeftWin).Type("cv").Analyze();
        Assert.Equal(2, Metric(stats, DayMetric.Shortcuts));
    }

    [Fact]
    public void Relachement_force_de_Ctrl_n_est_pas_un_raccourci_ensuite()
    {
        var stats = new Typist().Down(KeyCode.RightCtrl).ForcedUp(KeyCode.RightCtrl).Type("cv").Analyze();

        Assert.Empty(stats.Shortcuts);
        Assert.Equal(1, CharCount(stats, 'v'));
    }

    [Fact]
    public void Win_seul_ouvre_le_menu_demarrer()
    {
        var stats = new Typist().Key(KeyCode.LeftWin).Analyze();
        Assert.Equal(1, stats.Shortcuts[(Day(Typist.Start), ShortcutModifiers.Win, KeyCode.LeftWin)]);
    }

    [Fact]
    public void Tab_et_touches_du_pave_sont_comptees_par_touche()
    {
        var stats = new Typist().Key(KeyCode.Tab).Key(KeyCode.Tab).Key(KeyCode.NumEnter).Analyze();

        Assert.Equal(2, stats.KeyMinutes.Where(kv => kv.Key.Key == KeyCode.Tab).Sum(kv => kv.Value.Presses));
        Assert.Equal(1, stats.KeyMinutes.Where(kv => kv.Key.Key == KeyCode.NumEnter).Sum(kv => kv.Value.Presses));
    }

    [Fact]
    public void Touches_chevauchees_sont_comptees()
    {
        // Step plus court que la durée d'appui : chaque touche est enfoncée avant que la précédente soit relâchée.
        var stats = new Typist { Step = 60, HoldMs = 90 }.Type("azerty").Analyze();
        Assert.Equal(5, Metric(stats, DayMetric.Overlaps));
    }

    [Fact]
    public void Appli_exclue_ne_compte_que_les_touches()
    {
        var events = new Typist().Type(Prose).ToArray();
        var stats = new StatsAccumulator(new LocalDayClock(TimeZoneInfo.Utc));
        var analyzer = new Analyzer(stats);
        analyzer.SetFocus(new FocusInfo(Typist.Start - 1000, 7, 0, null, FocusState.NewWindow | FocusState.Excluded));
        foreach (var e in events) analyzer.Process(e);
        analyzer.EndAll();

        Assert.Empty(stats.PendingSessions);
        Assert.Empty(stats.Chars);
        Assert.Empty(stats.KeyMinutes);
        Assert.Equal(Prose.Length, Metric(stats, DayMetric.ExcludedKeys));
    }

    [Fact]
    public void Champ_de_mot_de_passe_ne_compte_que_les_touches_puis_reprend_l_analyse()
    {
        var t = new Typist().Type(Prose);
        var stats = new StatsAccumulator(new LocalDayClock(TimeZoneInfo.Utc));
        var analyzer = new Analyzer(stats);
        analyzer.SetFocus(new FocusInfo(Typist.Start - 1000, 1, 0, null, FocusState.NewWindow));
        foreach (var e in t.Events) analyzer.Process(e);

        int from = t.Events.Count;
        analyzer.SetFocus(new FocusInfo(t.Ms, 1, 0, null, FocusState.Secret)); // clôt la session en cours
        var before = TextFootprint(stats);
        Assert.True(before.Chars > 0);
        t.Type("kiwi");
        for (int i = from; i < t.Events.Count; i++) analyzer.Process(t.Events[i]);

        Assert.False(analyzer.InSession);
        Assert.Equal(before, TextFootprint(stats));
        Assert.Equal(4, Metric(stats, DayMetric.ExcludedKeys));

        from = t.Events.Count;
        analyzer.SetFocus(new FocusInfo(t.Ms, 1, 0, null, FocusState.None));
        t.Type(" kiwi");
        for (int i = from; i < t.Events.Count; i++) analyzer.Process(t.Events[i]);
        analyzer.EndAll();

        Assert.True(TextFootprint(stats).Keys > before.Keys);
        Assert.Equal(4, Metric(stats, DayMetric.ExcludedKeys));
    }

    /// <summary>Tout ce qu'une frappe peut laisser hors des compteurs de touches exclues.</summary>
    private static (int Keys, int Chars, int Bigrams, int Shortcuts, int Sessions) TextFootprint(StatsAccumulator stats) => (
        stats.KeyMinutes.Values.Sum(kc => kc.Presses + kc.Repeats),
        stats.Chars.Values.Sum(),
        stats.Bigrams.Count,
        stats.Shortcuts.Values.Sum(),
        stats.PendingSessions.Count);

    private static (Analyzer Analyzer, StatsAccumulator Stats) SecretField()
    {
        var stats = new StatsAccumulator(new LocalDayClock(TimeZoneInfo.Utc));
        var analyzer = new Analyzer(stats);
        analyzer.SetFocus(new FocusInfo(Typist.Start - 1000, 1, 0, null, FocusState.NewWindow | FocusState.Secret));
        return (analyzer, stats);
    }

    private static ushort KeyOf(char c)
    {
        Assert.True(LayoutTable.FrenchAzerty.TryFindKey(c, out ushort key, out _));
        return key;
    }

    [Fact]
    public void Touche_tenue_dans_le_champ_secret_ne_fuit_pas_apres_la_sortie()
    {
        var (analyzer, stats) = SecretField();
        long t = Typist.Start;
        ushort k = KeyOf('k');
        analyzer.Process(new InputEvent(t, EventKind.KeyDown, k, 1));
        analyzer.SetFocus(new FocusInfo(t + 100, 1, 0, null, FocusState.None));
        for (int i = 0; i < 20; i++) analyzer.Process(new InputEvent(t + 500 + i * 33, EventKind.KeyDown, k, 1));
        analyzer.Process(new InputEvent(t + 1200, EventKind.KeyUp, k, 1));
        analyzer.EndAll();

        Assert.Equal((0, 0, 0, 0, 0), TextFootprint(stats));
        Assert.Equal(1, Metric(stats, DayMetric.ExcludedKeys));
    }

    [Fact]
    public void Touche_tenue_avant_le_champ_secret_n_y_compte_pas_ses_repetitions()
    {
        var stats = new StatsAccumulator(new LocalDayClock(TimeZoneInfo.Utc));
        var analyzer = new Analyzer(stats);
        analyzer.SetFocus(new FocusInfo(Typist.Start - 1000, 1, 0, null, FocusState.NewWindow));
        long t = Typist.Start;
        ushort k = KeyOf('k');
        analyzer.Process(new InputEvent(t, EventKind.KeyDown, k, 1));
        analyzer.SetFocus(new FocusInfo(t + 100, 1, 0, null, FocusState.Secret));
        for (int i = 0; i < 10; i++) analyzer.Process(new InputEvent(t + 500 + i * 33, EventKind.KeyDown, k, 1));
        analyzer.Process(new InputEvent(t + 900, EventKind.KeyUp, k, 1));

        Assert.Equal(0, Metric(stats, DayMetric.ExcludedKeys));
    }

    [Fact]
    public void Ctrl_en_attente_dans_le_champ_secret_reste_dans_le_champ_secret()
    {
        var (analyzer, stats) = SecretField();
        long t = Typist.Start;
        analyzer.Process(new InputEvent(t, EventKind.KeyDown, KeyCode.LeftCtrl, 1));
        analyzer.SetFocus(new FocusInfo(t + 5, 1, 0, null, FocusState.None));
        analyzer.Process(new InputEvent(t + 50, EventKind.KeyUp, KeyCode.LeftCtrl, 1));
        ushort c = KeyOf('c');
        analyzer.Process(new InputEvent(t + 400, EventKind.KeyDown, c, 1));
        analyzer.Process(new InputEvent(t + 480, EventKind.KeyUp, c, 1));
        analyzer.EndAll();

        Assert.DoesNotContain(stats.KeyMinutes.Keys, km => km.Key == KeyCode.LeftCtrl);
        Assert.Empty(stats.Shortcuts);
        Assert.Equal(1, Metric(stats, DayMetric.ExcludedKeys));
    }

    [Fact]
    public void Code_alt_commence_dans_le_champ_secret_ne_produit_rien_apres_la_sortie()
    {
        var (analyzer, stats) = SecretField();
        long t = Typist.Start;
        analyzer.Process(new InputEvent(t, EventKind.KeyDown, KeyCode.LeftAlt, 1));
        ushort[] digits = [KeyCode.Num0, KeyCode.Num1, KeyCode.Num5, KeyCode.Num6];
        for (int i = 0; i < digits.Length; i++)
        {
            analyzer.Process(new InputEvent(t + 100 + i * 200, EventKind.KeyDown, digits[i], 1));
            analyzer.Process(new InputEvent(t + 180 + i * 200, EventKind.KeyUp, digits[i], 1));
        }
        analyzer.SetFocus(new FocusInfo(t + 1000, 1, 0, null, FocusState.None));
        analyzer.Process(new InputEvent(t + 1100, EventKind.KeyUp, KeyCode.LeftAlt, 1));
        analyzer.EndAll();

        Assert.Equal(0, CharCount(stats, 'œ'));
        Assert.Equal((0, 0, 0, 0, 0), TextFootprint(stats));
    }

    [Fact]
    public void Touche_morte_du_champ_secret_ne_compose_pas_la_lettre_suivante()
    {
        var (analyzer, stats) = SecretField();
        var t = new Typist();
        t.Key(0x1A); // ^ mort
        foreach (var e in t.Events) analyzer.Process(e);
        int from = t.Events.Count;
        analyzer.SetFocus(new FocusInfo(t.Ms, 1, 0, null, FocusState.None));
        t.Type("e");
        for (int i = from; i < t.Events.Count; i++) analyzer.Process(t.Events[i]);
        analyzer.EndAll();

        Assert.Equal(0, CharCount(stats, 'ê'));
        Assert.Equal(1, Metric(stats, DayMetric.ExcludedKeys));
    }

    [Theory]
    [InlineData(FocusState.None, false)]
    [InlineData(FocusState.NewWindow | FocusState.Elevated, false)]
    [InlineData(FocusState.Excluded, true)]
    [InlineData(FocusState.NewWindow | FocusState.Secret, true)]
    public void Compteurs_seuls_pour_appli_exclue_et_champ_secret(FocusState state, bool countersOnly) =>
        Assert.Equal(countersOnly, state.CountersOnly());

    [Fact]
    public void Changement_de_fenetre_termine_la_session_et_les_corrections()
    {
        var t = new Typist().Type(Prose);
        var stats = new StatsAccumulator(new LocalDayClock(TimeZoneInfo.Utc));
        var analyzer = new Analyzer(stats);
        analyzer.SetFocus(new FocusInfo(Typist.Start - 1000, 1, 0, null, FocusState.NewWindow));
        foreach (var e in t.Events) analyzer.Process(e);
        Assert.True(analyzer.InSession);

        analyzer.SetFocus(new FocusInfo(t.Ms, 2, 0, null, FocusState.NewWindow));

        Assert.False(analyzer.InSession);
        Assert.Single(stats.PendingSessions);
    }

    [Fact]
    public void Tick_termine_une_session_sans_frappe_depuis_5_s()
    {
        var t = new Typist().Type(Prose);
        var stats = new StatsAccumulator(new LocalDayClock(TimeZoneInfo.Utc));
        var analyzer = new Analyzer(stats);
        foreach (var e in t.Events) analyzer.Process(e);

        analyzer.Tick(t.Ms + 1000);
        Assert.True(analyzer.InSession);
        analyzer.Tick(t.Ms + 6000);
        Assert.False(analyzer.InSession);
    }

    [Fact]
    public void Code_est_reconnu_a_ses_symboles()
    {
        var stats = new Typist().Type("if (a == b) { x = y[0] + z; } else { return; }").Analyze();
        Assert.Equal(SessionKind.Code, Assert.Single(stats.PendingSessions).Kind);
    }

    [Fact]
    public void Rejouer_le_meme_flux_donne_les_memes_statistiques()
    {
        var events = new Typist().Type("le chqt").Backspace(2).Type("at dort sur le canapé, ").Combo(KeyCode.LeftCtrl, 0x2E)
            .Type("du salon depuis ce matin @ midi").ToArray();

        var a = Typist.Analyze(events);
        var b = Typist.Analyze(events);

        Assert.Equal(a.Metrics.OrderBy(kv => kv.Key), b.Metrics.OrderBy(kv => kv.Key));
        Assert.Equal(a.Chars.OrderBy(kv => kv.Key), b.Chars.OrderBy(kv => kv.Key));
    }
}
