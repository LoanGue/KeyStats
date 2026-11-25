using KeyStats.Core.Analysis;

namespace KeyStats.Core.Storage;

/// <summary>Chronologie : changements de fenêtre et pauses.</summary>
public static class TimelineQueries
{
    public static long AddFocus(this KeyStatsDb db, long ms, int appId, long? titleId, int? layoutId, FocusState flags)
    {
        using var cmd = db.Command(
            "INSERT INTO focus(ts_ms, app_id, title_id, layout_id, flags) VALUES($t, $a, $ti, $l, $f) RETURNING id",
            ms, appId, titleId, layoutId, (int)flags);
        return (long)cmd.ExecuteScalar()!;
    }

    /// <summary>Changements de fenêtre de [from, to), précédés du dernier avant <paramref name="fromMs"/>.</summary>
    public static IReadOnlyList<StoredFocus> GetFocus(this KeyStatsDb db, long fromMs, long toMs)
    {
        var list = new List<StoredFocus>();
        using var cmd = db.Command(
            """
            SELECT id, ts_ms, app_id, title_id, layout_id, flags FROM (
                SELECT * FROM (SELECT * FROM focus WHERE ts_ms < $f ORDER BY ts_ms DESC, id DESC LIMIT 1)
                UNION ALL
                SELECT * FROM focus WHERE ts_ms >= $f AND ts_ms < $t
            ) ORDER BY ts_ms, id
            """,
            fromMs, toMs);
        using var r = cmd.ExecuteReader();
        while (r.Read())
        {
            list.Add(new StoredFocus(
                r.GetInt64(0), r.GetInt64(1), r.GetInt32(2), r.IsDBNull(3) ? null : r.GetInt64(3),
                r.IsDBNull(4) ? null : r.GetInt32(4), (FocusState)r.GetInt32(5)));
        }
        return list;
    }

    public static void AddPause(this KeyStatsDb db, long startMs, long endMs, PauseReason reason)
    {
        using var cmd = db.Command("INSERT INTO pause(start_ms, end_ms, reason) VALUES($s, $e, $r)", startMs, endMs, (int)reason);
        cmd.ExecuteNonQuery();
    }
}
