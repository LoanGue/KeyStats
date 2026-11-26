using System.Security.AccessControl;
using System.Security.Principal;
using Microsoft.Web.WebView2.Core;

namespace KeyStats.Dashboard;

/// <summary>
/// Rapports de plantage de la WebView2 : un vidage mémoire du moteur de rendu peut contenir la phrase
/// de secours ou du texte déchiffré. Rien n'est envoyé à Microsoft, Crashpad ne peut pas écrire dans ses
/// dossiers de rapports, et ce qui y serait quand même arrivé est supprimé.
/// </summary>
/// <remarks>
/// Aucun réglage de WebView2 ne coupe Crashpad (<c>--disable-breakpad</c> et <c>--disable-crash-reporter</c>
/// sont sans effet). Vérifié par <c>Page.crash</c> : sans refus d'écriture, un <c>.dmp</c> de 6 Mo apparaît
/// dans <c>EBWebView\Crashpad\reports</c> ; avec, plus rien.
/// </remarks>
internal static class CrashReports
{
    private static readonly string[] Folders = ["reports", "attachments"];

    public static CoreWebView2EnvironmentOptions Options() => new() { IsCustomCrashReportingEnabled = true };

    /// <summary>À appeler avant de créer l'environnement : Crashpad ouvre ses dossiers au démarrage du navigateur.</summary>
    public static void Lock(string profileFolder)
    {
        string crashpad = Path.Combine(profileFolder, "EBWebView", "Crashpad");
        foreach (string name in Folders) DenyWrites(Path.Combine(crashpad, name));
    }

    public static void Watch(CoreWebView2Environment env, CoreWebView2 core)
    {
        string folder = env.FailureReportFolderPath;
        DenyWrites(folder);
        Purge(folder);
        // Filet de sécurité si le refus n'a pas pris : passages à 0, 5 et 30 s après le plantage.
        core.ProcessFailed += async (_, _) =>
        {
            foreach (int wait in (int[])[0, 5, 25])
            {
                await Task.Delay(TimeSpan.FromSeconds(wait));
                Purge(folder);
            }
        };
    }

    /// <summary>Refuse à l'utilisateur courant (donc à Crashpad) d'y créer fichiers et dossiers ; la suppression reste permise.</summary>
    private static void DenyWrites(string folder)
    {
        if (string.IsNullOrEmpty(folder)) return;
        var dir = Directory.CreateDirectory(folder);
        var acl = dir.GetAccessControl();
        var user = WindowsIdentity.GetCurrent().User!;
        acl.AddAccessRule(new FileSystemAccessRule(user, FileSystemRights.WriteData | FileSystemRights.AppendData,
            InheritanceFlags.ContainerInherit | InheritanceFlags.ObjectInherit, PropagationFlags.None, AccessControlType.Deny));
        dir.SetAccessControl(acl);
    }

    /// <summary>Supprime tout le contenu de <paramref name="folder"/> ; un fichier encore ouvert est laissé au passage suivant.</summary>
    public static void Purge(string? folder)
    {
        if (string.IsNullOrEmpty(folder)) return;
        try
        {
            foreach (string path in Directory.EnumerateFileSystemEntries(folder))
            {
                try
                {
                    if (Directory.Exists(path)) Directory.Delete(path, recursive: true);
                    else File.Delete(path);
                }
                catch (Exception e) when (e is IOException or UnauthorizedAccessException)
                {
                    // Verrouillé par Crashpad : repris au prochain passage.
                }
            }
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            // Dossier absent ou disparu pendant l'énumération : rien à supprimer.
        }
    }
}
