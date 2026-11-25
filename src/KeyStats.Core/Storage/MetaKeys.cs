namespace KeyStats.Core.Storage;

/// <summary>Noms des entrées de la table <c>meta</c> partagées entre l'agent et le tableau de bord.</summary>
public static class MetaKeys
{
    /// <summary>Clé publique maîtresse (ECDH P-256), écrite par l'assistant de première installation.</summary>
    public const string MasterPublicKey = "master_pub";

    /// <summary>Clé HMAC des empreintes de titres, protégée par DPAPI du compte courant.</summary>
    public const string TitleKey = "title_key_dpapi";
}
