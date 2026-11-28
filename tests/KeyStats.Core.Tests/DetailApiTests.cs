using System.Globalization;
using System.Text;
using KeyStats.Core.Analysis;
using KeyStats.Core.Crypto;
using KeyStats.Core.Input;
using KeyStats.Core.Stats;
using KeyStats.Core.Storage;
using KeyStats.Core.Web;

namespace KeyStats.Core.Tests;

public sealed class DetailApiTests : IDisposable
{
    private const int Today = 20261127;
    private const ushort E = 0x12, X = 0x2D;
    private readonly TempDb _temp = new();
    private readonly int _word;

    public DetailApiTests() => _word = _temp.Db.GetOrAddApp("winword.exe");

    public void Dispose() => _temp.Dispose();

    private static long At(int day, int hour, int minute) =>
        new DateTimeOffset(LocalDayClock.DateOf(day).AddHours(hour).AddMinutes(minute), TimeSpan.Zero).ToUnixTimeMilliseconds();

    private long Add(int day, long start, long durationMs, int device = 1, double grossCpm = 300)
    {
        _temp.Db.Exec($"""
            INSERT INTO session (day, start_ms, end_ms, app_id, title_id, device_id, kind, chars, typed, deleted, keys, text_keys, repeats,
                active_ms, gross_cpm, net_cpm, real_cpm, peak_cpm, typos, collateral, corrections, unanalyzed, retyped, reformulations, overlaps, kspc)
            VALUES ({day}, {start}, {start + durationMs}, {_word}, NULL, {device}, {(int)SessionKind.Prose}, 100, 100, 0, 100, 100, 0,
                {durationMs}, {grossCpm.ToString(CultureInfo.InvariantCulture)}, 0, 250, NULL, 0, 0, 0, 0, 0, 0, 0, 1)
            """);
        return (long)_temp.Db.Scalar("SELECT MAX(id) FROM session")!;
    }

    private void Write(MasterKey master, params InputEvent[] events)
    {
        using var writer = new RawArchiveWriter(_temp.Db, master.PublicKey, new TitleHasher(TitleHasher.NewKey()));
        writer.WriteBlock(events, null, events[^1].Ms);
    }

    private static InputEvent Key(long ms, EventKind kind, ushort key, ushort device = 1) => new(ms, kind, key, device);

    private StatsContext Stats() => new(_temp.Db, () => At(Today, 12, 0), TimeZoneInfo.Utc);

    [Fact]
    public void SessionsMostRecentFirstWithTheirSpeed()
    {
        long older = Add(20261126, At(20261126, 9, 0), 60_000);
        long newer = Add(20261127, At(20261127, 8, 0), 30_000, grossCpm: 250);
        Add(20261101, At(20261101, 8, 0), 30_000);

        var list = DetailApi.Sessions(_temp.Db, StatsFilter.Parse(WebQuery.Parse("period=week"), Today, 20251101)!);

        Assert.Equal([newer, older], list.Sessions.Select(s => s.Id));
        Assert.Equal(new DetailSession(newer, At(20261127, 8, 0), _word, 30_000, 50), list.Sessions[0]);
        Assert.False(list.Truncated);
    }

    [Fact]
    public void KeysAreReadFromTheRawOfTheSessionKeyboard()
    {
        using var master = MasterKey.Generate();
        long start = At(Today, 8, 0);
        long id = Add(Today, start, 200);
        // Bloc d'avant la session, frappe d'un autre clavier, relâchement après la dernière touche.
        Write(master, Key(start - 30_000, EventKind.KeyDown, X), Key(start - 29_900, EventKind.KeyUp, X));
        Write(master, Key(start, EventKind.KeyDown, E), Key(start + 50, EventKind.KeyDown, X, 2), Key(start + 60, EventKind.KeyUp, E),
            Key(start + 200, EventKind.KeyDown, X), Key(start + 230, EventKind.KeyUp, X, 2), Key(start + 280, EventKind.KeyUp, X));

        var keys = DetailApi.Keys(_temp.Db, master, id, start, start + 200, 1);

        Assert.Equal([new DetailPress(E, 0, 60, null, false), new DetailPress(X, 200, 80, null, false)], keys.Presses);
        Assert.False(keys.Truncated);
        Assert.False(keys.Incomplete);
    }

    [Fact]
    public void AnUnreadableBlockMarksTheSessionIncomplete()
    {
        using var master = MasterKey.Generate();
        long start = At(Today, 8, 0);
        long id = Add(Today, start, 10_000);
        Write(master, Key(start, EventKind.KeyDown, E), Key(start + 60, EventKind.KeyUp, E));
        Write(master, Key(start + 5000, EventKind.KeyDown, X), Key(start + 5060, EventKind.KeyUp, X));
        _temp.Db.Exec("UPDATE raw_block SET payload = zeroblob(length(payload)) WHERE id = (SELECT MAX(id) FROM raw_block)");

        var keys = DetailApi.Keys(_temp.Db, master, id, start, start + 10_000, 1);

        Assert.Equal([E], keys.Presses.Select(p => (ushort)p.Key));
        Assert.True(keys.Incomplete);
    }

    [Fact]
    public void ASessionWithoutRawIsIncomplete()
    {
        using var master = MasterKey.Generate();
        long start = At(Today, 8, 0);
        long id = Add(Today, start, 10_000);

        var keys = DetailApi.Keys(_temp.Db, master, id, start, start + 10_000, 1);

        Assert.Empty(keys.Presses);
        Assert.True(keys.Incomplete);
    }

    [Fact]
    public void KeysRouteOnlyAfterUnlocking()
    {
        using var master = MasterKey.Generate();
        long start = At(Today, 8, 0);
        long id = Add(Today, start, 100);
        Write(master, Key(start, EventKind.KeyDown, E), Key(start + 60, EventKind.KeyUp, E));
        var router = new WebRouter(Path.GetTempPath());
        using var session = new KeySession();
        DetailApi.Register(router, Stats(), session);
        string url = WebRouter.Origin + DetailApi.KeysPath + $"?session={id}";

        Assert.Equal(403, router.Handle("GET", url).Status);
        Assert.Equal(400, router.Handle("GET", WebRouter.Origin + DetailApi.KeysPath).Status);
        Assert.Equal(400, router.Handle("GET", WebRouter.Origin + DetailApi.KeysPath + "?session=-1").Status);
        Assert.Equal(404, router.Handle("GET", WebRouter.Origin + DetailApi.KeysPath + $"?session={id + 1}").Status);

        session.Adopt(MasterKey.FromPkcs8(master.ExportPkcs8()));
        var open = router.Handle("GET", url);
        Assert.Equal(200, open.Status);
        Assert.Contains("\"presses\":[{\"key\":18,\"downMs\":0,\"holdMs\":60", Encoding.UTF8.GetString(open.Body), StringComparison.Ordinal);

        session.Lock();
        Assert.Equal(403, router.Handle("GET", url).Status);
    }

    [Fact]
    public void SessionsRouteFollowsTheFilters()
    {
        Add(Today, At(Today, 8, 0), 1000, device: 2);
        var router = new WebRouter(Path.GetTempPath());
        using var session = new KeySession();
        DetailApi.Register(router, Stats(), session);

        Assert.Equal(400, router.Handle("GET", WebRouter.Origin + DetailApi.Path + "?period=decade").Status);
        var mine = router.Handle("GET", WebRouter.Origin + DetailApi.Path + "?period=day&device=2");
        Assert.Equal(200, mine.Status);
        Assert.Contains("\"durationMs\":1000", Encoding.UTF8.GetString(mine.Body), StringComparison.Ordinal);
        var other = router.Handle("GET", WebRouter.Origin + DetailApi.Path + "?period=day&device=1");
        Assert.Contains("\"sessions\":[]", Encoding.UTF8.GetString(other.Body), StringComparison.Ordinal);
    }
}
