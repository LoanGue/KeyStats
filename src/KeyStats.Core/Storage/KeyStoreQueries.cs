using KeyStats.Core.Crypto;

namespace KeyStats.Core.Storage;

/// <summary>Table <c>key_store</c> : protections de la clé privée maîtresse.</summary>
public static class KeyStoreQueries
{
    public static void AddKeyStore(this KeyStatsDb db, KeyStoreEntry e)
    {
        using var cmd = db.Command(
            "INSERT INTO key_store(kind, created_ms, salt, iterations, nonce, wrapped, cred_id) VALUES($k, $c, $s, $i, $n, $w, $cid)",
            (int)e.Kind, e.CreatedMs, e.Salt, e.Iterations, e.Nonce, e.Wrapped, e.CredentialId);
        cmd.ExecuteNonQuery();
    }

    public static void DeleteKeyStore(this KeyStatsDb db, KeyStoreKind kind)
    {
        using var cmd = db.Command("DELETE FROM key_store WHERE kind = $k", (int)kind);
        cmd.ExecuteNonQuery();
    }

    public static IReadOnlyList<KeyStoreEntry> GetKeyStore(this KeyStatsDb db)
    {
        var list = new List<KeyStoreEntry>();
        using var cmd = db.Command("SELECT id, kind, created_ms, salt, iterations, nonce, wrapped, cred_id FROM key_store ORDER BY id");
        using var r = cmd.ExecuteReader();
        while (r.Read())
        {
            list.Add(new KeyStoreEntry(
                r.GetInt64(0), (KeyStoreKind)r.GetInt32(1), r.GetInt64(2), KeyStatsDb.Blob(r, 3), r.GetInt32(4), KeyStatsDb.Blob(r, 5), KeyStatsDb.Blob(r, 6),
                r.IsDBNull(7) ? null : r.GetString(7)));
        }
        return list;
    }
}
