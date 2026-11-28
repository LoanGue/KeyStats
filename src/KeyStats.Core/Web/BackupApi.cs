using KeyStats.Core.Settings;
using KeyStats.Core.Storage;

namespace KeyStats.Core.Web;

/// <summary>Sélecteur de dossier du système, ouvert par le tableau de bord.</summary>
public interface IFolderPicker
{
    /// <summary>Dossier choisi, chemin complet ; <c>null</c> si la fenêtre est fermée sans choix.</summary>
    Task<string?> PickAsync(string initialFolder);
}

/// <param name="Folder">Dossier du miroir en vigueur, chemin complet.</param>
/// <param name="Custom">Dossier choisi dans les réglages ; faux pour <c>backup\</c> à côté de la base.</param>
/// <param name="LastMs">Dernière sauvegarde réussie dans ce dossier (ms UTC), nulle sans miroir lisible.</param>
public sealed record BackupView(string Folder, bool Custom, long? LastMs);

/// <param name="Outcome">
/// Dossier : <c>saved</c>, <c>cancelled</c> (fenêtre fermée), <c>invalid</c> (dossier absent, ou celui de la base),
/// <c>unreadable</c> (<c>settings.json</c> refusé, laissé tel quel), <c>failed</c> (écriture impossible).
/// Sauvegarde : <c>done</c>, <c>busy</c> (une sauvegarde est déjà en cours), <c>failed</c>.
/// </param>
/// <param name="AgentApplied">Après un changement de dossier : l'agent a relu ses réglages.</param>
public sealed record BackupOutcome(string Outcome, bool AgentApplied, BackupView Backup);

/// <summary>
/// Routes de la carte « Dossier de sauvegarde » : <c>GET /api/backup</c>, <c>POST /api/backup/folder</c> (le
/// dossier vient du sélecteur du système, jamais du corps de la requête) et <c>POST /api/backup/run</c>.
/// Les réponses ne donnent que le chemin du dossier et des dates ; une erreur n'est jamais citée.
/// </summary>
public static class BackupApi
{
    public const string Path = "/api/backup", FolderPath = Path + "/folder", RunPath = Path + "/run";

    private static int s_running;

    public static void Register(WebRouter router, string dbPath, IFolderPicker picker, IAgentControl agent, Func<long> nowMs)
    {
        ArgumentNullException.ThrowIfNull(router);
        ArgumentException.ThrowIfNullOrEmpty(dbPath);
        ArgumentNullException.ThrowIfNull(picker);
        ArgumentNullException.ThrowIfNull(agent);
        ArgumentNullException.ThrowIfNull(nowMs);
        string settingsPath = SettingsStore.PathFor(dbPath);
        router.Get(Path, () => WebResponse.Json(View(dbPath), WebJson.Default.BackupView));
        router.Post(FolderPath, async _ =>
        {
            string? picked = await picker.PickAsync(View(dbPath).Folder).ConfigureAwait(true);
            string outcome = picked is null ? "cancelled" : SaveFolder(dbPath, settingsPath, picked);
            bool applied = outcome == "saved" && await agent.ReloadAsync().ConfigureAwait(true);
            return Respond(outcome, applied, dbPath);
        });
        router.Post(RunPath, async _ => Respond(await RunAsync(dbPath, nowMs()).ConfigureAwait(true), false, dbPath));
    }

    public static BackupView View(string dbPath)
    {
        string? folder = SettingsStore.Load(SettingsStore.PathFor(dbPath)).Settings.BackupFolder;
        string mirror = BackupMirror.PathFor(dbPath, folder);
        return new BackupView(System.IO.Path.GetDirectoryName(mirror)!, folder is not null, LastRun(mirror));
    }

    /// <summary>
    /// Écrit <paramref name="folder"/> dans <c>settings.json</c>, s'il existe et n'est pas le dossier de la base :
    /// le miroir y prendrait le nom de la base elle-même.
    /// </summary>
    public static string SaveFolder(string dbPath, string settingsPath, string folder)
    {
        if (!System.IO.Path.IsPathFullyQualified(folder) || !Directory.Exists(folder)) return "invalid";
        string full = System.IO.Path.GetFullPath(folder);
        if (string.Equals(BackupMirror.PathFor(dbPath, full), System.IO.Path.GetFullPath(dbPath), StringComparison.OrdinalIgnoreCase))
            return "invalid";
        var load = SettingsStore.Load(settingsPath);
        if (load.Status == SettingsStatus.Invalid) return "unreadable";
        load.Settings.BackupFolder = full;
        try
        {
            SettingsStore.Save(settingsPath, load.Settings);
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            return "failed";
        }
        return "saved";
    }

    /// <summary>
    /// Met le miroir à jour hors du thread de l'interface. L'agent peut écrire dans la source pendant ce temps ;
    /// s'il met à jour le même miroir au même moment, l'une des deux copies échoue et le miroir reste entier.
    /// </summary>
    public static async Task<string> RunAsync(string dbPath, long nowMs)
    {
        if (Interlocked.Exchange(ref s_running, 1) != 0) return "busy";
        try
        {
            string mirror = BackupMirror.PathFor(dbPath, SettingsStore.Load(SettingsStore.PathFor(dbPath)).Settings.BackupFolder);
            await Task.Run(() => BackupMirror.Run(dbPath, mirror, nowMs)).ConfigureAwait(true);
            return "done";
        }
        catch (Exception)
        {
            // Le message pourrait citer un chemin ou un contenu : seul l'échec est rendu.
            return "failed";
        }
        finally
        {
            Volatile.Write(ref s_running, 0);
        }
    }

    private static long? LastRun(string mirror)
    {
        try
        {
            return BackupMirror.LastRunMs(mirror);
        }
        catch (Exception)
        {
            // Miroir illisible ou verrouillé : pas de date plutôt qu'une page en erreur.
            return null;
        }
    }

    private static WebResponse Respond(string outcome, bool applied, string dbPath) =>
        WebResponse.Json(new BackupOutcome(outcome, applied, View(dbPath)), WebJson.Default.BackupOutcome);
}
