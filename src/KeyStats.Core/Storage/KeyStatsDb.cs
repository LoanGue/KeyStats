using KeyStats.Core.Analysis;
using KeyStats.Core.Text;
using Microsoft.Data.Sqlite;

namespace KeyStats.Core.Storage;

/// <summary>Une protection de la clé privée (ligne de <c>key_store</c>).</summary>
public sealed record KeyStoreEntry(
    long Id, KeyStoreKind Kind, long CreatedMs, byte[] Salt, int Iterations, byte[] Nonce, byte[] Wrapped, string? CredentialId);

/// <summary>Un titre de fenêtre chiffré, tel qu'il est stocké.</summary>
public sealed record StoredTitle(long Id, int AppId, int EpochId, byte[] Nonce, byte[] Encrypted);

/// <summary>Un bloc brut chiffré, tel qu'il est stocké.</summary>
public sealed record StoredRawBlock(long Id, long StartMs, long EndMs, long? FocusId, int EventCount, int EpochId, byte[] Nonce, byte[] Payload);

/// <summary>Une ligne de la chronologie des fenêtres.</summary>
public sealed record StoredFocus(long Id, long Ms, int AppId, long? TitleId, int? LayoutId, FocusState Flags);

/// <summary>
/// Accès à <c>keystats.db</c> : ouverture, migrations, référentiels (avec cache) et chronologie.
/// Une instance par thread. Les écritures groupées passent par <see cref="BeginTransaction"/>.
/// </summary>
public sealed class KeyStatsDb : IDisposable
{
    private static readonly Lock s_initLock = new();
    private static bool s_initialized;

    private readonly SqliteConnection _conn;
    private readonly Dictionary<string, int> _apps = new(StringComparer.Ordinal);
    private readonly Dictionary<string, int> _layoutIds = new(StringComparer.Ordinal);
    private readonly Dictionary<int, LayoutTable> _layouts = [];
    private bool _inTransaction;

    private KeyStatsDb(SqliteConnection conn) => _conn = conn;

    public SqliteConnection Connection => _conn;

    /// <summary>Ouvre (et crée ou met à jour) la base.</summary>
    public static KeyStatsDb Open(string path, bool readOnly = false)
    {
        EnsureSqlite();
        var csb = new SqliteConnectionStringBuilder
        {
            DataSource = path,
            Mode = readOnly ? SqliteOpenMode.ReadOnly : SqliteOpenMode.ReadWriteCreate,
            Pooling = false,
        };
        var conn = new SqliteConnection(csb.ToString());
        conn.Open();
        var db = new KeyStatsDb(conn);
        try
        {
            db.Exec("PRAGMA busy_timeout = 5000");
            db.Exec("PRAGMA foreign_keys = ON");
            if (!readOnly)
            {
                db.Exec("PRAGMA journal_mode = WAL");
                db.Exec("PRAGMA synchronous = NORMAL");
                db.Migrate();
            }
            else if (db.UserVersion > Schema.CurrentVersion)
                throw new InvalidOperationException("Base créée par une version plus récente de KeyStats.");
        }
        catch
        {
            db.Dispose();
            throw;
        }
        return db;
    }

    /// <summary>SQLite natif enregistré explicitement : la découverte automatique ne survit pas à NativeAOT.</summary>
    private static void EnsureSqlite()
    {
        lock (s_initLock)
        {
            if (s_initialized) return;
            SQLitePCL.Batteries_V2.Init();
            s_initialized = true;
        }
    }

    public int UserVersion => (int)(long)Scalar("PRAGMA user_version")!;

    private void Migrate()
    {
        int version = UserVersion;
        if (version > Schema.CurrentVersion)
            throw new InvalidOperationException("Base créée par une version plus récente de KeyStats.");
        for (int v = version; v < Schema.CurrentVersion; v++)
        {
            using var tx = BeginTransaction();
            Exec(Schema.Migrations[v]);
            Exec($"PRAGMA user_version = {v + 1}");
            tx.Commit();
        }
    }

    // ---- Transactions ----

    /// <summary>Transaction en écriture (BEGIN IMMEDIATE). Annulée si elle n'est pas validée.</summary>
    public DbTransaction BeginTransaction()
    {
        if (_inTransaction) throw new InvalidOperationException("Transaction déjà ouverte.");
        Exec("BEGIN IMMEDIATE");
        _inTransaction = true;
        return new DbTransaction(this);
    }

    public sealed class DbTransaction : IDisposable
    {
        private KeyStatsDb? _db;

        internal DbTransaction(KeyStatsDb db) => _db = db;

        public void Commit()
        {
            var db = _db ?? throw new InvalidOperationException("Transaction terminée.");
            _db = null;
            db._inTransaction = false;
            db.Exec("COMMIT");
        }

        public void Dispose()
        {
            if (_db is null) return;
            var db = _db;
            _db = null;
            db._inTransaction = false;
            db.Exec("ROLLBACK");
        }
    }

    // ---- Méta ----

    public byte[]? GetMeta(string key)
    {
        using var cmd = Command("SELECT value FROM meta WHERE key = $k", key);
        return cmd.ExecuteScalar() as byte[];
    }

    public void SetMeta(string key, byte[]? value)
    {
        using var cmd = Command("INSERT INTO meta(key, value) VALUES($k, $v) ON CONFLICT(key) DO UPDATE SET value = excluded.value", key, value);
        cmd.ExecuteNonQuery();
    }

    // ---- Référentiels ----

    /// <summary>Identifiant de l'appli (nom d'exécutable, mis en minuscules).</summary>
    public int GetOrAddApp(string exe)
    {
        exe = exe.ToLowerInvariant();
        if (_apps.TryGetValue(exe, out int id)) return id;
        using (var find = Command("SELECT id FROM app WHERE exe = $e", exe))
        {
            if (find.ExecuteScalar() is long found)
            {
                _apps[exe] = (int)found;
                return (int)found;
            }
        }
        using var add = Command("INSERT INTO app(exe) VALUES($e) RETURNING id", exe);
        id = (int)(long)add.ExecuteScalar()!;
        _apps[exe] = id;
        return id;
    }

    public void SetAppExcluded(int appId, bool excluded)
    {
        using var cmd = Command("UPDATE app SET excluded = $x WHERE id = $id", excluded ? 1 : 0, appId);
        cmd.ExecuteNonQuery();
    }

    public IReadOnlyDictionary<int, string> GetApps()
    {
        var result = new Dictionary<int, string>();
        using var cmd = Command("SELECT id, exe FROM app");
        using var r = cmd.ExecuteReader();
        while (r.Read()) result[r.GetInt32(0)] = r.GetString(1);
        return result;
    }

    /// <summary>Clavier ou souris, reconnu par l'empreinte de son identifiant matériel.</summary>
    public int GetOrAddDevice(byte[] hwHash, string label, int kind)
    {
        using (var find = Command("SELECT id FROM device WHERE hw_hash = $h", hwHash))
        {
            if (find.ExecuteScalar() is long found) return (int)found;
        }
        using var add = Command("INSERT INTO device(label, kind, hw_hash) VALUES($l, $k, $h) RETURNING id", label, kind, hwHash);
        return (int)(long)add.ExecuteScalar()!;
    }

    public int GetOrAddLayout(LayoutTable layout)
    {
        byte[] hash = layout.ComputeHash();
        string key = Convert.ToHexString(hash);
        if (_layoutIds.TryGetValue(key, out int id)) return id;
        using (var find = Command("SELECT id FROM layout WHERE hash = $h", hash))
        {
            if (find.ExecuteScalar() is long found)
            {
                id = (int)found;
                _layoutIds[key] = id;
                _layouts[id] = layout;
                return id;
            }
        }
        using var add = Command("INSERT INTO layout(klid, hash, data) VALUES($k, $h, $d) RETURNING id", layout.Id, hash, layout.Serialize());
        id = (int)(long)add.ExecuteScalar()!;
        _layoutIds[key] = id;
        _layouts[id] = layout;
        return id;
    }

    public LayoutTable? GetLayout(int id)
    {
        if (_layouts.TryGetValue(id, out var cached)) return cached;
        using var cmd = Command("SELECT data FROM layout WHERE id = $id", id);
        if (cmd.ExecuteScalar() is not byte[] data) return null;
        var layout = LayoutTable.Deserialize(data);
        _layouts[id] = layout;
        return layout;
    }

    // ---- Chiffrement ----

    public int AddEpoch(long createdMs, byte[] ephemeralPublicKey)
    {
        using var cmd = Command("INSERT INTO key_epoch(created_ms, ephemeral_pub) VALUES($c, $p) RETURNING id", createdMs, ephemeralPublicKey);
        return (int)(long)cmd.ExecuteScalar()!;
    }

    public byte[]? GetEpochPublicKey(int epochId)
    {
        using var cmd = Command("SELECT ephemeral_pub FROM key_epoch WHERE id = $id", epochId);
        return cmd.ExecuteScalar() as byte[];
    }

    public void AddKeyStore(KeyStoreEntry e)
    {
        using var cmd = Command(
            "INSERT INTO key_store(kind, created_ms, salt, iterations, nonce, wrapped, cred_id) VALUES($k, $c, $s, $i, $n, $w, $cid)",
            (int)e.Kind, e.CreatedMs, e.Salt, e.Iterations, e.Nonce, e.Wrapped, e.CredentialId);
        cmd.ExecuteNonQuery();
    }

    public void DeleteKeyStore(KeyStoreKind kind)
    {
        using var cmd = Command("DELETE FROM key_store WHERE kind = $k", (int)kind);
        cmd.ExecuteNonQuery();
    }

    public IReadOnlyList<KeyStoreEntry> GetKeyStore()
    {
        var list = new List<KeyStoreEntry>();
        using var cmd = Command("SELECT id, kind, created_ms, salt, iterations, nonce, wrapped, cred_id FROM key_store ORDER BY id");
        using var r = cmd.ExecuteReader();
        while (r.Read())
        {
            list.Add(new KeyStoreEntry(
                r.GetInt64(0), (KeyStoreKind)r.GetInt32(1), r.GetInt64(2), Blob(r, 3), r.GetInt32(4), Blob(r, 5), Blob(r, 6),
                r.IsDBNull(7) ? null : r.GetString(7)));
        }
        return list;
    }

    // ---- Titres ----

    public long? FindTitle(byte[] titleHash)
    {
        using var cmd = Command("SELECT id FROM window_title WHERE title_hash = $h", titleHash);
        return cmd.ExecuteScalar() is long id ? id : null;
    }

    public long AddTitle(int appId, byte[] titleHash, int epochId, byte[] nonce, byte[] encrypted)
    {
        using var cmd = Command(
            "INSERT INTO window_title(app_id, title_hash, epoch_id, nonce, title_enc) VALUES($a, $h, $e, $n, $t) RETURNING id",
            appId, titleHash, epochId, nonce, encrypted);
        return (long)cmd.ExecuteScalar()!;
    }

    public StoredTitle? GetTitle(long id)
    {
        using var cmd = Command("SELECT app_id, epoch_id, nonce, title_enc FROM window_title WHERE id = $id", id);
        using var r = cmd.ExecuteReader();
        return r.Read() ? new StoredTitle(id, r.GetInt32(0), r.GetInt32(1), Blob(r, 2), Blob(r, 3)) : null;
    }

    /// <summary>Empreinte stockée d'un titre (sert de données associées au déchiffrement).</summary>
    public byte[]? GetTitleHash(long id)
    {
        using var cmd = Command("SELECT title_hash FROM window_title WHERE id = $id", id);
        return cmd.ExecuteScalar() as byte[];
    }

    public int CountTitles() => (int)(long)Scalar("SELECT count(*) FROM window_title")!;

    // ---- Chronologie ----

    public long AddFocus(long ms, int appId, long? titleId, int? layoutId, FocusState flags)
    {
        using var cmd = Command(
            "INSERT INTO focus(ts_ms, app_id, title_id, layout_id, flags) VALUES($t, $a, $ti, $l, $f) RETURNING id",
            ms, appId, titleId, layoutId, (int)flags);
        return (long)cmd.ExecuteScalar()!;
    }

    /// <summary>Changements de fenêtre de [from, to), précédés du dernier avant <paramref name="fromMs"/>.</summary>
    public IReadOnlyList<StoredFocus> GetFocus(long fromMs, long toMs)
    {
        var list = new List<StoredFocus>();
        using var cmd = Command(
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

    public long AddRawBlock(long startMs, long endMs, long? focusId, int eventCount, int epochId, byte[] nonce, byte[] payload)
    {
        using var cmd = Command(
            "INSERT INTO raw_block(start_ms, end_ms, focus_id, n_events, epoch_id, nonce, payload) VALUES($s, $e, $f, $n, $ep, $no, $p) RETURNING id",
            startMs, endMs, focusId, eventCount, epochId, nonce, payload);
        return (long)cmd.ExecuteScalar()!;
    }

    public IReadOnlyList<StoredRawBlock> GetRawBlocks(long fromMs, long toMs)
    {
        var list = new List<StoredRawBlock>();
        using var cmd = Command(
            "SELECT id, start_ms, end_ms, focus_id, n_events, epoch_id, nonce, payload FROM raw_block WHERE start_ms >= $f AND start_ms < $t ORDER BY start_ms, id",
            fromMs, toMs);
        using var r = cmd.ExecuteReader();
        while (r.Read())
        {
            list.Add(new StoredRawBlock(
                r.GetInt64(0), r.GetInt64(1), r.GetInt64(2), r.IsDBNull(3) ? null : r.GetInt64(3),
                r.GetInt32(4), r.GetInt32(5), Blob(r, 6), Blob(r, 7)));
        }
        return list;
    }

    public void AddPause(long startMs, long endMs, PauseReason reason)
    {
        using var cmd = Command("INSERT INTO pause(start_ms, end_ms, reason) VALUES($s, $e, $r)", startMs, endMs, (int)reason);
        cmd.ExecuteNonQuery();
    }

    // ---- Outils ----

    public void Exec(string sql)
    {
        using var cmd = _conn.CreateCommand();
        cmd.CommandText = sql;
        cmd.ExecuteNonQuery();
    }

    public object? Scalar(string sql)
    {
        using var cmd = _conn.CreateCommand();
        cmd.CommandText = sql;
        return cmd.ExecuteScalar();
    }

    /// <summary>Requête avec paramètres positionnels : chaque <c>$nom</c>, dans l'ordre d'apparition, reçoit la valeur suivante.</summary>
    internal SqliteCommand Command(string sql, params object?[] values)
    {
        var cmd = _conn.CreateCommand();
        cmd.CommandText = sql;
        int i = 0;
        var seen = new HashSet<string>(StringComparer.Ordinal);
        for (int p = sql.IndexOf('$', StringComparison.Ordinal); p >= 0; p = sql.IndexOf('$', p + 1))
        {
            int end = p + 1;
            while (end < sql.Length && (char.IsLetterOrDigit(sql[end]) || sql[end] == '_')) end++;
            string name = sql[p..end];
            if (!seen.Add(name)) continue;
            if (i >= values.Length) throw new ArgumentException("Paramètre manquant : " + name, nameof(values));
            cmd.Parameters.AddWithValue(name, values[i++] ?? DBNull.Value);
        }
        if (i != values.Length) throw new ArgumentException("Trop de paramètres.", nameof(values));
        return cmd;
    }

    internal static byte[] Blob(SqliteDataReader r, int i) => r.IsDBNull(i) ? [] : (byte[])r.GetValue(i);

    public void Dispose() => _conn.Dispose();
}
