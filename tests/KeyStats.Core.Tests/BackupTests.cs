using KeyStats.Core.Storage;

namespace KeyStats.Core.Tests;

public sealed class BackupTests
{
    private static string MirrorOf(TempDb tmp) => Path.Combine(Path.GetDirectoryName(tmp.DbPath)!, "backup", "keystats.db");

    private static void Fill(KeyStatsDb db, int from, int to)
    {
        for (int i = from; i <= to; i++)
        {
            db.Exec($"INSERT INTO key_epoch(id, created_ms, ephemeral_pub) VALUES({i}, {i * 1000}, x'0102')");
            db.Exec($"INSERT INTO focus(ts_ms, app_id) VALUES({i * 1000}, {db.GetOrAddApp($"app{i}.exe")})");
            db.Exec($"INSERT INTO minute_total VALUES({i}, {i}, 0, 0, 0, 0, 0)");
            db.AddPause(i * 10, i * 10 + 5, PauseReason.Snooze);
        }
        db.SetMeta("master_pub", [9, 9]);
    }

    /// <summary>Lignes qui diffèrent entre la source et le miroir, dans un sens ou dans l'autre, toutes tables confondues.</summary>
    private static long Differences(TempDb tmp, string mirror)
    {
        using (var attach = tmp.Db.Command("ATTACH $p AS b", mirror)) attach.ExecuteNonQuery();
        try
        {
            long n = 0;
            foreach (string t in BackupMirror.AppendOnly.Concat(BackupMirror.Replaced))
            {
                // Seul le miroir note l'heure de sa sauvegarde.
                string only = t == "meta" ? $" WHERE key <> '{MetaKeys.BackupMs}'" : "";
                n += (long)tmp.Db.Scalar(
                    $"SELECT (SELECT count(*) FROM (SELECT * FROM main.{t} EXCEPT SELECT * FROM b.{t}{only})) + " +
                    $"(SELECT count(*) FROM (SELECT * FROM b.{t}{only} EXCEPT SELECT * FROM main.{t}))")!;
            }
            return n;
        }
        finally
        {
            tmp.Db.Exec("DETACH b");
        }
    }

    [Fact]
    public void Chaque_table_du_schema_est_sauvegardee_une_fois()
    {
        using var tmp = new TempDb();
        var tables = new List<string>();
        using (var cmd = tmp.Db.Command("SELECT name FROM sqlite_master WHERE type = 'table'"))
        using (var r = cmd.ExecuteReader())
            while (r.Read()) tables.Add(r.GetString(0));
        var listed = BackupMirror.AppendOnly.Concat(BackupMirror.Replaced).ToList();
        Assert.Equal(listed.Count, listed.Distinct().Count());
        Assert.Equal(tables.Order(), listed.Order());
    }

    [Fact]
    public void Miroir_identique_puis_mis_a_jour_par_increments()
    {
        using var tmp = new TempDb();
        string mirror = MirrorOf(tmp);
        Fill(tmp.Db, 1, 3);
        var first = BackupMirror.Run(tmp.DbPath, mirror, 1000);
        Assert.Equal(6, first.Appended);
        Assert.Equal(0, Differences(tmp, mirror));

        // Ajouts, cumul sur une minute existante, pause supprimée.
        Fill(tmp.Db, 4, 5);
        tmp.Db.Exec("UPDATE minute_total SET keys = 42 WHERE ts_min = 1");
        tmp.Db.Exec("DELETE FROM pause WHERE start_ms = 20");
        var second = BackupMirror.Run(tmp.DbPath, mirror, 2000);
        Assert.Equal(4, second.Appended);
        Assert.Equal(0, Differences(tmp, mirror));
        Assert.Equal(2000, BackupMirror.LastRunMs(mirror));
    }

    [Fact]
    public void Miroir_en_un_seul_fichier()
    {
        using var tmp = new TempDb();
        string mirror = MirrorOf(tmp);
        Fill(tmp.Db, 1, 2);
        BackupMirror.Run(tmp.DbPath, mirror, 1000);
        Assert.Equal(["keystats.db"], Directory.GetFiles(Path.GetDirectoryName(mirror)!).Select(Path.GetFileName));
        using var db = KeyStatsDb.Open(mirror, readOnly: true);
        Assert.Equal("delete", db.Scalar("PRAGMA journal_mode"));
    }

    [Fact]
    public void Source_recreee_le_miroir_repart_de_zero()
    {
        using var tmp = new TempDb();
        string mirror = MirrorOf(tmp);
        Fill(tmp.Db, 1, 2);
        BackupMirror.Run(tmp.DbPath, mirror, 1000);

        // Même ids, autres lignes, et plus nombreuses : le dernier id du miroir ne désigne plus la même époque.
        tmp.Db.Exec("DELETE FROM focus; DELETE FROM key_epoch");
        for (int i = 1; i <= 4; i++) tmp.Db.Exec($"INSERT INTO key_epoch(id, created_ms, ephemeral_pub) VALUES({i}, {i * 7}, x'03')");
        BackupMirror.Run(tmp.DbPath, mirror, 2000);
        Assert.Equal(0, Differences(tmp, mirror));
    }

    private static long MirrorCount(string mirror, string table)
    {
        using var db = KeyStatsDb.Open(mirror, readOnly: true);
        return (long)db.Scalar($"SELECT count(*) FROM {table}")!;
    }

    [Fact]
    public void Ecriture_en_cours_dans_la_source_ni_bloquee_ni_copiee()
    {
        using var tmp = new TempDb();
        string mirror = MirrorOf(tmp);
        Fill(tmp.Db, 1, 2);
        // L'agent tient une transaction en écriture : la sauvegarde ne l'attend pas et ne voit pas sa ligne.
        using (var tx = tmp.Db.BeginTransaction())
        {
            tmp.Db.Exec("INSERT INTO key_epoch(id, created_ms, ephemeral_pub) VALUES(99, 1, x'01')");
            var sw = System.Diagnostics.Stopwatch.StartNew();
            BackupMirror.Run(tmp.DbPath, mirror, 1000);
            Assert.True(sw.ElapsedMilliseconds < 2000, $"{sw.ElapsedMilliseconds} ms");
            tmp.Db.Exec("INSERT INTO key_epoch(id, created_ms, ephemeral_pub) VALUES(100, 1, x'01')");
            tx.Commit();
        }
        Assert.Equal(2, MirrorCount(mirror, "key_epoch"));
    }

    [Fact]
    public void Schema_different_refuse_sans_toucher_au_miroir()
    {
        using var tmp = new TempDb();
        string mirror = MirrorOf(tmp);
        Fill(tmp.Db, 1, 2);
        BackupMirror.Run(tmp.DbPath, mirror, 1000);
        Fill(tmp.Db, 3, 3);
        tmp.Db.Exec($"PRAGMA user_version = {Schema.CurrentVersion + 1}");
        Assert.Throws<InvalidOperationException>(() => BackupMirror.Run(tmp.DbPath, mirror, 2000));
        tmp.Db.Exec($"PRAGMA user_version = {Schema.CurrentVersion}");
        Assert.Equal(1000, BackupMirror.LastRunMs(mirror));
        Assert.Equal(2, MirrorCount(mirror, "key_epoch"));
    }

    [Fact]
    public void Echec_en_cours_de_copie_laisse_le_miroir_precedent()
    {
        using var tmp = new TempDb();
        string mirror = MirrorOf(tmp);
        Fill(tmp.Db, 1, 2);
        BackupMirror.Run(tmp.DbPath, mirror, 1000);
        // Les ajouts passent, puis la dernière table remplacée n'a plus les mêmes colonnes.
        Fill(tmp.Db, 3, 4);
        tmp.Db.Exec("ALTER TABLE day_hist ADD COLUMN extra INTEGER");
        Assert.ThrowsAny<Microsoft.Data.Sqlite.SqliteException>(() => BackupMirror.Run(tmp.DbPath, mirror, 2000));
        Assert.Equal(1000, BackupMirror.LastRunMs(mirror));
        Assert.Equal(2, MirrorCount(mirror, "key_epoch"));
        Assert.Equal(2, MirrorCount(mirror, "pause"));
    }

    [Fact]
    public void Source_absente_ou_miroir_sur_la_source_refuses()
    {
        using var tmp = new TempDb();
        string mirror = MirrorOf(tmp);
        Assert.Throws<FileNotFoundException>(() => BackupMirror.Run(tmp.DbPath + ".absent", mirror, 1));
        Assert.False(Directory.Exists(Path.GetDirectoryName(mirror)));
        Assert.Throws<ArgumentException>(() => BackupMirror.Run(tmp.DbPath, tmp.DbPath.ToUpperInvariant(), 1));
        Assert.Null(BackupMirror.LastRunMs(mirror));
    }

    [Fact]
    public void Chemin_du_miroir_dans_le_dossier_choisi_ou_a_cote_de_la_base()
    {
        string db = Path.Combine(Path.GetTempPath(), "ks", "keystats.db");
        Assert.Equal(Path.Combine(Path.GetTempPath(), "ks", "backup", "keystats.db"), BackupMirror.PathFor(db, null));
        string folder = Path.Combine(Path.GetTempPath(), "ailleurs");
        Assert.Equal(Path.Combine(folder, "keystats.db"), BackupMirror.PathFor(db, folder));
    }

    private static readonly TimeZoneInfo Plus1 = TimeZoneInfo.CreateCustomTimeZone("UTC+1", TimeSpan.FromHours(1), "UTC+1", "UTC+1");

    private static long Utc(int d, int h, int mi = 0) => new DateTimeOffset(2026, 11, d, h, mi, 0, TimeSpan.Zero).ToUnixTimeMilliseconds();

    [Theory]
    // 3 h locales le 26 = 2 h UTC ; jour 0 : aucune sauvegarde.
    [InlineData(0, 0, 26, 10, true)]
    [InlineData(26, 2, 26, 10, false)]
    [InlineData(26, 1, 26, 10, true)]
    [InlineData(25, 23, 26, 1, false)]
    [InlineData(25, 1, 26, 1, true)]
    [InlineData(26, 12, 26, 10, true)]
    public void Sauvegarde_due_apres_3_h_locales(int lastDay, int lastHour, int nowDay, int nowHour, bool due)
    {
        long? last = lastDay == 0 ? null : Utc(lastDay, lastHour);
        Assert.Equal(due, BackupSchedule.IsDue(last, Utc(nowDay, nowHour), Plus1));
    }

    [Fact]
    public void Heure_sautee_le_creneau_passe_a_l_heure_suivante()
    {
        // Heure d'été à 3 h : le 29 mars 2026, 3 h n'existe pas, 4 h locales = 2 h UTC.
        var start = TimeZoneInfo.TransitionTime.CreateFloatingDateRule(new DateTime(1, 1, 1, 3, 0, 0), 3, 5, DayOfWeek.Sunday);
        var end = TimeZoneInfo.TransitionTime.CreateFloatingDateRule(new DateTime(1, 1, 1, 3, 0, 0), 10, 5, DayOfWeek.Sunday);
        var rule = TimeZoneInfo.AdjustmentRule.CreateAdjustmentRule(DateTime.MinValue.Date, DateTime.MaxValue.Date, TimeSpan.FromHours(1), start, end);
        var zone = TimeZoneInfo.CreateCustomTimeZone("Test/3h", TimeSpan.FromHours(1), "3h", "3h", "3h+1", [rule]);
        long now = new DateTimeOffset(2026, 3, 29, 12, 0, 0, TimeSpan.Zero).ToUnixTimeMilliseconds();
        Assert.Equal(new DateTimeOffset(2026, 3, 29, 2, 0, 0, TimeSpan.Zero).ToUnixTimeMilliseconds(), BackupSchedule.LatestSlotMs(now, zone));
    }
}
