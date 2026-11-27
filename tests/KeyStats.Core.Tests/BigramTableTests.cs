using KeyStats.Core.Analysis;
using KeyStats.Core.Stats;
using KeyStats.Core.Storage;

namespace KeyStats.Core.Tests;

public sealed class BigramTableTests : IDisposable
{
    private static readonly LogScale s_lat = LogScale.BigramLatency;
    private readonly TempDb _temp = new();

    public BigramTableTests()
    {
        // 180 délais mesurés, dont 100 dans la case 10 : la médiane est le milieu de cette case.
        Add('q', 'u', 100, 0, (10, 100));
        // 30 passages, dont 20 mesurés : 10 dans la case 12 et 10 dans la case 20.
        Add('m', 'o', 30, 1, (12, 10), (20, 10));
        Add('c', 'd', 40, 0, (14, 40));
        Add('a', 'b', 20, 10, (11, 20));
    }

    private void Add(char a, char b, int n, int errors, params (int Bucket, int N)[] latency) =>
        AddOn(20261126, a, b, n, errors, latency);

    private void AddOn(int day, char a, char b, int n, int errors, params (int Bucket, int N)[] latency)
    {
        var counts = new int[LogScale.Buckets];
        foreach (var (bucket, k) in latency) counts[bucket] = k;
        string blob = Convert.ToHexString(HistogramCodec.Encode(counts));
        _temp.Db.Exec($"INSERT INTO day_bigram VALUES ({day}, {(int)a}, {(int)b}, {n}, {errors}, X'{blob}')");
    }

    private BigramTable Read() => BigramTable.Read(_temp.Db, new DayRange(20261121, 20261127));

    [Fact]
    public void Delai_moyen_pondere_par_les_seuls_delais_mesures()
    {
        var mo = Read().Ranked(10, 20).Single(b => b.A + b.B == "mo");
        double delay = (s_lat.Middle(12) + s_lat.Middle(20)) / 2 - s_lat.Middle(10);
        Assert.Equal(delay, mo.DelayMs, 6);
        Assert.Equal(30 * delay, mo.CostMs, 6);
        Assert.Equal(1.0 / 31, mo.ErrorRate, 6);
    }

    [Fact]
    public void Union_des_plus_couteux_et_des_plus_fautifs_sans_doublon()
    {
        // Coût : « mo » puis « cd » ; taux de faute : « ab » (10 sur 30) puis « mo ». « qu », à la médiane et sans faute, n'y est pas.
        var ranked = Read().Ranked(10, 2);
        Assert.Equal(["mo", "cd", "ab"], ranked.Select(b => b.A + b.B));
        Assert.Equal(10.0 / 30, ranked[2].ErrorRate, 6);
    }

    [Fact]
    public void Bigramme_a_la_mediane_exclu_du_classement_par_cout()
    {
        // « qu » n'a pas de faute : seul le classement par coût pourrait le retenir, et la place ne manque pas.
        Assert.DoesNotContain(Read().Ranked(10, 20), b => b.A + b.B == "qu");
    }

    [Fact]
    public void Egalites_departagees_par_ordre_ordinal()
    {
        // Un jour plus tard, donc lus après leurs égaux : sans départage, l'ordre de lecture l'emporterait.
        AddOn(20261127, 'b', 'd', 40, 0, (14, 40));
        AddOn(20261127, 'c', 'a', 40, 0, (14, 40));
        AddOn(20261127, 'a', 'a', 20, 10, (11, 20));
        var table = Read();
        Assert.Equal(["mo", "bd", "ca", "cd", "aa", "ab"], table.Ranked(10, 20).Select(b => b.A + b.B));
        // Un seul bigramme par classement : « aa » passe devant « ab », à taux et nombre égaux.
        Assert.Equal(["mo", "aa"], table.Ranked(10, 1).Select(b => b.A + b.B));
    }

    [Fact]
    public void Meme_doigt_et_alternance_selon_le_doigte_standard()
    {
        // « qu » change de main ; « cd » reste au majeur gauche ; « mo » et « ab » restent sur une main, à deux doigts.
        Assert.Equal(new RhythmFlow(190, 40, 100), Read().Flow());
    }

    public void Dispose() => _temp.Dispose();
}
