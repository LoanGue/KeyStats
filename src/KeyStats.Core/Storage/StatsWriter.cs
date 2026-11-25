using KeyStats.Core.Analysis;
using Microsoft.Data.Sqlite;

namespace KeyStats.Core.Storage;

/// <summary>
/// Verse un <see cref="StatsAccumulator"/> dans la base. Tout est additif (UPSERT) : on peut
/// écrire aussi souvent qu'on veut. Les histogrammes en BLOB sont relus, fusionnés et réécrits
/// (pas de fonction SQLite maison, pour rester compatible AOT).
/// À appeler dans une transaction ouverte par l'appelant, avec le bloc brut de la même période.
/// </summary>
public sealed class StatsWriter
{
    private readonly KeyStatsDb _db;

    public StatsWriter(KeyStatsDb db) => _db = db;

    public void Write(StatsAccumulator stats)
    {
        ArgumentNullException.ThrowIfNull(stats);
        if (stats.IsEmpty) return;
        WriteKeyMinutes(stats);
        WriteMinutes(stats);
        WriteAppHours(stats);
        WriteSessions(stats);
        WriteChars(stats);
        WriteBigrams(stats);
        WriteWordLengths(stats);
        WriteShortcuts(stats);
        WriteErrorPairs(stats);
        WriteMetrics(stats);
        WriteHistograms(stats);
    }

    /// <summary>
    /// Avant un recalcul depuis le brut : efface les statistiques des jours locaux [fromDay, toDay]
    /// entiers, bornes en ms calculées par <paramref name="clock"/> pour que les tables à la minute et
    /// les tables par jour couvrent la même période. Les touches des applis exclues n'ont pas de brut :
    /// leurs compteurs sont gardés. Renvoie la période effacée, à rejouer.
    /// Les jours déjà écrits l'ont été dans le fuseau de l'époque : après un changement de fuseau, les
    /// bornes calculées ici peuvent couper un ancien jour. L'appelant élargit alors la plage d'un jour de
    /// chaque côté, sans quoi les frappes en bordure seraient comptées deux fois au rejeu.
    /// </summary>
    public (long FromMs, long ToMs) DeleteDays(int fromDay, int toDay, LocalDayClock clock)
    {
        ArgumentNullException.ThrowIfNull(clock);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(fromDay, toDay);
        long fromMs = clock.MidnightUtcMs(LocalDayClock.DateOf(fromDay));
        long toMs = clock.MidnightUtcMs(LocalDayClock.DateOf(toDay).AddDays(1));
        long fromMin = LocalDayClock.MinuteOf(fromMs), toMin = LocalDayClock.MinuteOf(toMs);
        long fromHour = LocalDayClock.HourOf(fromMs), toHour = LocalDayClock.HourOf(toMs);
        Run("DELETE FROM key_minute WHERE ts_min >= $a AND ts_min < $b", fromMin, toMin);
        Run("UPDATE minute_total SET keys = 0, chars = 0, backspaces = 0, clicks = 0, writing_ms = 0 WHERE ts_min >= $a AND ts_min < $b", fromMin, toMin);
        Run("DELETE FROM minute_total WHERE ts_min >= $a AND ts_min < $b AND excluded_keys = 0", fromMin, toMin);
        Run("DELETE FROM app_hour WHERE ts_hour >= $a AND ts_hour < $b", fromHour, toHour);
        Run("DELETE FROM session WHERE start_ms >= $a AND start_ms < $b", fromMs, toMs);
        foreach (string table in (string[])["day_char", "day_bigram", "day_wordlen", "day_shortcut", "day_error_pair", "day_hist"])
            Run($"DELETE FROM {table} WHERE day >= $a AND day <= $b", fromDay, toDay);
        Run("DELETE FROM day_metric WHERE day >= $a AND day <= $b AND metric <> $m", fromDay, toDay, (int)DayMetric.ExcludedKeys);
        return (fromMs, toMs);
    }

    private void Run(string sql, params object?[] values)
    {
        using var cmd = _db.Command(sql, values);
        cmd.ExecuteNonQuery();
    }

    // ---- Tables à la minute et à l'heure ----

    private void WriteKeyMinutes(StatsAccumulator s)
    {
        if (s.KeyMinutes.Count == 0) return;
        using var cmd = Prepare(
            "INSERT INTO key_minute(ts_min, key, device_id, presses, repeats) VALUES($m, $k, $d, $p, $r) " +
            "ON CONFLICT DO UPDATE SET presses = presses + excluded.presses, repeats = repeats + excluded.repeats",
            "$m", "$k", "$d", "$p", "$r");
        foreach (var ((minute, key, device), c) in s.KeyMinutes)
            Exec(cmd, minute, key, device, c.Presses, c.Repeats);
    }

    private void WriteMinutes(StatsAccumulator s)
    {
        if (s.Minutes.Count == 0) return;
        using var cmd = Prepare(
            "INSERT INTO minute_total(ts_min, keys, chars, backspaces, clicks, writing_ms, excluded_keys) VALUES($m, $k, $c, $b, $cl, $w, $x) " +
            "ON CONFLICT DO UPDATE SET keys = keys + excluded.keys, chars = chars + excluded.chars, backspaces = backspaces + excluded.backspaces, " +
            "clicks = clicks + excluded.clicks, writing_ms = writing_ms + excluded.writing_ms, excluded_keys = excluded_keys + excluded.excluded_keys",
            "$m", "$k", "$c", "$b", "$cl", "$w", "$x");
        foreach (var (minute, t) in s.Minutes)
            Exec(cmd, minute, t.Keys, t.Chars, t.Backspaces, t.Clicks, t.WritingMs, t.ExcludedKeys);
    }

    private void WriteAppHours(StatsAccumulator s)
    {
        if (s.AppHours.Count == 0) return;
        using var cmd = Prepare(
            "INSERT INTO app_hour(ts_hour, app_id, keys, chars, writing_ms, sessions) VALUES($h, $a, $k, $c, $w, $s) " +
            "ON CONFLICT DO UPDATE SET keys = keys + excluded.keys, chars = chars + excluded.chars, " +
            "writing_ms = writing_ms + excluded.writing_ms, sessions = sessions + excluded.sessions",
            "$h", "$a", "$k", "$c", "$w", "$s");
        foreach (var ((hour, app), t) in s.AppHours)
            Exec(cmd, hour, app, t.Keys, t.Chars, t.WritingMs, t.Sessions);
    }

    private void WriteSessions(StatsAccumulator s)
    {
        if (s.Sessions.Count == 0) return;
        using var cmd = Prepare(
            """
            INSERT INTO session(day, start_ms, end_ms, app_id, title_id, device_id, kind, chars, typed, deleted, keys, text_keys,
                repeats, active_ms, gross_cpm, net_cpm, real_cpm, peak_cpm, typos, collateral, corrections, unanalyzed, retyped,
                reformulations, overlaps, kspc)
            VALUES($day, $start, $end, $app, $title, $dev, $kind, $chars, $typed, $deleted, $keys, $tkeys, $rep, $active,
                $gross, $net, $real, $peak, $typos, $coll, $corr, $unan, $retyped, $reform, $overlaps, $kspc)
            """,
            "$day", "$start", "$end", "$app", "$title", "$dev", "$kind", "$chars", "$typed", "$deleted", "$keys", "$tkeys", "$rep",
            "$active", "$gross", "$net", "$real", "$peak", "$typos", "$coll", "$corr", "$unan", "$retyped", "$reform", "$overlaps", "$kspc");
        foreach (var r in s.Sessions)
        {
            Exec(cmd,
                r.Day, r.StartMs, r.EndMs, r.AppId, r.TitleId == 0 ? null : r.TitleId, r.DeviceId, (int)r.Kind,
                r.Chars, r.Typed, r.Deleted, r.Keys, r.TextKeys, r.Repeats, r.ActiveMs,
                r.GrossCpm, r.NetCpm, r.RealCpm, r.PeakCpm, r.Typos, r.Collateral, r.Corrections, r.Unanalyzed, r.Retyped,
                r.Reformulations, r.Overlaps, r.Kspc);
        }
    }

    // ---- Tables par jour ----

    private void WriteChars(StatsAccumulator s)
    {
        if (s.Chars.Count == 0) return;
        using var cmd = Prepare(
            "INSERT INTO day_char(day, kind, ch, n) VALUES($d, $k, $c, $n) ON CONFLICT DO UPDATE SET n = n + excluded.n",
            "$d", "$k", "$c", "$n");
        foreach (var ((day, kind, ch), n) in s.Chars)
            Exec(cmd, day, (int)kind, (int)ch, n);
    }

    private void WriteBigrams(StatsAccumulator s)
    {
        if (s.Bigrams.Count == 0) return;
        using var find = Prepare("SELECT lat FROM day_bigram WHERE day = $d AND a = $a AND b = $b", "$d", "$a", "$b");
        using var cmd = Prepare(
            "INSERT INTO day_bigram(day, a, b, n, errors, lat) VALUES($d, $a, $b, $n, $e, $l) " +
            "ON CONFLICT DO UPDATE SET n = n + excluded.n, errors = errors + excluded.errors, lat = coalesce(excluded.lat, lat)",
            "$d", "$a", "$b", "$n", "$e", "$l");
        var merged = new int[LogScale.Buckets];
        foreach (var ((day, a, b), t) in s.Bigrams)
        {
            byte[]? lat = null;
            if (t.Latency is not null)
            {
                t.Latency.CopyTo(merged, 0);
                SetValues(find, day, (int)a, (int)b);
                if (find.ExecuteScalar() is byte[] old) HistogramCodec.AddTo(old, merged);
                lat = HistogramCodec.Encode(merged);
            }
            Exec(cmd, day, (int)a, (int)b, t.N, t.Errors, lat);
        }
    }

    private void WriteWordLengths(StatsAccumulator s)
    {
        if (s.WordLengths.Count == 0) return;
        using var cmd = Prepare(
            "INSERT INTO day_wordlen(day, len, n) VALUES($d, $l, $n) ON CONFLICT DO UPDATE SET n = n + excluded.n",
            "$d", "$l", "$n");
        foreach (var ((day, len), n) in s.WordLengths)
            Exec(cmd, day, len, n);
    }

    private void WriteShortcuts(StatsAccumulator s)
    {
        if (s.Shortcuts.Count == 0) return;
        using var cmd = Prepare(
            "INSERT INTO day_shortcut(day, mods, key, n) VALUES($d, $m, $k, $n) ON CONFLICT DO UPDATE SET n = n + excluded.n",
            "$d", "$m", "$k", "$n");
        foreach (var ((day, mods, key), n) in s.Shortcuts)
            Exec(cmd, day, (int)mods, key, n);
    }

    private void WriteErrorPairs(StatsAccumulator s)
    {
        if (s.ErrorPairs.Count == 0) return;
        using var cmd = Prepare(
            "INSERT INTO day_error_pair(day, typed, wanted, type, n) VALUES($d, $t, $w, $ty, $n) ON CONFLICT DO UPDATE SET n = n + excluded.n",
            "$d", "$t", "$w", "$ty", "$n");
        foreach (var ((day, typed, wanted, type), n) in s.ErrorPairs)
            Exec(cmd, day, (int)typed, (int)wanted, (int)type, n);
    }

    private void WriteMetrics(StatsAccumulator s)
    {
        if (s.Metrics.Count == 0) return;
        using var cmd = Prepare(
            "INSERT INTO day_metric(day, metric, value) VALUES($d, $m, $v) ON CONFLICT DO UPDATE SET value = value + excluded.value",
            "$d", "$m", "$v");
        foreach (var ((day, metric), value) in s.Metrics)
            Exec(cmd, day, (int)metric, value);
    }

    private void WriteHistograms(StatsAccumulator s)
    {
        if (s.Histograms.Count == 0) return;
        using var find = Prepare("SELECT data FROM day_hist WHERE day = $d AND hist = $h AND sub = $s", "$d", "$h", "$s");
        using var cmd = Prepare(
            "INSERT INTO day_hist(day, hist, sub, data) VALUES($d, $h, $s, $data) ON CONFLICT DO UPDATE SET data = excluded.data",
            "$d", "$h", "$s", "$data");
        foreach (var ((day, kind, sub), counts) in s.Histograms)
        {
            var merged = (int[])counts.Clone();
            SetValues(find, day, (int)kind, sub);
            if (find.ExecuteScalar() is byte[] old) HistogramCodec.AddTo(old, merged);
            Exec(cmd, day, (int)kind, sub, HistogramCodec.Encode(merged));
        }
    }

    // ---- Commandes réutilisées (une préparation par table et par écriture) ----

    private SqliteCommand Prepare(string sql, params string[] names)
    {
        var cmd = _db.Connection.CreateCommand();
        cmd.CommandText = sql;
        foreach (string n in names) cmd.Parameters.Add(new SqliteParameter(n, null));
        return cmd;
    }

    private static void SetValues(SqliteCommand cmd, params object?[] values)
    {
        for (int i = 0; i < values.Length; i++) cmd.Parameters[i].Value = values[i] ?? DBNull.Value;
    }

    private static void Exec(SqliteCommand cmd, params object?[] values)
    {
        SetValues(cmd, values);
        cmd.ExecuteNonQuery();
    }
}
