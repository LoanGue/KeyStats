namespace KeyStats.Core.Storage;

/// <summary>
/// Schéma de <c>keystats.db</c>. Une migration par version (<c>PRAGMA user_version</c>), jamais
/// modifiée une fois publiée : on en ajoute une nouvelle.
/// Horodatages en ms UTC ; minutes et heures UTC (ms / 60 000, ms / 3 600 000) ; jours locaux aaaammjj.
/// </summary>
public static class Schema
{
    public const int CurrentVersion = 2;

    internal static readonly string[] Migrations =
    [
        // Version 1
        """
        CREATE TABLE meta (
            key   TEXT PRIMARY KEY,
            value BLOB
        );

        -- Référentiels
        CREATE TABLE app (
            id       INTEGER PRIMARY KEY,
            exe      TEXT NOT NULL UNIQUE,      -- nom de l'exécutable en minuscules (winword.exe)
            excluded INTEGER NOT NULL DEFAULT 0
        );
        CREATE TABLE key_epoch (
            id            INTEGER PRIMARY KEY,
            created_ms    INTEGER NOT NULL,
            ephemeral_pub BLOB NOT NULL         -- clé publique jetable (SPKI) : avec la clé privée maîtresse, redonne la clé AES
        );
        CREATE TABLE window_title (
            id         INTEGER PRIMARY KEY,
            app_id     INTEGER NOT NULL REFERENCES app(id),
            title_hash BLOB NOT NULL UNIQUE,    -- HMAC local : reconnaît un titre sans l'écrire en clair
            epoch_id   INTEGER NOT NULL REFERENCES key_epoch(id),
            nonce      BLOB NOT NULL,
            title_enc  BLOB NOT NULL
        );
        CREATE TABLE device (
            id      INTEGER PRIMARY KEY,
            label   TEXT NOT NULL,
            kind    INTEGER NOT NULL,           -- 1 clavier, 2 souris ou pavé tactile
            hw_hash BLOB NOT NULL UNIQUE
        );
        CREATE TABLE layout (
            id   INTEGER PRIMARY KEY,
            klid TEXT NOT NULL,
            hash BLOB NOT NULL UNIQUE,
            data BLOB NOT NULL                  -- LayoutTable.Serialize()
        );

        -- Chronologie et brut
        CREATE TABLE focus (
            id        INTEGER PRIMARY KEY,
            ts_ms     INTEGER NOT NULL,
            app_id    INTEGER NOT NULL REFERENCES app(id),
            title_id  INTEGER REFERENCES window_title(id),
            layout_id INTEGER REFERENCES layout(id),
            flags     INTEGER NOT NULL DEFAULT 0
        );
        CREATE INDEX focus_ts ON focus(ts_ms);
        CREATE TABLE raw_block (
            id       INTEGER PRIMARY KEY,
            start_ms INTEGER NOT NULL,
            end_ms   INTEGER NOT NULL,
            focus_id INTEGER REFERENCES focus(id),
            n_events INTEGER NOT NULL,
            epoch_id INTEGER NOT NULL REFERENCES key_epoch(id),
            nonce    BLOB NOT NULL,
            payload  BLOB NOT NULL
        );
        CREATE INDEX raw_block_start ON raw_block(start_ms);
        CREATE TABLE pause (
            id       INTEGER PRIMARY KEY,
            start_ms INTEGER NOT NULL,
            end_ms   INTEGER NOT NULL,
            reason   INTEGER NOT NULL           -- PauseReason
        );
        CREATE INDEX pause_start ON pause(start_ms);
        CREATE TABLE key_store (
            id         INTEGER PRIMARY KEY,
            kind       INTEGER NOT NULL,        -- 1 phrase de secours, 2 Windows Hello
            created_ms INTEGER NOT NULL,
            salt       BLOB NOT NULL,
            iterations INTEGER NOT NULL,
            nonce      BLOB NOT NULL,
            wrapped    BLOB NOT NULL,           -- clé privée PKCS#8 chiffrée (AES-GCM, étiquette à la fin)
            cred_id    TEXT
        );

        -- Statistiques en clair
        CREATE TABLE key_minute (
            ts_min    INTEGER NOT NULL,
            key       INTEGER NOT NULL,
            device_id INTEGER NOT NULL,
            presses   INTEGER NOT NULL,
            repeats   INTEGER NOT NULL,
            PRIMARY KEY (ts_min, key, device_id)
        ) WITHOUT ROWID;
        CREATE TABLE minute_total (
            ts_min        INTEGER PRIMARY KEY,
            keys          INTEGER NOT NULL,
            chars         INTEGER NOT NULL,
            backspaces    INTEGER NOT NULL,
            clicks        INTEGER NOT NULL,
            writing_ms    INTEGER NOT NULL,
            excluded_keys INTEGER NOT NULL
        );
        CREATE TABLE app_hour (
            ts_hour    INTEGER NOT NULL,
            app_id     INTEGER NOT NULL,
            keys       INTEGER NOT NULL,
            chars      INTEGER NOT NULL,
            writing_ms INTEGER NOT NULL,
            sessions   INTEGER NOT NULL,
            PRIMARY KEY (ts_hour, app_id)
        ) WITHOUT ROWID;
        CREATE TABLE session (
            id             INTEGER PRIMARY KEY,
            day            INTEGER NOT NULL,
            start_ms       INTEGER NOT NULL,
            end_ms         INTEGER NOT NULL,
            app_id         INTEGER NOT NULL,
            title_id       INTEGER,
            device_id      INTEGER NOT NULL,
            kind           INTEGER NOT NULL,
            chars          INTEGER NOT NULL,
            typed          INTEGER NOT NULL,
            deleted        INTEGER NOT NULL,
            keys           INTEGER NOT NULL,
            text_keys      INTEGER NOT NULL,
            repeats        INTEGER NOT NULL,
            active_ms      INTEGER NOT NULL,
            gross_cpm      REAL NOT NULL,
            net_cpm        REAL NOT NULL,
            real_cpm       REAL NOT NULL,
            peak_cpm       REAL,
            typos          INTEGER NOT NULL,
            collateral     INTEGER NOT NULL,
            corrections    INTEGER NOT NULL,
            unanalyzed     INTEGER NOT NULL,
            retyped        INTEGER NOT NULL,
            reformulations INTEGER NOT NULL,
            overlaps       INTEGER NOT NULL,
            kspc           REAL NOT NULL
        );
        CREATE INDEX session_start ON session(start_ms);
        CREATE INDEX session_day ON session(day);
        CREATE TABLE day_char (
            day  INTEGER NOT NULL,
            kind INTEGER NOT NULL,
            ch   INTEGER NOT NULL,
            n    INTEGER NOT NULL,
            PRIMARY KEY (day, kind, ch)
        ) WITHOUT ROWID;
        CREATE TABLE day_bigram (
            day    INTEGER NOT NULL,
            a      INTEGER NOT NULL,
            b      INTEGER NOT NULL,
            n      INTEGER NOT NULL,
            errors INTEGER NOT NULL,
            lat    BLOB,                        -- HistogramCodec, échelle LogScale.BigramLatency
            PRIMARY KEY (day, a, b)
        ) WITHOUT ROWID;
        CREATE TABLE day_wordlen (
            day INTEGER NOT NULL,
            len INTEGER NOT NULL,
            n   INTEGER NOT NULL,
            PRIMARY KEY (day, len)
        ) WITHOUT ROWID;
        CREATE TABLE day_shortcut (
            day  INTEGER NOT NULL,
            mods INTEGER NOT NULL,
            key  INTEGER NOT NULL,
            n    INTEGER NOT NULL,
            PRIMARY KEY (day, mods, key)
        ) WITHOUT ROWID;
        CREATE TABLE day_error_pair (
            day    INTEGER NOT NULL,
            typed  INTEGER NOT NULL,
            wanted INTEGER NOT NULL,
            type   INTEGER NOT NULL,
            n      INTEGER NOT NULL,
            PRIMARY KEY (day, typed, wanted, type)
        ) WITHOUT ROWID;
        CREATE TABLE day_metric (
            day    INTEGER NOT NULL,
            metric INTEGER NOT NULL,
            value  INTEGER NOT NULL,
            PRIMARY KEY (day, metric)
        ) WITHOUT ROWID;
        CREATE TABLE day_hist (
            day  INTEGER NOT NULL,
            hist INTEGER NOT NULL,
            sub  INTEGER NOT NULL,
            data BLOB NOT NULL,
            PRIMARY KEY (day, hist, sub)
        ) WITHOUT ROWID;
        """,

        // Version 2 : longueurs de mots par nature de session (prose, code). Les lignes d'avant n'en ont
        // pas : elles passent sous kind = 0 (SessionKind.None), comptées dans les totaux, ni en prose ni en
        // code, jusqu'au recalcul depuis le brut.
        """
        CREATE TABLE day_wordlen_v2 (
            day  INTEGER NOT NULL,
            kind INTEGER NOT NULL,
            len  INTEGER NOT NULL,
            n    INTEGER NOT NULL,
            PRIMARY KEY (day, kind, len)
        ) WITHOUT ROWID;
        INSERT INTO day_wordlen_v2(day, kind, len, n) SELECT day, 0, len, n FROM day_wordlen;
        DROP TABLE day_wordlen;
        ALTER TABLE day_wordlen_v2 RENAME TO day_wordlen;
        """,
    ];
}

/// <summary>Motif d'une période sans enregistrement (table <c>pause</c>).</summary>
public enum PauseReason
{
    /// <summary>Pause demandée par l'icône de notification.</summary>
    Snooze = 1,
    /// <summary>Session Windows verrouillée ou en veille.</summary>
    Locked = 2,
    /// <summary>Fenêtre lancée en administrateur : touches probablement invisibles.</summary>
    Elevated = 3,
    /// <summary>Appli exclue : seul le nombre de touches est gardé.</summary>
    Excluded = 4,
    /// <summary>Agent arrêté (trou entre deux exécutions).</summary>
    Stopped = 5,
    /// <summary>Champ de mot de passe : seul le nombre de touches est gardé.</summary>
    Secret = 6,
}

/// <summary>Façon dont la clé privée est protégée (colonne <c>key_store.kind</c>).</summary>
public enum KeyStoreKind
{
    Passphrase = 1,
    WindowsHello = 2,
}
