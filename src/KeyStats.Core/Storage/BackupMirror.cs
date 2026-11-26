namespace KeyStats.Core.Storage;

/// <summary>Bilan d'une sauvegarde : des compteurs seulement.</summary>
public readonly record struct BackupResult(long Appended, long Replaced, long DurationMs);

/// <summary>
/// Copie miroir de <c>keystats.db</c>, mise à jour en une transaction sur un instantané cohérent de
/// la source, qui n'est que lue : l'agent continue d'y écrire pendant ce temps. Le miroir a le même
/// schéma ; son journal est en mode DELETE (jamais WAL) pour qu'il tienne en un seul fichier, copiable tel quel.
/// Le brut et les titres y restent chiffrés comme dans la source.
/// </summary>
public static class BackupMirror
{
    /// <summary>Tables où une ligne n'est jamais modifiée ni supprimée : copiées à partir du dernier id.</summary>
    internal static readonly string[] AppendOnly = ["key_epoch", "window_title", "focus", "raw_block"];

    /// <summary>
    /// Tables petites, ou réécrites par les cumuls et le recalcul : remplacées entières. Appareils et
    /// dispositions aussi : d'une base recréée à l'autre, leurs lignes peuvent être identiques à des ids différents.
    /// </summary>
    internal static readonly string[] Replaced =
    [
        "meta", "app", "device", "layout", "pause", "key_store", "session",
        "key_minute", "minute_total", "app_hour",
        "day_char", "day_bigram", "day_wordlen", "day_shortcut", "day_error_pair", "day_metric", "day_hist",
    ];

    /// <summary>Crée ou met à jour le miroir <paramref name="backupPath"/> de <paramref name="sourcePath"/>.</summary>
    public static BackupResult Run(string sourcePath, string backupPath, long nowMs)
    {
        // ATTACH créerait une base vide à la place d'une source absente.
        if (!File.Exists(sourcePath)) throw new FileNotFoundException("Base source absente.");
        if (string.Equals(Path.GetFullPath(sourcePath), Path.GetFullPath(backupPath), StringComparison.OrdinalIgnoreCase))
            throw new ArgumentException("Le miroir ne peut pas être la base elle-même.", nameof(backupPath));
        long started = Environment.TickCount64;
        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(backupPath))!);
        using var db = KeyStatsDb.Open(backupPath, wal: false);
        // Les tables sont remplacées l'une après l'autre : les références sont vérifiées dans la source.
        db.Exec("PRAGMA foreign_keys = OFF");
        using (var attach = db.Command("ATTACH $p AS src", sourcePath)) attach.ExecuteNonQuery();
        try
        {
            if ((long)db.Scalar("PRAGMA src.user_version")! != db.UserVersion)
                throw new InvalidOperationException("Schéma de la source différent de celui du miroir.");
            long appended = 0, replaced = 0;
            // Différée, pas IMMEDIATE : celle-ci prendrait aussi le verrou d'écriture de la source attachée
            // et bloquerait l'agent. Ainsi seul le miroir est verrouillé ; la source est lue sur un
            // instantané (WAL) qui tient jusqu'à la fin de la transaction.
            using (var tx = db.BeginTransaction(deferred: true))
            {
                foreach (string t in AppendOnly) appended += Append(db, t);
                foreach (string t in Replaced) replaced += Replace(db, t);
                db.SetMeta(MetaKeys.BackupMs, BitConverter.GetBytes(nowMs));
                tx.Commit();
            }
            return new BackupResult(appended, replaced, Environment.TickCount64 - started);
        }
        finally
        {
            db.Exec("DETACH src");
        }
    }

    /// <summary>Heure de la dernière sauvegarde réussie dans ce miroir (ms UTC), nulle sans miroir.</summary>
    public static long? LastRunMs(string backupPath)
    {
        if (!File.Exists(backupPath)) return null;
        using var db = KeyStatsDb.Open(backupPath, readOnly: true);
        if (db.Scalar("SELECT count(*) FROM sqlite_master WHERE name = 'meta'") is not 1L) return null;
        return db.GetMeta(MetaKeys.BackupMs) is { Length: 8 } v ? BitConverter.ToInt64(v) : null;
    }

    /// <summary>
    /// Lignes de la source au-delà du dernier id du miroir. Si la ligne de ce dernier id diffère dans la
    /// source (base recréée depuis), le miroir de la table est d'abord vidé : ses ids ne désignent plus
    /// les mêmes lignes.
    /// </summary>
    private static long Append(KeyStatsDb db, string table)
    {
        long last = (long)db.Scalar($"SELECT coalesce(max(id), 0) FROM main.{table}")!;
        using (var check = db.Command($"SELECT count(*) FROM (SELECT * FROM main.{table} WHERE id = $l EXCEPT SELECT * FROM src.{table} WHERE id = $l)", last))
        {
            if ((long)check.ExecuteScalar()! != 0)
            {
                db.Exec($"DELETE FROM main.{table}");
                last = 0;
            }
        }
        using var cmd = db.Command($"INSERT INTO main.{table} SELECT * FROM src.{table} WHERE id > $last", last);
        return cmd.ExecuteNonQuery();
    }

    private static long Replace(KeyStatsDb db, string table)
    {
        db.Exec($"DELETE FROM main.{table}");
        using var cmd = db.Command($"INSERT INTO main.{table} SELECT * FROM src.{table}");
        return cmd.ExecuteNonQuery();
    }
}
