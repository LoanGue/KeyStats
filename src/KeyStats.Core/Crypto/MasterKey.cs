using System.Security.Cryptography;

namespace KeyStats.Core.Crypto;

/// <summary>
/// Clé maîtresse ECDH P-256. La partie publique (SPKI) est tout ce dont l'agent a besoin pour
/// chiffrer ; la partie privée n'existe en mémoire que dans le tableau de bord, une fois déverrouillé.
/// </summary>
public sealed class MasterKey : IDisposable
{
    private readonly ECDiffieHellman _key;

    private MasterKey(ECDiffieHellman key) => _key = key;

    public static MasterKey Generate() => new(ECDiffieHellman.Create(ECCurve.NamedCurves.nistP256));

    public static MasterKey FromPkcs8(ReadOnlySpan<byte> pkcs8)
    {
        var key = ECDiffieHellman.Create();
        try
        {
            key.ImportPkcs8PrivateKey(pkcs8, out _);
            if (key.KeySize != 256) throw new CryptographicException("Clé maîtresse inattendue.");
            return new MasterKey(key);
        }
        catch
        {
            key.Dispose();
            throw;
        }
    }

    public byte[] PublicKey => _key.ExportSubjectPublicKeyInfo();

    /// <summary>Clé privée en clair : uniquement pour la rechiffrer aussitôt (<see cref="KeyWrap"/>). À effacer après usage.</summary>
    public byte[] ExportPkcs8() => _key.ExportPkcs8PrivateKey();

    /// <summary>Retrouve la clé AES d'une époque à partir de sa clé publique jetable.</summary>
    public byte[] DeriveEpochKey(ReadOnlySpan<byte> ephemeralPublicKey)
    {
        using var ephemeral = ECDiffieHellman.Create();
        ephemeral.ImportSubjectPublicKeyInfo(ephemeralPublicKey, out _);
        byte[] secret = _key.DeriveRawSecretAgreement(ephemeral.PublicKey);
        try
        {
            return EpochKeys.Kdf(secret, ephemeralPublicKey);
        }
        finally
        {
            CryptographicOperations.ZeroMemory(secret);
        }
    }

    public void Dispose() => _key.Dispose();
}

/// <summary>Création des époques côté agent, avec la seule clé publique.</summary>
public static class EpochKeys
{
    private static ReadOnlySpan<byte> Info => "keystats epoch v1"u8;

    /// <summary>Nouvelle époque : clé publique jetable (à stocker) et clé AES-256 (à garder en mémoire seulement).</summary>
    public static (byte[] EphemeralPublicKey, byte[] Key) Create(ReadOnlySpan<byte> masterPublicKey)
    {
        using var master = ECDiffieHellman.Create();
        master.ImportSubjectPublicKeyInfo(masterPublicKey, out _);
        if (master.KeySize != 256) throw new CryptographicException("Clé publique maîtresse inattendue.");
        using var ephemeral = ECDiffieHellman.Create(ECCurve.NamedCurves.nistP256);
        byte[] ephemeralPublic = ephemeral.ExportSubjectPublicKeyInfo();
        byte[] secret = ephemeral.DeriveRawSecretAgreement(master.PublicKey);
        try
        {
            return (ephemeralPublic, Kdf(secret, ephemeralPublic));
        }
        finally
        {
            CryptographicOperations.ZeroMemory(secret);
        }
    }

    internal static byte[] Kdf(ReadOnlySpan<byte> secret, ReadOnlySpan<byte> ephemeralPublic)
    {
        var key = new byte[32];
        HKDF.DeriveKey(HashAlgorithmName.SHA256, secret, key, ephemeralPublic, Info);
        return key;
    }
}
