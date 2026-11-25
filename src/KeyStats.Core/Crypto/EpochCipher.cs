using System.Buffers.Binary;
using System.Security.Cryptography;
using System.Text;

namespace KeyStats.Core.Crypto;

/// <summary>
/// Chiffrement AES-256-GCM des blocs bruts et des titres d'une époque. Nonce aléatoire de 12 octets
/// par message, étiquette de 16 octets ajoutée à la fin. Les données associées lient chaque message
/// à sa ligne (dates et nombre d'événements du bloc, empreinte du titre) : on ne peut pas les
/// échanger entre lignes sans que le déchiffrement échoue.
/// </summary>
public sealed class EpochCipher : IDisposable
{
    public const int NonceSize = 12, TagSize = 16;

    private readonly AesGcm _aes;
    private readonly byte[] _key;

    public EpochCipher(int epochId, byte[] key)
    {
        ArgumentNullException.ThrowIfNull(key);
        if (key.Length != 32) throw new ArgumentException("Clé AES-256 attendue.", nameof(key));
        EpochId = epochId;
        _key = (byte[])key.Clone();
        _aes = new AesGcm(_key, TagSize);
    }

    public int EpochId { get; }

    public (byte[] Nonce, byte[] Ciphertext) EncryptBlock(ReadOnlySpan<byte> payload, long startMs, long endMs, int eventCount, long? focusId)
    {
        Span<byte> aad = stackalloc byte[BlockAadSize];
        BlockAad(aad, startMs, endMs, eventCount, focusId);
        return Encrypt(payload, aad);
    }

    public byte[] DecryptBlock(ReadOnlySpan<byte> nonce, ReadOnlySpan<byte> ciphertext, long startMs, long endMs, int eventCount, long? focusId)
    {
        Span<byte> aad = stackalloc byte[BlockAadSize];
        BlockAad(aad, startMs, endMs, eventCount, focusId);
        return Decrypt(nonce, ciphertext, aad);
    }

    public (byte[] Nonce, byte[] Ciphertext) EncryptTitle(string title, ReadOnlySpan<byte> titleHash)
    {
        ArgumentNullException.ThrowIfNull(title);
        byte[] plain = Encoding.UTF8.GetBytes(title);
        try
        {
            return Encrypt(plain, TitleAad(titleHash));
        }
        finally
        {
            CryptographicOperations.ZeroMemory(plain);
        }
    }

    public string DecryptTitle(ReadOnlySpan<byte> nonce, ReadOnlySpan<byte> ciphertext, ReadOnlySpan<byte> titleHash)
    {
        byte[] plain = Decrypt(nonce, ciphertext, TitleAad(titleHash));
        try
        {
            return Encoding.UTF8.GetString(plain);
        }
        finally
        {
            CryptographicOperations.ZeroMemory(plain);
        }
    }

    private (byte[] Nonce, byte[] Ciphertext) Encrypt(ReadOnlySpan<byte> plain, ReadOnlySpan<byte> aad)
    {
        var nonce = RandomNumberGenerator.GetBytes(NonceSize);
        var output = new byte[plain.Length + TagSize];
        _aes.Encrypt(nonce, plain, output.AsSpan(0, plain.Length), output.AsSpan(plain.Length), aad);
        return (nonce, output);
    }

    private byte[] Decrypt(ReadOnlySpan<byte> nonce, ReadOnlySpan<byte> ciphertext, ReadOnlySpan<byte> aad)
    {
        if (nonce.Length != NonceSize || ciphertext.Length < TagSize)
            throw new CryptographicException("Message chiffré invalide.");
        int n = ciphertext.Length - TagSize;
        var plain = new byte[n];
        _aes.Decrypt(nonce, ciphertext[..n], ciphertext[n..], plain, aad);
        return plain;
    }

    private const int BlockAadSize = 32;

    /// <summary>Le bloc reste lié à ses dates, à son nombre d'événements et à sa fenêtre (0 : aucune).</summary>
    private static void BlockAad(Span<byte> aad, long startMs, long endMs, int eventCount, long? focusId)
    {
        "KSR1"u8.CopyTo(aad);
        BinaryPrimitives.WriteInt64LittleEndian(aad[4..], startMs);
        BinaryPrimitives.WriteInt64LittleEndian(aad[12..], endMs);
        BinaryPrimitives.WriteInt32LittleEndian(aad[20..], eventCount);
        BinaryPrimitives.WriteInt64LittleEndian(aad[24..], focusId ?? 0);
    }

    private static byte[] TitleAad(ReadOnlySpan<byte> titleHash)
    {
        var aad = new byte[4 + titleHash.Length];
        "KST1"u8.CopyTo(aad);
        titleHash.CopyTo(aad.AsSpan(4));
        return aad;
    }

    public void Dispose()
    {
        _aes.Dispose();
        CryptographicOperations.ZeroMemory(_key);
    }
}

/// <summary>
/// Empreinte d'un titre de fenêtre (HMAC-SHA256 tronqué à 16 octets) : reconnaît un titre déjà
/// stocké sans l'écrire en clair. La clé est locale, protégée par DPAPI côté agent.
/// </summary>
public sealed class TitleHasher : IDisposable
{
    public const int KeySize = 32, HashSize = 16;

    private readonly byte[] _key;

    public TitleHasher(ReadOnlySpan<byte> key)
    {
        if (key.Length != KeySize) throw new ArgumentException("Clé de 32 octets attendue.", nameof(key));
        _key = key.ToArray();
    }

    public static byte[] NewKey() => RandomNumberGenerator.GetBytes(KeySize);

    public byte[] Hash(string exe, string title)
    {
        ArgumentNullException.ThrowIfNull(exe);
        ArgumentNullException.ThrowIfNull(title);
        string text = string.Concat(exe.ToLowerInvariant(), "\n", title);
        byte[] data = Encoding.UTF8.GetBytes(text);
        Span<byte> mac = stackalloc byte[32];
        HMACSHA256.HashData(_key, data, mac);
        CryptographicOperations.ZeroMemory(data);
        return mac[..HashSize].ToArray();
    }

    public void Dispose() => CryptographicOperations.ZeroMemory(_key);
}
