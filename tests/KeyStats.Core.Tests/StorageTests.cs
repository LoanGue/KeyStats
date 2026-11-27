using System.Text;
using KeyStats.Core.Analysis;
using KeyStats.Core.Crypto;
using KeyStats.Core.Input;
using KeyStats.Core.Storage;
using KeyStats.Core.Text;

namespace KeyStats.Core.Tests;

public sealed class StorageTests
{
    [Fact]
    public void Base_neuve_au_dernier_schema_et_reouverture_sans_migration()
    {
        using var tmp = new TempDb();
        Assert.Equal(Schema.CurrentVersion, tmp.Db.UserVersion);
        Assert.Equal(Schema.CurrentVersion, tmp.Reopen().UserVersion);
    }

    [Fact]
    public void Referentiels_dedoublonnes()
    {
        using var tmp = new TempDb();
        int app = tmp.Db.GetOrAddApp("Code.exe");

        Assert.Equal(app, tmp.Db.GetOrAddApp("code.exe"));
        Assert.Equal(app, tmp.Reopen().GetOrAddApp("CODE.EXE"));
        Assert.NotEqual(app, tmp.Db.GetOrAddApp("winword.exe"));
        Assert.Equal("code.exe", tmp.Db.GetApps()[app]);

        int layout = tmp.Db.GetOrAddLayout(LayoutTable.FrenchAzerty);
        Assert.Equal(layout, tmp.Db.GetOrAddLayout(LayoutTable.FrenchAzerty));
        var back = tmp.Reopen().GetLayout(layout)!;
        Assert.Equal(LayoutTable.FrenchAzerty.ComputeHash(), back.ComputeHash());
        Assert.Equal('@', back.Get(0x0B, (int)ShiftState.AltGr, out _));
    }

    [Fact]
    public void Brut_chiffre_ecrit_par_l_agent_et_relu_par_le_tableau_de_bord()
    {
        using var tmp = new TempDb();
        using var master = MasterKey.Generate();
        int app = tmp.Db.GetOrAddApp("notepad.exe");
        var first = new Typist().Type("premier bloc").ToArray();
        var second = new Typist(Typist.Start + 90_000).Type("second bloc").ToArray();

        long focusId;
        using (var writer = new RawArchiveWriter(tmp.Db, master.PublicKey, new TitleHasher(TitleHasher.NewKey())))
        {
            long title = writer.GetOrAddTitle(app, "notepad.exe", "Sans titre - Bloc-notes", Typist.Start);
            focusId = tmp.Db.AddFocus(Typist.Start, app, title, null, FocusState.NewWindow);
            writer.WriteBlock(first, focusId, Typist.Start);
            writer.WriteBlock(second, focusId, Typist.Start + 90_000);
        }

        tmp.Reopen();
        using var reader = new RawArchiveReader(tmp.Db, master);
        var blocks = reader.ReadBlocks(Typist.Start - 1, Typist.Start + 3_600_000).ToList();

        Assert.Equal(2, blocks.Count);
        Assert.Equal(first, blocks[0].Events);
        Assert.Equal(second, blocks[1].Events);
        Assert.Equal(focusId, blocks[0].Block.FocusId);
        var focus = Assert.Single(tmp.Db.GetFocus(Typist.Start, Typist.Start + 1));
        Assert.Equal("Sans titre - Bloc-notes", reader.ReadTitle(focus.TitleId!.Value));
    }

    [Fact]
    public void Ni_texte_ni_titre_en_clair_dans_les_fichiers_de_la_base()
    {
        using var tmp = new TempDb();
        using var master = MasterKey.Generate();
        int app = tmp.Db.GetOrAddApp("keepass.exe");
        const string secret = "MotDePasseTresSecret";
        var events = new Typist().Type(secret.ToLowerInvariant()).ToArray();
        using (var writer = new RawArchiveWriter(tmp.Db, master.PublicKey, new TitleHasher(TitleHasher.NewKey())))
        {
            writer.GetOrAddTitle(app, "keepass.exe", secret, Typist.Start);
            writer.WriteBlock(events, null, Typist.Start);
        }

        byte[] all = tmp.ReadAllBytesClosed();

        Assert.True(all.Length > 0);
        Assert.Equal(-1, all.AsSpan().IndexOf(Encoding.UTF8.GetBytes(secret)));
        Assert.Equal(-1, all.AsSpan().IndexOf(Encoding.Unicode.GetBytes(secret)));
        Assert.Equal(-1, all.AsSpan().IndexOf(Encoding.UTF8.GetBytes(secret.ToLowerInvariant())));
    }

    [Fact]
    public void Titre_deja_vu_stocke_une_seule_fois()
    {
        using var tmp = new TempDb();
        using var master = MasterKey.Generate();
        int code = tmp.Db.GetOrAddApp("code.exe"), word = tmp.Db.GetOrAddApp("winword.exe");
        using var writer = new RawArchiveWriter(tmp.Db, master.PublicKey, new TitleHasher(TitleHasher.NewKey()));

        long a = writer.GetOrAddTitle(code, "code.exe", "main.cs - KeyStats", Typist.Start);
        long b = writer.GetOrAddTitle(code, "code.exe", "main.cs - KeyStats", Typist.Start + 7_200_000);
        long c = writer.GetOrAddTitle(word, "winword.exe", "main.cs - KeyStats", Typist.Start);

        Assert.Equal(a, b);
        Assert.NotEqual(a, c);
        Assert.Equal(2, tmp.Db.CountTitles());
    }

    [Fact]
    public void Nouvelle_epoque_chaque_heure_et_toutes_restent_lisibles()
    {
        using var tmp = new TempDb();
        using var master = MasterKey.Generate();
        var events = new Typist().Type("abc").ToArray();
        using (var writer = new RawArchiveWriter(tmp.Db, master.PublicKey, new TitleHasher(TitleHasher.NewKey())))
        {
            writer.WriteBlock(events, null, Typist.Start);
            writer.WriteBlock(events, null, Typist.Start + 1000);
            writer.WriteBlock(events, null, Typist.Start + RawArchiveWriter.EpochLifetimeMs);
            writer.DropEpoch();
            writer.WriteBlock(events, null, Typist.Start + RawArchiveWriter.EpochLifetimeMs + 1000);
        }

        Assert.Equal(3, tmp.Count("SELECT count(*) FROM key_epoch"));
        using var reader = new RawArchiveReader(tmp.Db, master);
        var blocks = reader.ReadBlocks(0, long.MaxValue).ToList();
        Assert.Equal(4, blocks.Count);
        Assert.All(blocks, b => Assert.Equal(events, b.Events));
    }

    [Fact]
    public void Brut_illisible_avec_une_autre_cle_maitresse()
    {
        using var tmp = new TempDb();
        using var master = MasterKey.Generate();
        using var intruder = MasterKey.Generate();
        using (var writer = new RawArchiveWriter(tmp.Db, master.PublicKey, new TitleHasher(TitleHasher.NewKey())))
            writer.WriteBlock(new Typist().Type("abc").ToArray(), null, Typist.Start);

        using var reader = new RawArchiveReader(tmp.Db, intruder);
        Assert.ThrowsAny<System.Security.Cryptography.CryptographicException>(() => reader.ReadBlocks(0, long.MaxValue).ToList());
    }

    [Fact]
    public void Statistiques_additionnees_a_chaque_enregistrement()
    {
        using var tmp = new TempDb();
        var writer = new StatsWriter(tmp.Db);
        var events = new Typist().Type("le chqt").Backspace(2).Type("at dort sur le canapé du salon depuis ce matin").ToArray();

        for (int i = 0; i < 2; i++)
        {
            var stats = Typist.Analyze(events);
            using var tx = tmp.Db.BeginTransaction();
            writer.Write(stats);
            tx.Commit();
        }

        int day = new LocalDayClock(TimeZoneInfo.Utc).DayKey(Typist.Start);
        Assert.Equal(2, tmp.Count("SELECT count(*) FROM session"));
        Assert.Equal(4, tmp.Count($"SELECT sum(presses) FROM key_minute WHERE key = {KeyCode.Backspace}"));
        Assert.Equal(2, tmp.Count($"SELECT value FROM day_metric WHERE day = {day} AND metric = {(int)DayMetric.Typos}"));
        Assert.Equal(2, tmp.Count($"SELECT n FROM day_error_pair WHERE typed = {(int)'q'} AND wanted = {(int)'a'}"));
        Assert.Equal(6, tmp.Count($"SELECT sum(n) FROM day_char WHERE day = {day} AND ch = {(int)'c'}"));
        Assert.Equal(2, tmp.Count($"SELECT errors FROM day_bigram WHERE a = {(int)'h'} AND b = {(int)'a'}"));
    }

    [Fact]
    public void Longueurs_de_mots_rangees_sous_la_nature_de_la_session()
    {
        using var tmp = new TempDb();
        var stats = Typist.Analyze(new Typist().Type("le chat dort sur le canapé du salon depuis ce matin ").ToArray());
        new StatsWriter(tmp.Db).Write(stats);

        long kind = tmp.Count("SELECT kind FROM session");
        Assert.Equal(11, tmp.Count($"SELECT sum(n) FROM day_wordlen WHERE kind = {kind}"));
        Assert.Equal(4, tmp.Count($"SELECT n FROM day_wordlen WHERE kind = {kind} AND len = 2"));
        Assert.Equal(0, tmp.Count($"SELECT count(*) FROM day_wordlen WHERE kind <> {kind}"));
    }

    [Fact]
    public void Migration_2_garde_les_longueurs_de_mots_sans_nature()
    {
        using var tmp = new TempDb();
        tmp.Db.Exec("DROP TABLE day_wordlen");
        tmp.Db.Exec("CREATE TABLE day_wordlen (day INTEGER NOT NULL, len INTEGER NOT NULL, n INTEGER NOT NULL, PRIMARY KEY (day, len)) WITHOUT ROWID");
        tmp.Db.Exec("INSERT INTO day_wordlen VALUES (20261102, 4, 7)");
        tmp.Db.Exec("PRAGMA user_version = 1");

        Assert.Equal(Schema.CurrentVersion, tmp.Reopen().UserVersion);
        Assert.Equal(7, tmp.Count("SELECT n FROM day_wordlen WHERE day = 20261102 AND kind = 0 AND len = 4"));
    }

    [Fact]
    public void Histogrammes_fusionnes_et_non_ecrases()
    {
        using var tmp = new TempDb();
        var writer = new StatsWriter(tmp.Db);
        int day = 20261102;
        for (int i = 0; i < 3; i++)
        {
            var stats = new StatsAccumulator(new LocalDayClock(TimeZoneInfo.Utc));
            stats.AddHistogram(day, HistogramKind.Chord, 0, 2, 5);
            stats.AddBigram(day, 'e', 's', 120);
            writer.Write(stats);
        }

        using var cmd = tmp.Db.Command("SELECT data FROM day_hist WHERE day = $d AND hist = $h", day, (int)HistogramKind.Chord);
        var data = (byte[])cmd.ExecuteScalar()!;
        Assert.Equal(15, HistogramCodec.Decode(data, StatsAccumulator.ChordBuckets)[2]);

        using var lat = tmp.Db.Command("SELECT lat FROM day_bigram WHERE day = $d", day);
        var counts = HistogramCodec.Decode((byte[])lat.ExecuteScalar()!, LogScale.Buckets);
        Assert.Equal(3, counts.Sum());
    }

    [Fact]
    public void Recalcul_efface_la_periode_mais_garde_les_touches_des_applis_exclues()
    {
        using var tmp = new TempDb();
        var writer = new StatsWriter(tmp.Db);
        var stats = Typist.Analyze(new Typist().Type("le chat dort sur le canapé du salon depuis ce matin").ToArray());
        stats.AddExcludedKey(Typist.Start);
        stats.AddExcludedKey(Typist.Start + 1);
        writer.Write(stats);
        var clock = new LocalDayClock(TimeZoneInfo.Utc);
        int day = clock.DayKey(Typist.Start);

        var (fromMs, toMs) = writer.DeleteDays(day, day, clock);

        Assert.True(fromMs <= Typist.Start && Typist.Start < toMs);
        Assert.Equal(86_400_000, toMs - fromMs);

        Assert.Equal(0, tmp.Count("SELECT count(*) FROM session"));
        Assert.Equal(0, tmp.Count("SELECT count(*) FROM key_minute"));
        Assert.Equal(0, tmp.Count("SELECT count(*) FROM day_char"));
        Assert.Equal(0, tmp.Count("SELECT count(*) FROM day_bigram"));
        Assert.Equal(2, tmp.Count("SELECT sum(excluded_keys) FROM minute_total"));
        Assert.Equal(0, tmp.Count("SELECT sum(keys) FROM minute_total"));
        Assert.Equal(2, tmp.Count($"SELECT value FROM day_metric WHERE metric = {(int)DayMetric.ExcludedKeys}"));
        Assert.Equal(1, tmp.Count("SELECT count(*) FROM day_metric"));
    }

    [Fact]
    public void Transaction_non_validee_annulee()
    {
        using var tmp = new TempDb();
        using (tmp.Db.BeginTransaction()) tmp.Db.GetOrAddDevice([1, 2, 3], "Clavier", 1);
        Assert.Equal(0, tmp.Count("SELECT count(*) FROM device"));
    }

    [Fact]
    public void Ids_crees_dans_une_transaction_annulee_ne_sont_pas_reutilises()
    {
        using var tmp = new TempDb();
        using var master = MasterKey.Generate();
        var events = new Typist().Type("abc").ToArray();
        using var writer = new RawArchiveWriter(tmp.Db, master.PublicKey, new TitleHasher(TitleHasher.NewKey()));

        using (tmp.Db.BeginTransaction())
        {
            tmp.Db.GetOrAddApp("notepad.exe");
            writer.WriteBlock(events, null, Typist.Start);
        }

        using (var tx = tmp.Db.BeginTransaction())
        {
            int app = tmp.Db.GetOrAddApp("notepad.exe");
            writer.GetOrAddTitle(app, "notepad.exe", "Sans titre", Typist.Start + 1000);
            writer.WriteBlock(events, null, Typist.Start + 1000);
            tx.Commit();
        }

        Assert.Equal(1, tmp.Count("SELECT count(*) FROM key_epoch"));
        Assert.Equal(1, tmp.Count("SELECT count(*) FROM app"));
        using var reader = new RawArchiveReader(tmp.Db, master);
        Assert.Equal(events, Assert.Single(reader.ReadBlocks(0, long.MaxValue)).Events);
    }

    [Fact]
    public void Transaction_deja_annulee_par_sqlite_vide_les_caches_sans_masquer_l_erreur()
    {
        using var tmp = new TempDb();
        long before = tmp.Db.RollbackCount;

        void FailAfterSqliteRollback()
        {
            using (tmp.Db.BeginTransaction())
            {
                tmp.Db.GetOrAddApp("notepad.exe");
                tmp.Db.Exec("ROLLBACK"); // comme SQLite après SQLITE_FULL ou IOERR
                throw new IOException("erreur d'origine");
            }
        }

        var original = Assert.Throws<IOException>(FailAfterSqliteRollback);

        Assert.Equal("erreur d'origine", original.Message);
        Assert.Equal(before + 1, tmp.Db.RollbackCount);
        Assert.False(tmp.Db.IsFaulted);
        Assert.Equal(0, tmp.Db.LastRollbackErrorCode);
        using (var tx = tmp.Db.BeginTransaction())
        {
            tmp.Db.GetOrAddApp("notepad.exe");
            tx.Commit();
        }
        Assert.Equal(1, tmp.Count("SELECT count(*) FROM app"));
    }

    [Fact]
    public void Commit_en_echec_annule_la_transaction_et_vide_les_caches()
    {
        using var tmp = new TempDb();
        long before = tmp.Db.RollbackCount;

        using (var tx = tmp.Db.BeginTransaction())
        {
            tmp.Db.GetOrAddApp("notepad.exe");
            tmp.Db.Exec("PRAGMA defer_foreign_keys = ON");
            tmp.Db.Exec("INSERT INTO focus (ts_ms, app_id) VALUES (1, 999)");
            Assert.ThrowsAny<Microsoft.Data.Sqlite.SqliteException>(tx.Commit);
        }

        Assert.Equal(before + 1, tmp.Db.RollbackCount);
        Assert.Equal(0, tmp.Count("SELECT count(*) FROM app"));
        using (var tx = tmp.Db.BeginTransaction())
        {
            tmp.Db.GetOrAddApp("notepad.exe");
            tx.Commit();
        }
        Assert.Equal(1, tmp.Count("SELECT count(*) FROM app"));
    }

    [Fact]
    public void Pauses_et_meta()
    {
        using var tmp = new TempDb();
        tmp.Db.AddPause(10, 20, PauseReason.Snooze);
        tmp.Db.SetMeta("master_pub", [1, 2, 3]);

        tmp.Reopen();
        Assert.Equal(1, tmp.Count($"SELECT count(*) FROM pause WHERE reason = {(int)PauseReason.Snooze}"));
        Assert.Equal([1, 2, 3], tmp.Db.GetMeta("master_pub"));
        Assert.Null(tmp.Db.GetMeta("absent"));
    }
}
