using System.Security.Cryptography;
using System.Text;

namespace KeyStats.Core.Control;

/// <summary>Commande envoyée à l'agent par le tube nommé.</summary>
public enum AgentCommand : byte
{
    /// <summary>5 min de pause de plus, comme un clic sur l'icône.</summary>
    Snooze = 1,
    PauseUntilResumed = 2,
    /// <summary>Fin de la pause par l'icône ; sans effet sur le verrouillage ou la veille.</summary>
    Resume = 3,
    /// <summary>Tout ce qui est en attente est écrit avant la réponse.</summary>
    Flush = 4,
    Quit = 5,
}

public enum AgentReply : byte
{
    Ok = 1,
    /// <summary>Ligne illisible ou commande inconnue.</summary>
    Unknown = 2,
    /// <summary>Commande reconnue mais pas exécutée (arrêt en cours, écriture pas confirmée à temps).</summary>
    Failed = 3,
}

/// <summary>
/// Format du tube de l'agent : le client écrit une commande en ASCII suivie de <c>\n</c>, lit la réponse
/// sur une ligne, puis ferme. Une connexion par commande. Rien d'autre ne passe : ni texte, ni titre.
/// </summary>
public static class ControlProtocol
{
    /// <summary>Longueur maximale d'une ligne, saut de ligne compris.</summary>
    public const int MaxLine = 32;

    private static readonly string[] Commands = ["", "snooze", "pause", "resume", "flush", "quit"];
    private static readonly string[] Replies = ["", "ok", "unknown", "failed"];

    /// <summary>
    /// Un agent par base et par session Windows : la clé vient du chemin complet de la base, sans casse,
    /// pour que <c>--db</c> sur une base de test ne gêne pas l'agent installé.
    /// </summary>
    public static string InstanceKey(string dbPath)
    {
        byte[] hash = SHA256.HashData(Encoding.UTF8.GetBytes(Path.GetFullPath(dbPath).ToUpperInvariant()));
        return Convert.ToHexStringLower(hash.AsSpan(0, 8));
    }

    public static string PipeName(int sessionId, string dbPath) => $"KeyStats.Agent.{sessionId}.{InstanceKey(dbPath)}";

    public static byte[] Encode(AgentCommand command) => Line(Commands, (int)command);

    public static byte[] Encode(AgentReply reply) => Line(Replies, (int)reply);

    public static bool TryParse(ReadOnlySpan<byte> line, out AgentCommand command)
    {
        int i = Find(Commands, line);
        command = (AgentCommand)i;
        return i > 0;
    }

    public static bool TryParse(ReadOnlySpan<byte> line, out AgentReply reply)
    {
        int i = Find(Replies, line);
        reply = (AgentReply)i;
        return i > 0;
    }

    /// <summary>
    /// Lit jusqu'au <c>\n</c>, exclu du résultat. -1 si le flux se ferme avant, ou si la ligne
    /// dépasse <see cref="MaxLine"/>.
    /// </summary>
    internal static async Task<int> ReadLineAsync(Stream stream, byte[] buffer, CancellationToken token)
    {
        int length = 0;
        while (length < MaxLine)
        {
            int n = await stream.ReadAsync(buffer.AsMemory(length, MaxLine - length), token).ConfigureAwait(false);
            if (n == 0) return -1;
            int end = buffer.AsSpan(length, n).IndexOf((byte)'\n');
            if (end >= 0) return length + end;
            length += n;
        }
        return -1;
    }

    private static byte[] Line(string[] words, int i)
    {
        if (i <= 0 || i >= words.Length) throw new ArgumentOutOfRangeException(nameof(i), i, "Valeur hors du protocole.");
        return Encoding.ASCII.GetBytes(words[i] + "\n");
    }

    /// <summary>Ligne sans son <c>\n</c> (ni un éventuel <c>\r</c>) ; casse exacte.</summary>
    private static int Find(string[] words, ReadOnlySpan<byte> line)
    {
        if (line.EndsWith("\r"u8)) line = line[..^1];
        for (int i = 1; i < words.Length; i++)
        {
            if (line.Length == words[i].Length && Ascii.Equals(line, words[i])) return i;
        }
        return 0;
    }
}
