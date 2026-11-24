using KeyStats.Core.Analysis;

namespace KeyStats.Core.Tests;

public sealed class CorrectionAnalyzerTests
{
    private sealed class Recorder : ICorrectionSink
    {
        public List<(CorrectionResult Result, ErrorRecord[] Errors)> Items { get; } = [];

        public void OnCorrection(in CorrectionResult result, ReadOnlySpan<ErrorRecord> errors) =>
            Items.Add((result, errors.ToArray()));
    }

    private readonly Recorder _sink = new();
    private readonly CorrectionAnalyzer _analyzer;
    private long _ms = 1_000_000;

    public CorrectionAnalyzerTests() => _analyzer = new CorrectionAnalyzer(_sink);

    private void Type(string text)
    {
        foreach (char c in text)
        {
            _analyzer.OnChar(c, _ms);
            _ms += 200;
        }
    }

    private void Backspace(int n)
    {
        for (int i = 0; i < n; i++)
        {
            _analyzer.OnBackspace(_ms, repeat: false);
            _ms += 200;
        }
    }

    private (CorrectionResult Result, ErrorRecord[] Errors) Single()
    {
        Assert.Single(_sink.Items);
        return _sink.Items[0];
    }

    [Fact]
    public void Chqt_deux_retours_arriere_une_seule_faute_et_un_effacement_collateral()
    {
        Type("le chqt");
        Backspace(2);
        Type("at dort");

        var (r, errors) = Single();
        Assert.Equal(CorrectionOutcome.Typo, r.Outcome);
        Assert.Equal(2, r.Deleted);
        Assert.Equal(1, r.Errors);
        Assert.Equal(1, r.Collateral);
        var e = Assert.Single(errors);
        Assert.Equal(new ErrorRecord(ErrorType.Substitution, 'q', 'a', 'h'), e);
        // Le q a été tapé deux frappes avant le premier retour arrière.
        Assert.Equal(400, r.DetectionDelayMs);
    }

    [Fact]
    public void Teh_corrige_en_the_est_une_inversion()
    {
        Type("teh");
        Backspace(2);
        Type("he end");

        var (r, errors) = Single();
        Assert.Equal(CorrectionOutcome.Typo, r.Outcome);
        Assert.Equal(0, r.Collateral);
        var e = Assert.Single(errors);
        Assert.Equal(ErrorType.Transposition, e.Type);
        Assert.Equal('e', e.Typed);
        Assert.Equal('h', e.Wanted);
    }

    [Fact]
    public void Lettre_oubliee_est_une_omission()
    {
        Type("le cht");
        Backspace(1);
        Type("at dort");

        var (r, errors) = Single();
        Assert.Equal(CorrectionOutcome.Typo, r.Outcome);
        Assert.Equal(1, r.Collateral);
        Assert.Equal(new ErrorRecord(ErrorType.Omission, '\0', 'a', 'h'), Assert.Single(errors));
    }

    [Fact]
    public void Lettre_en_trop_est_une_insertion()
    {
        Type("le chaat");
        Backspace(2);
        Type("t dort");

        var (r, errors) = Single();
        Assert.Equal(CorrectionOutcome.Typo, r.Outcome);
        Assert.Equal(1, r.Collateral);
        var e = Assert.Single(errors);
        Assert.Equal(ErrorType.Insertion, e.Type);
        Assert.Equal('a', e.Typed);
    }

    [Fact]
    public void Retape_a_l_identique_n_est_pas_une_faute()
    {
        Type("le chat");
        Backspace(2);
        Type("at dort");

        var (r, _) = Single();
        Assert.Equal(CorrectionOutcome.Retyped, r.Outcome);
        Assert.Equal(0, r.Errors);
    }

    [Fact]
    public void Changement_d_idee_est_une_reformulation()
    {
        Type("je pense que bonjour");
        Backspace(7);
        Type("salut tout le monde");

        var (r, errors) = Single();
        Assert.Equal(CorrectionOutcome.Reformulation, r.Outcome);
        Assert.Empty(errors);
    }

    [Fact]
    public void Effacer_sans_retaper_puis_cliquer_est_une_reformulation()
    {
        Type("le chat dort");
        Backspace(5);
        _analyzer.Invalidate();

        Assert.Equal(CorrectionOutcome.Reformulation, Single().Result.Outcome);
    }

    [Fact]
    public void Retour_arriere_apres_un_clic_n_est_pas_analyse()
    {
        Type("le chat");
        _analyzer.Invalidate();
        Backspace(2);
        Type("xy");

        var (r, _) = Single();
        Assert.Equal(CorrectionOutcome.Unanalyzed, r.Outcome);
        Assert.Equal(2, r.Deleted);
    }

    [Fact]
    public void Clic_avant_la_fin_de_la_reprise_conclut_avec_ce_qui_a_ete_tape()
    {
        Type("le chqt");
        Backspace(2);
        Type("at");
        Assert.Empty(_sink.Items);

        _analyzer.Invalidate();

        var (r, _) = Single();
        Assert.Equal(CorrectionOutcome.Typo, r.Outcome);
        Assert.Equal(0, _analyzer.BufferedLength);
    }

    [Fact]
    public void Retour_arriere_maintenu_est_marque()
    {
        Type("le chqt");
        _analyzer.OnBackspace(_ms, repeat: false);
        _analyzer.OnBackspace(_ms + 500, repeat: true);
        _ms += 700;
        Type("at dort");

        var (r, _) = Single();
        Assert.True(r.Held);
        Assert.Equal(CorrectionOutcome.Typo, r.Outcome);
    }

    [Fact]
    public void Ctrl_retour_arriere_efface_le_mot_et_les_espaces_de_fin()
    {
        Type("le chat ");

        int deleted = _analyzer.OnWordBackspace(_ms);

        Assert.Equal(5, deleted);
        Assert.Equal(3, _analyzer.BufferedLength);
        _ms += 200;
        Type("chien noir");
        Assert.True(Single().Result.Word);
    }

    [Fact]
    public void Ctrl_retour_arriere_sur_tampon_vide_n_est_pas_analyse()
    {
        int deleted = _analyzer.OnWordBackspace(_ms);
        Type("abc");

        Assert.Equal(0, deleted);
        var (r, _) = Single();
        Assert.Equal(CorrectionOutcome.Unanalyzed, r.Outcome);
        Assert.True(r.Word);
    }

    [Fact]
    public void Deux_corrections_rapprochees_sont_analysees_separement()
    {
        Type("le chqt");
        Backspace(2);
        Type("at dirt");
        Backspace(3);
        Type("ort sur le lit");

        Assert.Equal(2, _sink.Items.Count);
        Assert.All(_sink.Items, i => Assert.Equal(CorrectionOutcome.Typo, i.Result.Outcome));
        Assert.Equal(new ErrorRecord(ErrorType.Substitution, 'i', 'o', 'd'), Assert.Single(_sink.Items[1].Errors));
    }

    [Fact]
    public void Le_tampon_garde_une_taille_bornee()
    {
        Type(new string('a', CorrectionAnalyzer.BufferSize * 3));
        Assert.True(_analyzer.BufferedLength <= CorrectionAnalyzer.BufferSize);
    }
}
