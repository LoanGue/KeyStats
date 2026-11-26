using Microsoft.Data.Sqlite;

namespace KeyStats.Core.Storage;

/// <summary>Transactions en écriture et reprise après une annulation.</summary>
public sealed partial class KeyStatsDb
{
    // ---- Transactions ----

    /// <summary>
    /// Transaction en écriture (BEGIN IMMEDIATE), annulée si elle n'est pas validée. Différée, elle ne prend
    /// le verrou d'écriture qu'à la première écriture, et seulement sur la base écrite : une base attachée
    /// qui n'est que lue n'est pas verrouillée.
    /// </summary>
    public DbTransaction BeginTransaction(bool deferred = false)
    {
        if (_inTransaction) throw new InvalidOperationException("Transaction déjà ouverte.");
        if (IsFaulted) throw new InvalidOperationException("Annulation impossible lors de la dernière transaction : base à rouvrir.");
        Exec(deferred ? "BEGIN DEFERRED" : "BEGIN IMMEDIATE");
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
            try
            {
                db.Exec("COMMIT");
            }
            catch
            {
                db.RollbackQuietly();
                throw;
            }
        }

        public void Dispose()
        {
            if (_db is null) return;
            var db = _db;
            _db = null;
            db._inTransaction = false;
            db.RollbackQuietly();
        }
    }

    /// <summary>
    /// Vrai si un <c>ROLLBACK</c> a échoué et que la connexion est restée dans une transaction : l'état
    /// de la base est inconnu, toute nouvelle transaction est refusée. L'appelant ferme et rouvre la base.
    /// </summary>
    public bool IsFaulted { get; private set; }

    /// <summary>Code d'erreur SQLite du dernier <c>ROLLBACK</c> en échec (0 sinon), pour le journal de diagnostic.</summary>
    public int LastRollbackErrorCode { get; private set; }

    /// <summary>
    /// Annule la transaction en cours si SQLite ne l'a pas déjà fait (SQLITE_FULL, IOERR, BUSY l'annulent
    /// d'office), sans masquer l'erreur d'origine. Les caches sont vidés dans tous les cas. Un échec de
    /// l'annulation est noté (<see cref="IsFaulted"/>, <see cref="LastRollbackErrorCode"/>) au lieu d'être levé.
    /// </summary>
    private void RollbackQuietly()
    {
        try
        {
            if (InTransactionNative) Exec("ROLLBACK");
        }
        catch (SqliteException e)
        {
            LastRollbackErrorCode = e.SqliteErrorCode;
            IsFaulted = InTransactionNative;
        }
        finally
        {
            OnRollback();
        }
    }

    private bool InTransactionNative => SQLitePCL.raw.sqlite3_get_autocommit(_conn.Handle) == 0;

    /// <summary>Les ids mis en cache pendant la transaction annulée n'existent plus.</summary>
    private void OnRollback()
    {
        RollbackCount++;
        _apps.Clear();
        _layoutIds.Clear();
        _layouts.Clear();
    }
}
