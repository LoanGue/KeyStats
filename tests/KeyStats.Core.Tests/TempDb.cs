using KeyStats.Core.Storage;

namespace KeyStats.Core.Tests;

/// <summary>Base SQLite dans un dossier temporaire, supprimée à la fin du test.</summary>
internal sealed class TempDb : IDisposable
{
    private readonly string _dir;

    public TempDb()
    {
        _dir = Path.Combine(Path.GetTempPath(), "keystats-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_dir);
        DbPath = Path.Combine(_dir, "keystats.db");
        Db = KeyStatsDb.Open(DbPath);
    }

    public string DbPath { get; }

    public KeyStatsDb Db { get; private set; }

    /// <summary>Ferme et rouvre la base (vérifie ce qui a vraiment été écrit).</summary>
    public KeyStatsDb Reopen()
    {
        Db.Dispose();
        Db = KeyStatsDb.Open(DbPath);
        return Db;
    }

    /// <summary>Tous les octets de la base et de son journal, base fermée.</summary>
    public byte[] ReadAllBytesClosed()
    {
        Db.Dispose();
        var bytes = new List<byte>();
        foreach (string f in Directory.GetFiles(_dir)) bytes.AddRange(File.ReadAllBytes(f));
        Db = KeyStatsDb.Open(DbPath);
        return [.. bytes];
    }

    public long Count(string sql) => (long)Db.Scalar(sql)!;

    public void Dispose()
    {
        Db.Dispose();
        try
        {
            Directory.Delete(_dir, recursive: true);
        }
        catch (IOException)
        {
            // Fichier encore verrouillé par l'antivirus : le dossier temporaire sera nettoyé plus tard.
        }
    }
}
