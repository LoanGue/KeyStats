using KeyStats.Core.Analysis;
using KeyStats.Core.Stats;
using KeyStats.Core.Storage;
using KeyStats.Core.Web;

namespace KeyStats.Core.Tests;

public sealed class TyposApiTests : IDisposable
{
    private const int Today = 20261127; // un vendredi
    private readonly TempDb _temp = new();
    private readonly int _word, _code, _keyboard;

    public TyposApiTests()
    {
        var db = _temp.Db;
        _word = db.GetOrAddApp("winword.exe");
        _code = db.GetOrAddApp("code.exe");
        _keyboard = db.GetOrAddDevice([1], "Clavier", 1);
        // Jour, appli, nature, caractères nets, tapés, touches de texte, fautes, effacements collatéraux.
        AddSession(20261126, _word, SessionKind.Prose, 380, 400, 420, 20, 6);
        AddSession(20261125, _code, SessionKind.Prose, 90, 100, 110, 10, 4);
        AddSession(20261125, _code, SessionKind.Code, 500, 600, 700, 50, 50);
        AddSession(20261118, _word, SessionKind.Prose, 100, 100, 100, 0, 0);

        AddPair(20261126, 'z', 'e', ErrorType.Substitution, 7);
        AddPair(20261125, 'z', 'e', ErrorType.Substitution, 2);
        AddPair(20261126, 'p', 'e', ErrorType.Substitution, 3);
        AddPair(20261126, 'e', 'é', ErrorType.Substitution, 4);
        AddPair(20261126, '\0', 'é', ErrorType.Omission, 1);
        AddPair(20261118, 'a', 'q', ErrorType.Substitution, 50);
        AddChar(20261126, SessionKind.Prose, 'é', 100);
        AddChar(20261126, SessionKind.Code, 'é', 900);
        AddChar(20261125, SessionKind.Prose, 'É', 8);

        AddMetric(20261126, DayMetric.Substitutions, 16);
        AddMetric(20261126, DayMetric.Omissions, 1);
        AddMetric(20261126, DayMetric.Backspaces, 120);
        AddMetric(20261126, DayMetric.BackspaceRepeats, 30);
        AddMetric(20261126, DayMetric.WordBackspaces, 5);
        AddMetric(20261126, DayMetric.WordBackspaceChars, 32);
        AddMetric(20261118, DayMetric.Backspaces, 1000);
    }

    private void AddSession(int day, int app, SessionKind kind, int chars, int typed, int textKeys, int typos, int collateral) =>
        _temp.Db.Exec($"""
            INSERT INTO session (day, start_ms, end_ms, app_id, device_id, kind, chars, typed, deleted, keys, text_keys, repeats,
                active_ms, gross_cpm, net_cpm, real_cpm, peak_cpm, typos, collateral, corrections, unanalyzed, retyped, reformulations, overlaps, kspc)
            VALUES ({day}, 0, 60000, {app}, {_keyboard}, {(int)kind}, {chars}, {typed}, {typed - chars}, {textKeys}, {textKeys}, 0,
                60000, 300, 0, 250, NULL, {typos}, {collateral}, 0, 0, 0, 0, 0, 1)
            """);

    private void AddPair(int day, char typed, char wanted, ErrorType type, int n) =>
        _temp.Db.Exec($"INSERT INTO day_error_pair VALUES ({day}, {(int)typed}, {(int)wanted}, {(int)type}, {n})");

    private void AddChar(int day, SessionKind kind, char c, int n) =>
        _temp.Db.Exec($"INSERT INTO day_char VALUES ({day}, {(int)kind}, {(int)c}, {n})");

    private void AddMetric(int day, DayMetric metric, long value) =>
        _temp.Db.Exec($"INSERT INTO day_metric VALUES ({day}, {(int)metric}, {value})");

    private Typos Read(string query) => TyposApi.Read(_temp.Db, StatsFilter.Parse(WebQuery.Parse(query), Today, 20261101)!);

    [Fact]
    public void Precision_jour_par_jour_sur_la_prose()
    {
        var t = Read("period=week");
        Assert.Equal(("2026-11-21", "2026-11-27", false), (t.From, t.To, t.DayTablesUnfiltered));
        Assert.Equal(new TyposDay("2026-11-21", 0, null), t.Days[0]);
        Assert.Equal(new TyposDay("2026-11-25", 100, 0.9), t.Days[4]);
        Assert.Equal(new TyposDay("2026-11-26", 400, 0.95), t.Days[5]);
        // 30 fautes sur 500 caractères de prose ; la session de code ne compte pas.
        Assert.Equal(0.94, t.Accuracy!.Value, 6);
    }

    [Fact]
    public void Cout_des_corrections_sur_la_prose()
    {
        var cost = Read("period=week").Cost;
        // 530 touches de texte pour 470 caractères conservés.
        Assert.Equal((30L, 10L, 530L, 60L), (cost.Typos, cost.Collateral, cost.Keys, cost.LostKeys));
        Assert.Equal(530.0 / 470, cost.Kspc!.Value, 6);
    }

    [Fact]
    public void Paires_les_plus_confondues_avec_les_voisines_azerty()
    {
        var pairs = Read("period=week").Pairs;
        Assert.Equal([
            new TyposPair("z", "e", true, 9),
            new TyposPair("e", "é", false, 4),
            new TyposPair("p", "e", false, 3),
        ], pairs);
    }

    [Fact]
    public void Caracteres_accentues_fautes_sur_leurs_occurrences_en_prose()
    {
        var accents = Read("period=week").Accents;
        Assert.Equal((TyposApi.AccentedChars.Length, new TyposChar("é", 100, 5)), (accents.Count, accents[0]));
        Assert.Equal(new TyposChar("É", 8, 0), accents.Single(a => a.Character == "É"));
    }

    [Fact]
    public void Types_et_maniere_d_effacer_depuis_les_compteurs_du_jour()
    {
        var t = Read("period=week");
        Assert.Equal(new TyposTypes(16, 1, 0, 0), t.Types);
        Assert.Equal(new TyposDeletion(90, 30, 32, 5), t.Deletion);
    }

    [Fact]
    public void Delai_de_detection_mediane_et_cases()
    {
        var counts = new int[LogScale.Buckets];
        counts[10] = 3;
        counts[12] = 1;
        _temp.Db.Exec($"INSERT INTO day_hist VALUES (20261126, {(int)HistogramKind.DetectionDelayMs}, 0, "
            + $"X'{Convert.ToHexString(HistogramCodec.Encode(counts))}')");
        var delay = Read("period=week").Delay;
        Assert.Equal(13, delay.Bins.Count);
        Assert.Equal(3, delay.Bins[10].Count);
        Assert.Equal((LogScale.DetectionDelay.LowerBound(12), LogScale.DetectionDelay.UpperBound(12)), (delay.Bins[12].FromMs, delay.Bins[12].ToMs!.Value));
        Assert.Equal(LogScale.DetectionDelay.Median(counts), delay.MedianMs);
    }

    [Fact]
    public void Filtre_d_appli_sur_les_sessions_seulement()
    {
        var t = Read($"period=week&app={_word}");
        Assert.True(t.DayTablesUnfiltered);
        Assert.Equal(0.95, t.Accuracy!.Value, 6);
        Assert.Equal(20, t.Cost.Typos);
        Assert.Equal(3, t.Pairs.Count);
    }

    [Fact]
    public void Periode_sans_frappe()
    {
        var t = Read("period=day");
        Assert.Equal([new TyposDay("2026-11-27", 0, null)], t.Days);
        Assert.Null(t.Accuracy);
        Assert.Equal(new TyposCost(0, 0, null, 0, 0), t.Cost);
        Assert.Equal((null, 0), (t.Delay.MedianMs, t.Delay.Bins.Count));
        Assert.Equal(new TyposDeletion(0, 0, 0, 0), t.Deletion);
    }

    public void Dispose() => _temp.Dispose();
}
