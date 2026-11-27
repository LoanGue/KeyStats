using KeyStats.Core.Analysis;
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

    private void Add(int day, long start, long durationMs, long activeMs, int app, SessionKind kind) =>
        _temp.Db.Exec($"""
            INSERT INTO session (day, start_ms, end_ms, app_id, device_id, kind, chars, typed, deleted, keys, text_keys, repeats,
                active_ms, gross_cpm, net_cpm, real_cpm, peak_cpm, typos, collateral, corrections, unanalyzed, retyped, reformulations, overlaps, kspc)
            VALUES ({day}, {start}, {start + durationMs}, {app}, 1, {(int)kind}, 100, 100, 0, 100, 100, 0,
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
        ActivityApi.Register(router, new StatsContext(_temp.Db, () => At(Today, 12, 0), TimeZoneInfo.Utc));
        Assert.Equal(400, router.Handle("GET", WebRouter.Origin + ActivityApi.Path + "?period=decade").Status);
        Assert.Equal(200, router.Handle("GET", WebRouter.Origin + ActivityApi.Path + "?period=week").Status);
    }
}
