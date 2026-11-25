using System.ComponentModel;
using System.Security.Cryptography;
using KeyStats.Agent.Interop;
using KeyStats.Core.Crypto;
using KeyStats.Core.Storage;

namespace KeyStats.Agent.Recording;

/// <summary>
/// Base de l'agent et écriture chiffrée. Sans clé publique maîtresse (assistant pas encore passé),
/// <see cref="Archive"/> est nul : seules les statistiques sont écrites.
/// </summary>
internal sealed class AgentStorage : IDisposable
{
    private AgentStorage(KeyStatsDb db, RawArchiveWriter? archive, bool titleKeyReplaced)
    {
        Db = db;
        Archive = archive;
        TitleKeyReplaced = titleKeyReplaced;
    }

    public static string DefaultPath =>
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "KeyStats", "keystats.db");

    public KeyStatsDb Db { get; }

    public RawArchiveWriter? Archive { get; }

    /// <summary>La clé des empreintes n'était plus lisible (profil réinitialisé) : une nouvelle a été créée.</summary>
    public bool TitleKeyReplaced { get; }

    public static AgentStorage Open(string path)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(path))!);
        var db = KeyStatsDb.Open(path);
        try
        {
            if (db.GetMeta(MetaKeys.MasterPublicKey) is not { Length: > 0 } masterPublic)
                return new AgentStorage(db, null, false);
            byte[] key = LoadTitleKey(db, out bool replaced);
            try
            {
                return new AgentStorage(db, new RawArchiveWriter(db, masterPublic, new TitleHasher(key)), replaced);
            }
            finally
            {
                CryptographicOperations.ZeroMemory(key);
            }
        }
        catch
        {
            db.Dispose();
            throw;
        }
    }

    /// <summary>
    /// Clé créée une fois puis gardée sous DPAPI. Si elle est illisible, les titres déjà stockés ne
    /// seront plus reconnus : ils seront ajoutés une seconde fois, ce qui ne perd rien.
    /// </summary>
    private static byte[] LoadTitleKey(KeyStatsDb db, out bool replaced)
    {
        replaced = false;
        if (db.GetMeta(MetaKeys.TitleKey) is { } stored)
        {
            try
            {
                byte[] key = Dpapi.Unprotect(stored);
                if (key.Length == TitleHasher.KeySize) return key;
                CryptographicOperations.ZeroMemory(key);
            }
            catch (Win32Exception)
            {
            }
            replaced = true;
        }
        byte[] fresh = TitleHasher.NewKey();
        db.SetMeta(MetaKeys.TitleKey, Dpapi.Protect(fresh));
        return fresh;
    }

    public void Dispose()
    {
        Archive?.Dispose();
        Db.Dispose();
    }
}
