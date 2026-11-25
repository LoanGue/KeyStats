
namespace KeyStats.Core.Storage;

/// <summary>Brut chiffré : époques, titres de fenêtre et blocs.</summary>
public static class ArchiveQueries
{
    public static int AddEpoch(this KeyStatsDb db, long createdMs, byte[] ephemeralPublicKey)
    {
        using var cmd = db.Command("INSERT INTO key_epoch(created_ms, ephemeral_pub) VALUES($c, $p) RETURNING id", createdMs, ephemeralPublicKey);
        return (int)(long)cmd.ExecuteScalar()!;
    }

    public static byte[]? GetEpochPublicKey(this KeyStatsDb db, int epochId)
    {
        using var cmd = db.Command("SELECT ephemeral_pub FROM key_epoch WHERE id = $id", epochId);
        return cmd.ExecuteScalar() as byte[];
    }

    public static long? FindTitle(this KeyStatsDb db, byte[] titleHash)
    {
        using var cmd = db.Command("SELECT id FROM window_title WHERE title_hash = $h", titleHash);
        return cmd.ExecuteScalar() is long id ? id : null;
    }

    public static long AddTitle(this KeyStatsDb db, int appId, byte[] titleHash, int epochId, byte[] nonce, byte[] encrypted)
    {
        using var cmd = db.Command(
            "INSERT INTO window_title(app_id, title_hash, epoch_id, nonce, title_enc) VALUES($a, $h, $e, $n, $t) RETURNING id",
            appId, titleHash, epochId, nonce, encrypted);
        return (long)cmd.ExecuteScalar()!;
    }

    public static StoredTitle? GetTitle(this KeyStatsDb db, long id)
    {
        using var cmd = db.Command("SELECT app_id, epoch_id, nonce, title_enc FROM window_title WHERE id = $id", id);
        using var r = cmd.ExecuteReader();
        return r.Read() ? new StoredTitle(id, r.GetInt32(0), r.GetInt32(1), KeyStatsDb.Blob(r, 2), KeyStatsDb.Blob(r, 3)) : null;
    }

    /// <summary>Empreinte stockée d'un titre (sert de données associées au déchiffrement).</summary>
    public static byte[]? GetTitleHash(this KeyStatsDb db, long id)
    {
        using var cmd = db.Command("SELECT title_hash FROM window_title WHERE id = $id", id);
        return cmd.ExecuteScalar() as byte[];
    }

    public static int CountTitles(this KeyStatsDb db) => (int)(long)db.Scalar("SELECT count(*) FROM window_title")!;

    public static long AddRawBlock(this KeyStatsDb db, long startMs, long endMs, long? focusId, int eventCount, int epochId, byte[] nonce, byte[] payload)
    {
        using var cmd = db.Command(
            "INSERT INTO raw_block(start_ms, end_ms, focus_id, n_events, epoch_id, nonce, payload) VALUES($s, $e, $f, $n, $ep, $no, $p) RETURNING id",
            startMs, endMs, focusId, eventCount, epochId, nonce, payload);
        return (long)cmd.ExecuteScalar()!;
    }

    public static IReadOnlyList<StoredRawBlock> GetRawBlocks(this KeyStatsDb db, long fromMs, long toMs)
    {
        var list = new List<StoredRawBlock>();
        using var cmd = db.Command(
            "SELECT id, start_ms, end_ms, focus_id, n_events, epoch_id, nonce, payload FROM raw_block WHERE start_ms >= $f AND start_ms < $t ORDER BY start_ms, id",
            fromMs, toMs);
        using var r = cmd.ExecuteReader();
        while (r.Read())
        {
            list.Add(new StoredRawBlock(
                r.GetInt64(0), r.GetInt64(1), r.GetInt64(2), r.IsDBNull(3) ? null : r.GetInt64(3),
                r.GetInt32(4), r.GetInt32(5), KeyStatsDb.Blob(r, 6), KeyStatsDb.Blob(r, 7)));
        }
        return list;
    }

}
