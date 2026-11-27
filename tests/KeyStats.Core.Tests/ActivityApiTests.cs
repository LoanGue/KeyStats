using KeyStats.Core.Analysis;
using KeyStats.Core.Crypto;
using KeyStats.Core.Storage;
using KeyStats.Core.Stats;
using KeyStats.Core.Web;

namespace KeyStats.Core.Tests;

public sealed class ActivityApiTests : IDisposable
{
    private const int Today = 20261127; // un vendredi
    private const long Minute = 60_000;
    private readonly TempDb _temp = new();
    private readonly int _word, _code;

    public ActivityApiTests()
    {
        var db = _temp.Db;
        _word = db.GetOrAddApp("winword.exe");
        _code = db.GetOrAddApp("code.exe");
        // Jour, début (UTC), durée, temps actif, appli, nature.
        Add(20261127, At(20261127, 10, 50), 20 * Minute, 20 * Minute, _word, SessionKind.Prose);
        Add(20261126, At(20261126, 9, 0), 10 * Minute, 6 * Minute, _code, SessionKind.Code);
        Add(20261126, At(20261126, 14, 0), 5 * Minute, 3 * Minute, _word, SessionKind.Prose);
        Add(20261118, At(20261118, 8, 0), 10 * Minute, 9 * Minute, _word, SessionKind.Prose);
        Add(20251201, At(20251201, 8, 0), 10 * Minute, 4 * Minute, _word, SessionKind.Prose);
        Add(20251120, At(20251120, 8, 0), 10 * Minute, 7 * Minute, _word, SessionKind.Prose);
    }

    public void Dispose() => _temp.Dispose();

    private static long At(int day, int hour, int minute) =>
        new DateTimeOffset(LocalDayClock.DateOf(day).AddHours(hour).AddMinutes(minute), TimeSpan.Zero).ToUnixTimeMilliseconds();

    private void Add(int day, long start, long durationMs, long activeMs, int app, SessionKind kind, long? title = null) =>
        _temp.Db.Exec($"""
            INSERT INTO session (day, start_ms, end_ms, app_id, title_id, device_id, kind, chars, typed, deleted, keys, text_keys, repeats,
                active_ms, gross_cpm, net_cpm, real_cpm, peak_cpm, typos, collateral, corrections, unanalyzed, retyped, reformulations, overlaps, kspc)
            VALUES ({day}, {start}, {start + durationMs}, {app}, {title?.ToString(System.Globalization.CultureInfo.InvariantCulture) ?? "NULL"}, 1, {(int)kind}, 100, 100, 0, 100, 100, 0,
                {activeMs}, 300, 0, 250, NULL, 0, 0, 0, 0, 0, 0, 0, 1)
            """);

    private Activity Read(string query, TimeZoneInfo? zone = null) =>
        ActivityApi.Read(_temp.Db, zone ?? TimeZoneInfo.Utc, StatsFilter.Parse(WebQuery.Parse(query), Today, 20251101)!);

    [Fact]
    public void Calendrier_des_53_dernieres_semaines_quelle_que_soit_la_periode()
    {
        var a = Read("period=day");
        Assert.Equal(("2026-11-27", "2026-11-27"), (a.From, a.To));
        Assert.Equal("2025-11-24", a.CalendarFrom); // lundi, 52 semaines avant celle d'aujourd'hui
        Assert.Equal([
            new ActivityDay("2025-12-01", 4 * Minute),
            new ActivityDay("2026-11-18", 9 * Minute),
            new ActivityDay("2026-11-26", 9 * Minute),
            new ActivityDay("2026-11-27", 20 * Minute),
        ], a.Calendar);
    }

    [Fact]
    public void Applis_de_la_periode_de_la_plus_utilisee_a_la_moins_utilisee()
    {
        Assert.Equal([new ActivityApp(_word, "winword.exe", 23 * Minute), new ActivityApp(_code, "code.exe", 6 * Minute)],
            Read("period=week").Apps);
        Assert.Equal([new ActivityApp(_word, "winword.exe", 20 * Minute)], Read("period=day").Apps);
    }

    [Fact]
    public void Les_filtres_valent_pour_le_calendrier_les_heures_et_les_applis()
    {
        var a = Read("period=week&kind=code");
        Assert.Equal([new ActivityDay("2026-11-26", 6 * Minute)], a.Calendar);
        Assert.Equal([new ActivityApp(_code, "code.exe", 6 * Minute)], a.Apps);
        Assert.Equal(6 * Minute, a.Hours.Sum(d => d.Sum()));
        Assert.Equal([new ActivityDay("2026-11-27", 20 * Minute)], Read($"period=week&app={_word}&kind=prose").Calendar.TakeLast(1));
        var other = Read("period=week&device=2");
        Assert.Empty(other.Calendar);
        Assert.Empty(other.Apps);
        Assert.Equal(0, other.Hours.Sum(d => d.Sum()));
    }

    [Fact]
    public void Heures_par_jour_de_la_semaine_session_a_cheval_repartie_au_prorata()
    {
        var hours = Read("period=week").Hours;
        Assert.Equal(7, hours.Count);
        Assert.All(hours, d => Assert.Equal(24, d.Count));
        // Vendredi 10 h 50 à 11 h 10 : 10 min de chaque côté de 11 h.
        Assert.Equal(10 * Minute, hours[4][10]);
        Assert.Equal(10 * Minute, hours[4][11]);
        Assert.Equal(6 * Minute, hours[3][9]);
        Assert.Equal(3 * Minute, hours[3][14]);
        Assert.Equal(29 * Minute, hours.Sum(d => d.Sum()));
    }

    [Fact]
    public void Heures_locales_du_fuseau_y_compris_decale_d_une_demi_heure()
    {
        var india = TimeZoneInfo.CreateCustomTimeZone("IST", TimeSpan.FromMinutes(330), "IST", "IST");
        var hours = Read("period=week", india).Hours;
        // 10 h 50 à 11 h 10 UTC : 16 h 20 à 16 h 40 en Inde.
        Assert.Equal(20 * Minute, hours[4][16]);
        // 9 h UTC le jeudi : 14 h 30 en Inde.
        Assert.Equal(6 * Minute, hours[3][14]);
    }

    [Fact]
    public void Session_sans_duree_comptee_a_son_heure_de_debut()
    {
        var grid = new double[7, 24];
        ActivityApi.Spread(grid, TimeZoneInfo.Utc, At(Today, 23, 59), At(Today, 23, 59), 500);
        Assert.Equal(500, grid[4, 23]);
    }

    [Fact]
    public void Route_refuse_un_filtre_invalide()
    {
        var router = new WebRouter(Path.GetTempPath());
        using var session = new KeySession();
        ActivityApi.Register(router, new StatsContext(_temp.Db, () => At(Today, 12, 0), TimeZoneInfo.Utc), session);
        Assert.Equal(400, router.Handle("GET", WebRouter.Origin + ActivityApi.Path + "?period=decade").Status);
        Assert.Equal(200, router.Handle("GET", WebRouter.Origin + ActivityApi.Path + "?period=week").Status);
    }

    private long[] AddTitles(MasterKey master, params string[] titles)
    {
        using var writer = new RawArchiveWriter(_temp.Db, master.PublicKey, new TitleHasher(TitleHasher.NewKey()));
        return [.. titles.Select(t => writer.GetOrAddTitle(_word, "winword.exe", t, At(Today, 8, 0)))];
    }

    [Fact]
    public void Titres_les_plus_utilises_puis_les_autres_et_le_temps_sans_titre()
    {
        using var master = MasterKey.Generate();
        var ids = AddTitles(master, [.. Enumerable.Range(1, 12).Select(i => $"Rapport {i}.docx - Word")]);
        for (int i = 0; i < ids.Length; i++)
            Add(20261125, At(20261125, 8, i), Minute, (20 - i) * 1000, _word, SessionKind.Prose, ids[i]);
        Add(20261125, At(20261125, 9, 0), Minute, 2000, _code, SessionKind.Code, ids[0]);

        var list = ActivityTitles.Read(_temp.Db, master, StatsFilter.Parse(WebQuery.Parse("period=week"), Today, 20251101)!, _word);

        Assert.Equal(_word, list.App);
        Assert.Equal(ActivityTitles.MaxTitles, list.Titles.Count);
        Assert.Equal(new ActivityTitle("Rapport 1.docx - Word", 20_000), list.Titles[0]);
        Assert.Equal(new ActivityTitle("Rapport 10.docx - Word", 11_000), list.Titles[^1]);
        Assert.Equal(10_000 + 9_000, list.OtherMs);
        // Les sessions de Word sans titre de cette semaine : 20 + 3 min.
        Assert.Equal(23 * Minute, list.UntitledMs);
    }

    [Fact]
    public void Titre_illisible_compte_avec_les_autres()
    {
        using var master = MasterKey.Generate();
        var ids = AddTitles(master, "Lettre.docx - Word", "Notes.docx - Word");
        Add(20261127, At(20261127, 6, 0), Minute, 5000, _word, SessionKind.Prose, ids[0]);
        Add(20261127, At(20261127, 7, 0), Minute, 4000, _word, SessionKind.Prose, ids[1]);
        _temp.Db.Exec($"UPDATE window_title SET title_enc = zeroblob(length(title_enc)) WHERE id = {ids[0]}");

        var list = ActivityTitles.Read(_temp.Db, master, StatsFilter.Parse(WebQuery.Parse("period=day"), Today, 20251101)!, _word);

        Assert.Equal([new ActivityTitle("Notes.docx - Word", 4000)], list.Titles);
        Assert.Equal(5000, list.OtherMs);
    }

    [Fact]
    public void Route_des_titres_seulement_apres_deverrouillage()
    {
        using var master = MasterKey.Generate();
        var ids = AddTitles(master, "Lettre.docx - Word");
        Add(20261127, At(20261127, 6, 0), Minute, 5000, _word, SessionKind.Prose, ids[0]);
        var router = new WebRouter(Path.GetTempPath());
        using var session = new KeySession();
        ActivityApi.Register(router, new StatsContext(_temp.Db, () => At(Today, 12, 0), TimeZoneInfo.Utc), session);
        string url = WebRouter.Origin + ActivityTitles.Path + $"?period=day&target={_word}";

        var locked = router.Handle("GET", url);
        Assert.Equal(403, locked.Status);
        Assert.DoesNotContain("Lettre", System.Text.Encoding.UTF8.GetString(locked.Body), StringComparison.Ordinal);
        // A bad request is reported as such even while the key is locked.
        Assert.Equal(400, router.Handle("GET", url.Replace("period=day", "period=decade", StringComparison.Ordinal)).Status);

        session.Adopt(MasterKey.FromPkcs8(master.ExportPkcs8()));
        Assert.Equal(400, router.Handle("GET", WebRouter.Origin + ActivityTitles.Path + "?period=day").Status);
        Assert.Equal(400, router.Handle("GET", url.Replace("period=day", "period=decade", StringComparison.Ordinal)).Status);
        var open = router.Handle("GET", url);
        Assert.Equal(200, open.Status);
        Assert.Contains("Lettre.docx - Word", System.Text.Encoding.UTF8.GetString(open.Body), StringComparison.Ordinal);

        session.Lock();
        Assert.Equal(403, router.Handle("GET", url).Status);
    }
}
