using KeyStats.Core.Crypto;
using KeyStats.Core.Input;

namespace KeyStats.Core.Storage;

/// <summary>
/// Côté agent : écrit les blocs bruts et les titres de fenêtre, chiffrés avec l'époque en cours.
/// Ne connaît que la clé publique maîtresse. À utiliser dans la transaction d'enregistrement.
/// </summary>
public sealed class RawArchiveWriter : IDisposable
{
    public const long EpochLifetimeMs = 3_600_000;

    private readonly KeyStatsDb _db;
    private readonly byte[] _masterPublicKey;
    private readonly TitleHasher _hasher;
    private EpochCipher? _cipher;
    private long _epochStartMs;
    private long _epochRollbacks;

    public RawArchiveWriter(KeyStatsDb db, byte[] masterPublicKey, TitleHasher hasher)
    {
        _db = db;
        _masterPublicKey = masterPublicKey;
        _hasher = hasher;
    }

    /// <summary>Époque en cours ; une nouvelle est créée au premier usage et après une heure.</summary>
    private EpochCipher Cipher(long nowMs)
    {
        // Une époque créée dans une transaction annulée n'a plus de ligne key_epoch : on en refait une.
        if (_cipher is null || nowMs - _epochStartMs >= EpochLifetimeMs || nowMs < _epochStartMs
            || _db.RollbackCount != _epochRollbacks)
        {
            var (ephemeral, key) = EpochKeys.Create(_masterPublicKey);
            try
            {
                int id = _db.AddEpoch(nowMs, ephemeral);
                _cipher?.Dispose();
                _cipher = new EpochCipher(id, key);
                _epochStartMs = nowMs;
                _epochRollbacks = _db.RollbackCount;
            }
            finally
            {
                System.Security.Cryptography.CryptographicOperations.ZeroMemory(key);
            }
        }
        return _cipher;
    }

    /// <summary>Oublie la clé d'époque (verrouillage, veille) : la suivante sera neuve.</summary>
    public void DropEpoch()
    {
        _cipher?.Dispose();
        _cipher = null;
    }

    /// <summary>Enregistre un bloc d'événements croissants. Renvoie l'id du bloc, ou 0 s'il est vide.</summary>
    public long WriteBlock(ReadOnlySpan<InputEvent> events, long? focusId, long nowMs)
    {
        if (events.IsEmpty) return 0;
        long start = events[0].Ms, end = Math.Max(start, events[^1].Ms);
        byte[] payload = RawCodec.Encode(events, start);
        var cipher = Cipher(nowMs);
        var (nonce, data) = cipher.EncryptBlock(payload, start, end, events.Length, focusId);
        // Le brut en clair ne reste pas en mémoire, d'où il pourrait finir dans le fichier d'échange.
        System.Security.Cryptography.CryptographicOperations.ZeroMemory(payload);
        return _db.AddRawBlock(start, end, focusId, events.Length, cipher.EpochId, nonce, data);
    }

    /// <summary>Id du titre, ajouté chiffré s'il n'a jamais été vu. Le titre ne touche jamais le disque en clair.</summary>
    public long GetOrAddTitle(int appId, string exe, string title, long nowMs)
    {
        byte[] hash = _hasher.Hash(exe, title);
        if (_db.FindTitle(hash) is long existing) return existing;
        var cipher = Cipher(nowMs);
        var (nonce, data) = cipher.EncryptTitle(title, hash);
        return _db.AddTitle(appId, hash, cipher.EpochId, nonce, data);
    }

    public void Dispose()
    {
        DropEpoch();
        _hasher.Dispose();
    }
}

/// <summary>Côté tableau de bord, après déverrouillage : relit le brut et les titres.</summary>
public sealed class RawArchiveReader : IDisposable
{
    private readonly KeyStatsDb _db;
    private readonly MasterKey _master;
    private readonly Dictionary<int, EpochCipher> _epochs = [];

    public RawArchiveReader(KeyStatsDb db, MasterKey master)
    {
        _db = db;
        _master = master;
    }

    private EpochCipher Epoch(int id)
    {
        if (_epochs.TryGetValue(id, out var cipher)) return cipher;
        byte[] ephemeral = _db.GetEpochPublicKey(id) ?? throw new InvalidDataException("Époque inconnue : " + id);
        byte[] key = _master.DeriveEpochKey(ephemeral);
        try
        {
            cipher = new EpochCipher(id, key);
        }
        finally
        {
            System.Security.Cryptography.CryptographicOperations.ZeroMemory(key);
        }
        _epochs[id] = cipher;
        return cipher;
    }

    public InputEvent[] ReadBlock(StoredRawBlock block)
    {
        ArgumentNullException.ThrowIfNull(block);
        byte[] payload = Epoch(block.EpochId).DecryptBlock(block.Nonce, block.Payload, block.StartMs, block.EndMs, block.EventCount, block.FocusId);
        InputEvent[] events;
        try
        {
            events = RawCodec.Decode(payload, block.StartMs);
        }
        finally
        {
            System.Security.Cryptography.CryptographicOperations.ZeroMemory(payload);
        }
        if (events.Length != block.EventCount) throw new InvalidDataException("Bloc brut incohérent.");
        return events;
    }

    /// <summary>Blocs commençant dans [from, to), dans l'ordre, avec leurs événements.</summary>
    public IEnumerable<(StoredRawBlock Block, InputEvent[] Events)> ReadBlocks(long fromMs, long toMs)
    {
        foreach (var block in _db.GetRawBlocks(fromMs, toMs))
            yield return (block, ReadBlock(block));
    }

    public string? ReadTitle(long id)
    {
        var stored = _db.GetTitle(id);
        if (stored is null) return null;
        byte[] hash = _db.GetTitleHash(id) ?? throw new InvalidDataException("Titre sans empreinte.");
        return Epoch(stored.EpochId).DecryptTitle(stored.Nonce, stored.Encrypted, hash);
    }

    public void Dispose()
    {
        foreach (var c in _epochs.Values) c.Dispose();
        _epochs.Clear();
    }
}
