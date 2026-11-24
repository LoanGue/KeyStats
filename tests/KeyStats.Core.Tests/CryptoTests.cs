using System.Security.Cryptography;
using System.Text;
using KeyStats.Core.Crypto;
using KeyStats.Core.Storage;

namespace KeyStats.Core.Tests;

public sealed class CryptoTests
{
    private const int FastIterations = 100_000;

    [Fact]
    public void Cle_d_epoque_retrouvee_par_la_cle_privee_seulement()
    {
        using var master = MasterKey.Generate();
        using var other = MasterKey.Generate();

        var (ephemeral, key) = EpochKeys.Create(master.PublicKey);

        Assert.Equal(key, master.DeriveEpochKey(ephemeral));
        Assert.NotEqual(key, other.DeriveEpochKey(ephemeral));
    }

    [Fact]
    public void Bloc_chiffre_lie_a_ses_dates_et_a_son_nombre_d_evenements()
    {
        using var cipher = new EpochCipher(1, RandomNumberGenerator.GetBytes(32));
        byte[] payload = Encoding.UTF8.GetBytes("contenu du bloc");

        var (nonce, ct) = cipher.EncryptBlock(payload, 100, 200, 3);

        Assert.Equal(payload, cipher.DecryptBlock(nonce, ct, 100, 200, 3));
        Assert.ThrowsAny<CryptographicException>(() => cipher.DecryptBlock(nonce, ct, 100, 200, 4));
        Assert.ThrowsAny<CryptographicException>(() => cipher.DecryptBlock(nonce, ct, 101, 200, 3));
        ct[0] ^= 1;
        Assert.ThrowsAny<CryptographicException>(() => cipher.DecryptBlock(nonce, ct, 100, 200, 3));
    }

    [Fact]
    public void Empreinte_de_titre_stable_et_dependante_de_la_cle()
    {
        using var a = new TitleHasher(TitleHasher.NewKey());
        using var b = new TitleHasher(TitleHasher.NewKey());

        Assert.Equal(a.Hash("Code.exe", "main.cs"), a.Hash("code.exe", "main.cs"));
        Assert.NotEqual(a.Hash("code.exe", "main.cs"), a.Hash("code.exe", "Main.cs"));
        Assert.NotEqual(a.Hash("code.exe", "main.cs"), b.Hash("code.exe", "main.cs"));
        Assert.Equal(TitleHasher.HashSize, a.Hash("x", "y").Length);
    }

    [Fact]
    public void Phrase_de_secours_aller_retour_et_phrase_fausse()
    {
        using var master = MasterKey.Generate();
        byte[] pkcs8 = master.ExportPkcs8();

        var entry = KeyWrap.WrapWithPassphrase(pkcs8, "Cheval correct batterie agrafe", 0, FastIterations);

        Assert.Equal(pkcs8, KeyWrap.UnwrapWithPassphrase(entry, "  Cheval   correct batterie agrafe "));
        Assert.Null(KeyWrap.UnwrapWithPassphrase(entry, "cheval correct batterie agrafe"));
        Assert.Null(KeyWrap.UnwrapWithPassphrase(entry, "autre chose"));
        using var restored = MasterKey.FromPkcs8(KeyWrap.UnwrapWithPassphrase(entry, "Cheval correct batterie agrafe"));
        Assert.Equal(master.PublicKey, restored.PublicKey);
    }

    [Fact]
    public void Phrase_normalisee_en_NFC()
    {
        // « é » composé (U+00E9) et décomposé (e + U+0301) donnent la même clé.
        string composed = "caf" + (char)0x00E9, decomposed = "cafe" + (char)0x0301;
        Assert.NotEqual(composed, decomposed);
        Assert.Equal(KeyWrap.NormalizePassphrase(composed), KeyWrap.NormalizePassphrase(decomposed));
    }

    [Fact]
    public void Trop_peu_d_iterations_refuse()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => KeyWrap.WrapWithPassphrase([1, 2, 3], "phrase", 0, 1000));
    }

    [Fact]
    public void Windows_Hello_aller_retour_et_signature_fausse()
    {
        byte[] pkcs8 = RandomNumberGenerator.GetBytes(138);
        byte[] challenge = KeyWrap.NewHelloChallenge();
        byte[] signature = RandomNumberGenerator.GetBytes(256);

        var entry = KeyWrap.WrapWithHello(pkcs8, challenge, signature, "cred", 0);

        Assert.Equal(challenge, entry.Salt);
        Assert.Equal(pkcs8, KeyWrap.UnwrapWithHello(entry, signature));
        Assert.Null(KeyWrap.UnwrapWithHello(entry, RandomNumberGenerator.GetBytes(256)));
        Assert.Throws<ArgumentException>(() => KeyWrap.UnwrapWithHello(entry, new byte[16]));
    }

    [Fact]
    public void Entree_d_un_type_ne_s_ouvre_pas_avec_l_autre()
    {
        var hello = KeyWrap.WrapWithHello([1, 2, 3], KeyWrap.NewHelloChallenge(), new byte[256], "cred", 0);
        Assert.Throws<ArgumentException>(() => KeyWrap.UnwrapWithPassphrase(hello, "phrase"));
    }

    [Fact]
    public void Cles_protegees_relues_depuis_la_base()
    {
        using var tmp = new TempDb();
        using var master = MasterKey.Generate();
        byte[] pkcs8 = master.ExportPkcs8();
        tmp.Db.AddKeyStore(KeyWrap.WrapWithPassphrase(pkcs8, "ma phrase", 1, FastIterations));
        byte[] sig = RandomNumberGenerator.GetBytes(256);
        tmp.Db.AddKeyStore(KeyWrap.WrapWithHello(pkcs8, KeyWrap.NewHelloChallenge(), sig, "cred-1", 2));

        var entries = tmp.Reopen().GetKeyStore();

        Assert.Equal(2, entries.Count);
        var phrase = Assert.Single(entries, e => e.Kind == KeyStoreKind.Passphrase);
        var hello = Assert.Single(entries, e => e.Kind == KeyStoreKind.WindowsHello);
        Assert.Equal(pkcs8, KeyWrap.UnwrapWithPassphrase(phrase, "ma phrase"));
        Assert.Equal(pkcs8, KeyWrap.UnwrapWithHello(hello, sig));
        Assert.Equal("cred-1", hello.CredentialId);

        tmp.Db.DeleteKeyStore(KeyStoreKind.WindowsHello);
        Assert.Single(tmp.Db.GetKeyStore());
    }
}
