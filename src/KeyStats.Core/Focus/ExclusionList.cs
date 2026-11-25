namespace KeyStats.Core.Focus;

/// <summary>
/// Exécutables dont on ne garde que le nombre de touches : ni brut, ni titre, ni analyse de texte.
/// Comparaison sur le nom de fichier, sans tenir compte de la casse ni du dossier.
/// </summary>
public sealed class ExclusionList
{
    /// <summary>
    /// Gestionnaires de mots de passe, bureau à distance (on y tape au moins un mot de passe) et
    /// invites d'identifiants de Windows.
    /// </summary>
    public static IReadOnlyList<string> Defaults { get; } =
    [
        "KeePass.exe", "KeePassXC.exe", "Bitwarden.exe", "1Password.exe",
        "mstsc.exe", "msrdc.exe",
        "CredentialUIBroker.exe", "consent.exe", "LockApp.exe", "LogonUI.exe",
    ];

    private readonly HashSet<string> _names;

    public ExclusionList(IEnumerable<string> executables)
    {
        _names = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (string exe in executables)
            if (Name(exe) is { Length: > 0 } name) _names.Add(name);
    }

    public static ExclusionList Default { get; } = new(Defaults);

    public int Count => _names.Count;

    public bool Contains(string exe) => _names.Contains(Name(exe));

    // Les deux séparateurs, quel que soit le système où tournent les tests.
    private static string Name(string exe) => exe[(exe.AsSpan().LastIndexOfAny('\\', '/') + 1)..].Trim();
}
