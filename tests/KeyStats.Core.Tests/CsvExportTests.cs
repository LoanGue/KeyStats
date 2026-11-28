using System.Text;
using KeyStats.Core.Analysis;
using KeyStats.Core.Stats;

namespace KeyStats.Core.Tests;

public sealed class CsvExportTests : IDisposable
{
    private static readonly LocalDayClock Paris = new(TimeZoneInfo.FindSystemTimeZoneById("Europe/Paris"));
    private readonly TempDb _temp = new();
    private readonly int _word, _code, _laptop, _external;
    private readonly string _folder = Path.Combine(Path.GetTempPath(), "keystats-tests", "csv-" + Guid.NewGuid().ToString("N"));

    public CsvExportTests()
    {
        var db = _temp.Db;
        _word = db.GetOrAddApp("winword.exe");
        _code = db.GetOrAddApp("code.exe");
        _laptop = db.GetOrAddDevice([1], "Portable; intégré", 1);
        _external = db.GetOrAddDevice([2], "=Externe", 1);
        // 26 novembre 2026, 10 h à Paris (9 h UTC) ; 25 novembre, 23 h 30 à Paris.
        Add(20261126, At(2026, 11, 26, 9, 0), _word, _laptop, SessionKind.Prose, 250, 260, 13, 30_000, 60_000);
        Add(20261126, At(2026, 11, 26, 11, 0), _code, _external, SessionKind.Code, 150, 160, 0, 60_000, 60_000);
        Add(20261125, At(2026, 11, 25, 22, 30), _word, _laptop, SessionKind.Prose, 100, 100, 0, 20_000, 30_000);
        AddPresses(At(2026, 11, 26, 9, 0), 0x12, _laptop, 30, 2);
        AddPresses(At(2026, 11, 26, 9, 1), 0x12, _external, 10, 0);
        AddPresses(At(2026, 11, 26, 9, 0), 0x39, _laptop, 50, 0);
        // 24 novembre à 23 h 59 heure de Paris : hors de la période.
        AddPresses(At(2026, 11, 24, 22, 59), 0x39, _laptop, 1000, 0);
    }

    public void Dispose()
    {
        _temp.Dispose();
        if (Directory.Exists(_folder)) Directory.Delete(_folder, recursive: true);
    }

    private static long At(int y, int mo, int d, int h, int mi) => new DateTimeOffset(y, mo, d, h, mi, 0, TimeSpan.Zero).ToUnixTimeMilliseconds();

    private static StatsFilter Filter(int? app = null, SessionKind? kind = null, int? device = null) =>
        new(StatsPeriod.All, new DayRange(20261125, 20261126), null, app, kind, device);

    private void Add(int day, long start, int app, int device, SessionKind kind, int chars, int typed, int typos, long activeMs, long durationMs) =>
        _temp.Db.Exec($"""
            INSERT INTO session (day, start_ms, end_ms, app_id, device_id, kind, chars, typed, deleted, keys, text_keys, repeats,
                active_ms, gross_cpm, net_cpm, real_cpm, peak_cpm, typos, collateral, corrections, unanalyzed, retyped, reformulations, overlaps, kspc)
            VALUES ({day}, {start}, {start + durationMs}, {app}, {device}, {(int)kind}, {chars}, {typed}, {typed - chars}, {typed + 5}, {typed}, 0,
                {activeMs}, 300, 0, 250, NULL, {typos}, 0, {typos}, 0, 0, 0, 0, 1)
            """);

    private void AddPresses(long ms, int key, int device, int presses, int repeats) =>
        _temp.Db.Exec($"INSERT INTO key_minute VALUES ({LocalDayClock.MinuteOf(ms)}, {key}, {device}, {presses}, {repeats})");

    private static string[] Lines(CsvTable csv)
    {
        byte[] bytes = csv.ToBytes();
        Assert.Equal(Encoding.UTF8.GetPreamble(), bytes[..3]);
        string text = Encoding.UTF8.GetString(bytes, 3, bytes.Length - 3);
        Assert.EndsWith("\r\n", text, StringComparison.Ordinal);
        return text[..^2].Split("\r\n");
    }

    [Fact]
    public void Champs_proteges_et_virgule_decimale()
    {
        var csv = new CsvTable("a", "b", "c", "d", "e").Text("x;y").Text("dit \"oui\"").Text("-2+3").Number(12.346, 2).Number(null);
        Assert.Equal(["a;b;c;d;e", "\"x;y\";\"dit \"\"oui\"\"\";'-2+3;12,35;"], Lines(csv));
        Assert.Throws<InvalidOperationException>(() => new CsvTable("a", "b").Text("seul").ToBytes());
    }

    [Fact]
    public void Un_jour_par_ligne_vitesse_et_precision_sur_les_sommes()
    {
        Assert.Equal([
            "jour;sessions;durée (min);écriture active (min);touches;caractères;vitesse réelle (mots/min);précision (%)",
            "2026-11-25;1;0,5;0,3;105;100;40;100",
            // 400 caractères en 2 minutes ; précision sur la prose seule : 1 − 13 / 260.
            "2026-11-26;2;2;1,5;430;400;40;95",
        ], Lines(CsvExport.Days(_temp.Db, Filter())));
    }

    [Fact]
    public void Les_filtres_de_session_s_appliquent_aux_jours_et_aux_sessions()
    {
        Assert.Equal(2, Lines(CsvExport.Days(_temp.Db, Filter(app: _code))).Length);
        string[] sessions = Lines(CsvExport.Sessions(_temp.Db, Paris, Filter(kind: SessionKind.Prose)));
        Assert.Equal(3, sessions.Length);
        Assert.All(sessions[1..], line => Assert.Contains(";winword.exe;prose;", line, StringComparison.Ordinal));
    }

    [Fact]
    public void Sessions_en_heure_locale_sans_titre_de_fenetre()
    {
        string[] lines = Lines(CsvExport.Sessions(_temp.Db, Paris, Filter()));
        Assert.Equal(4, lines.Length);
        Assert.Equal("2026-11-25 23:30:00;2026-11-25 23:30:30;winword.exe;prose;\"Portable; intégré\";20;105;100;100;0;50;60;;0;0;100", lines[1]);
        Assert.Equal("2026-11-26 12:00:00;2026-11-26 12:01:00;code.exe;code;'=Externe;60;165;150;160;10;50;60;;0;0;", lines[3]);
    }

    [Fact]
    public void Touches_de_la_periode_en_jours_locaux_avec_le_filtre_clavier()
    {
        Assert.Equal(["touche;code;appuis;répétitions", "Espace;0x039;50;0", "E;0x012;40;2"],
            Lines(CsvExport.Keys(_temp.Db, Paris, Filter())));
        Assert.Equal(["touche;code;appuis;répétitions", "E;0x012;10;0"],
            Lines(CsvExport.Keys(_temp.Db, Paris, Filter(device: _external))));
    }

    [Fact]
    public void Ecrit_les_fichiers_coches_sans_fichier_temporaire()
    {
        Directory.CreateDirectory(_folder);
        var names = CsvExport.Write(_temp.Db, Paris, Filter(), CsvTables.Days | CsvTables.Keys, _folder);
        Assert.Equal(["keystats-jours-2026-11-25_2026-11-26.csv", "keystats-touches-2026-11-25_2026-11-26.csv"], names);
        Assert.Equal(names.Order(), Directory.GetFiles(_folder).Select(Path.GetFileName).Order());
        // Un second export de la même période remplace le premier.
        CsvExport.Write(_temp.Db, Paris, Filter(), CsvTables.Days, _folder);
        Assert.Equal(2, Directory.GetFiles(_folder).Length);
    }
}
