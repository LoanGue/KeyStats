using KeyStats.Core.Analysis;
using KeyStats.Core.Reference;
using KeyStats.Core.Stats;
using KeyStats.Core.Web;

namespace KeyStats.Core.Tests;

public sealed class LettersApiTests : IDisposable
{
    private const int Today = 20261127; // un vendredi
    private readonly TempDb _temp = new();

    public LettersApiTests()
    {
        AddChar(20261126, SessionKind.Prose, 'e', 60);
        AddChar(20261126, SessionKind.Prose, 'é', 20);
        AddChar(20261126, SessionKind.Prose, 'E', 10);
        AddChar(20261126, SessionKind.Code, 'a', 10);
        AddChar(20261126, SessionKind.Code, '{', 500);
        AddChar(20261118, SessionKind.Prose, 'z', 1000);

        // Prose : 4 mots de 2 lettres, 2 de 5, 1 de 20. Code : 3 mots de 8 lettres.
        AddLength(20261126, SessionKind.Prose, 2, 4);
        AddLength(20261126, SessionKind.Prose, 5, 2);
        AddLength(20261125, SessionKind.Prose, 20, 1);
        AddLength(20261125, SessionKind.Code, 8, 3);
        AddLength(20261118, SessionKind.Prose, 3, 1000);
    }

    private void AddChar(int day, SessionKind kind, char c, int n) =>
        _temp.Db.Exec($"INSERT INTO day_char VALUES ({day}, {(int)kind}, {(int)c}, {n})");

    private void AddLength(int day, SessionKind kind, int len, int n) =>
        _temp.Db.Exec($"INSERT INTO day_wordlen VALUES ({day}, {(int)kind}, {len}, {n})");

    private LettersStats Read(string query) => LettersApi.Read(_temp.Db, StatsFilter.Parse(WebQuery.Parse(query), Today, 20261101)!);

    private static long Count(LettersStats l, char c) => l.Letters.Single(x => x.Letter == c.ToString()).Count;

    [Fact]
    public void Lettres_ramenees_a_a_z_sans_accent_ni_majuscule()
    {
        var l = Read("period=week");

        Assert.Equal(26, l.Letters.Count);
        Assert.Equal(90, Count(l, 'e'));
        Assert.Equal(10, Count(l, 'a'));
        Assert.Equal(0, Count(l, 'z'));
        Assert.Equal(100, l.Letters.Sum(x => x.Count));
    }

    [Fact]
    public void References_des_lettres_somment_a_un()
    {
        var l = Read("period=week");

        Assert.Equal(1, l.Letters.Sum(x => x.French), 6);
        Assert.Equal(1, l.Letters.Sum(x => x.English), 6);
        Assert.True(l.Letters.Single(x => x.Letter == "e").French > 0.14);
    }

    [Fact]
    public void Longueurs_en_cases_de_1_a_15_avec_les_plus_longs_dans_la_derniere()
    {
        var bins = Read("period=week").WordLengths;

        Assert.Equal(Enumerable.Range(1, LettersApi.MaxLength), bins.Select(b => b.Length));
        Assert.Equal(4, bins[1].Count);
        Assert.Equal(1, bins[^1].Count);
        Assert.Equal(10, bins.Sum(b => b.Count));
        Assert.Equal(1, bins.Sum(b => b.French), 6);
        Assert.Equal(bins[1].French, bins.Max(b => b.French)); // les mots de 2 lettres dominent en français
    }

    [Fact]
    public void Moyenne_mediane_et_moyennes_par_nature()
    {
        var w = Read("period=week").Words;

        Assert.Equal(10, w.Count);
        Assert.Equal((4 * 2 + 2 * 5 + 20 + 3 * 8) / 10.0, w.Average!.Value, 6);
        Assert.Equal(5, w.Median);
        Assert.Equal((4 * 2 + 2 * 5 + 20) / 7.0, w.Prose!.Value, 6);
        Assert.Equal(8, w.Code!.Value, 6);
        Assert.Equal(WordLengths.French.Average, w.French);
    }

    [Fact]
    public void Longueur_nulle_ignoree_et_longueur_excessive_ramenee_a_30()
    {
        AddLength(20261126, SessionKind.Prose, 0, 5);
        AddLength(20261126, SessionKind.Prose, 40, 1);

        var l = Read("period=week");

        Assert.Equal(11, l.Words.Count);
        Assert.Equal(2, l.WordLengths[^1].Count);
        Assert.Equal((4 * 2 + 2 * 5 + 20 + 3 * 8 + 30) / 11.0, l.Words.Average!.Value, 6);
    }

    [Fact]
    public void Filtre_de_nature_applique_aux_lettres_et_aux_mots()
    {
        var l = Read("period=week&kind=code");

        Assert.Equal(10, Count(l, 'a'));
        Assert.Equal(0, Count(l, 'e'));
        Assert.Equal(3, l.Words.Count);
        Assert.Null(l.Words.Prose);
        Assert.False(l.DayTablesUnfiltered);
    }

    [Fact]
    public void Periode_sans_mot_donne_des_valeurs_nulles()
    {
        var l = Read("period=day");

        Assert.Equal(0, l.Words.Count);
        Assert.Null(l.Words.Average);
        Assert.Null(l.Words.Median);
        Assert.All(l.WordLengths, b => Assert.Equal(0, b.Count));
    }

    [Fact]
    public void Filtre_d_appli_signale_que_les_tables_par_jour_l_ignorent()
    {
        int app = _temp.Db.GetOrAddApp("winword.exe");

        var l = Read($"period=week&app={app}");

        Assert.True(l.DayTablesUnfiltered);
        Assert.Equal(100, l.Letters.Sum(x => x.Count));
    }

    [Fact]
    public void Sources_citees_pour_chaque_reference()
    {
        var sources = Read("period=week").Sources;

        Assert.Equal([LetterFrequencies.French.Source, LetterFrequencies.English.Source, WordLengths.French.Source], sources);
    }

    public void Dispose() => _temp.Dispose();
}
