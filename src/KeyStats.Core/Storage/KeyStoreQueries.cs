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

    /// <summary>
    /// Remplace la protection du même type en une transaction : l'ancienne enveloppe est écrasée de zéros
    /// dans sa page (<c>secure_delete</c>), puis le journal WAL est reporté dans la base et vidé, pour que
    /// ni le fichier ni son journal ne la gardent. Faux si le report a été bloqué par une lecture en cours
    /// (l'agent) : l'ancienne enveloppe peut alors rester dans le journal jusqu'au prochain report complet.
    /// </summary>
    public static bool ReplaceKeyStore(this KeyStatsDb db, KeyStoreEntry e)
    {
        db.Exec("PRAGMA secure_delete = ON");
        try
        {
            using var tx = db.BeginTransaction();
            db.DeleteKeyStore(e.Kind);
            db.AddKeyStore(e);
            tx.Commit();
        }
        finally
        {
            db.Exec("PRAGMA secure_delete = OFF");
        }
        return db.TruncateWal();
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
