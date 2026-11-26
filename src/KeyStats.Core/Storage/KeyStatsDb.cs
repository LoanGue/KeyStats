using KeyStats.Core.Analysis;
using KeyStats.Core.Text;
using Microsoft.Data.Sqlite;

namespace KeyStats.Core.Storage;

/// <summary>
/// Accès à <c>keystats.db</c> : ouverture, migrations, référentiels (avec cache) et chronologie.
/// Une instance par thread. Les écritures groupées passent par <see cref="BeginTransaction"/>.
/// </summary>
public sealed partial class KeyStatsDb : IDisposable
{
    private static readonly Lock s_initLock = new();
    private static bool s_initialized;

    private readonly SqliteConnection _conn;
    private readonly Dictionary<string, int> _apps = new(StringComparer.Ordinal);
    private readonly Dictionary<string, int> _layoutIds = new(StringComparer.Ordinal);
    private readonly Dictionary<int, LayoutTable> _layouts = [];
    private bool _inTransaction;

    /// <summary>
    /// Nombre de transactions annulées. Un objet qui garde en mémoire un id créé dans une transaction
    /// (époque de chiffrement par exemple) le compare pour savoir si cet id existe encore.
    /// </summary>
    public long RollbackCount { get; private set; }

    private KeyStatsDb(SqliteConnection conn) => _conn = conn;

    public SqliteConnection Connection => _conn;

    /// <summary>
    /// Ouvre (et crée ou met à jour) la base. Sans <paramref name="wal"/>, le journal est en mode DELETE :
    /// la base tient en un seul fichier (miroir de sauvegarde).
    /// </summary>
    public static KeyStatsDb Open(string path, bool readOnly = false, bool wal = true)
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
                db.Exec(wal ? "PRAGMA journal_mode = WAL" : "PRAGMA journal_mode = DELETE");
                // En journal DELETE, NORMAL peut corrompre la base si le courant est coupé pendant le COMMIT.
                db.Exec(wal ? "PRAGMA synchronous = NORMAL" : "PRAGMA synchronous = FULL");
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
