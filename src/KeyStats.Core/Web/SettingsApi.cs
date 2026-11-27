using System.Text.Json;
using KeyStats.Core.Analysis;
using KeyStats.Core.Settings;

namespace KeyStats.Core.Web;

/// <summary>Ce que le tableau de bord demande à l'agent après l'écriture de <c>settings.json</c>.</summary>
public interface IAgentControl
{
    /// <summary>Commande <c>reload</c> ; vrai si l'agent a répondu <c>ok</c>.</summary>
    Task<bool> ReloadAsync();

    /// <summary>Arrête l'agent qui tourne et le relance ; vrai si un agent tourne ensuite.</summary>
    Task<bool> RestartAsync();
}

/// <summary>Seuils de la page Réglages, en millisecondes.</summary>
public sealed record Thresholds(int ActiveGapMs, int SessionGapMs);

/// <param name="Readable">Faux quand <c>settings.json</c> est refusé : la page ne l'écrase pas.</param>
/// <param name="ExcludedApps">Liste en vigueur, la liste intégrée tant que le fichier n'en donne pas.</param>
public sealed record SettingsView(bool Readable, Thresholds Thresholds, Thresholds Defaults,
    IReadOnlyList<string> ExcludedApps, LiveCounterView LiveCounter);

/// <param name="Position">Une valeur de <see cref="LiveCorner.All"/>.</param>
/// <param name="Opacity">En pour cent.</param>
public sealed record LiveCounterView(bool Enabled, string Position, int Opacity);

/// <summary>Modification partielle : une valeur absente (null) reste telle quelle.</summary>
public sealed record SettingsPatch(int? ActiveGapMs, int? SessionGapMs, IReadOnlyList<string>? ExcludedApps,
    LiveCounterPatch? LiveCounter);

public sealed record LiveCounterPatch(bool? Enabled, string? Position, int? Opacity);

/// <param name="Outcome">
/// <c>saved</c>, <c>invalid</c> (valeur hors bornes, rien d'écrit), <c>unreadable</c> (fichier refusé,
/// laissé tel quel) ou <c>failed</c> (écriture impossible).
/// </param>
/// <param name="AgentApplied">Un agent a pris les nouveaux réglages (relecture, ou redémarrage pour les seuils).</param>
/// <param name="Restarted">L'agent a été arrêté et relancé, parce qu'un seuil a changé.</param>
public sealed record SettingsSaved(string Outcome, bool AgentApplied, bool Restarted, SettingsView Settings);

/// <summary>Valeurs de <see cref="SettingsSaved.Outcome"/>, lues telles quelles par la page Réglages.</summary>
public static class SettingsOutcome
{
    public const string Saved = "saved";
    public const string Invalid = "invalid";
    public const string Unreadable = "unreadable";
    public const string Failed = "failed";
}

/// <summary>
/// <c>GET /api/settings</c> et <c>POST /api/settings</c> : lecture et modification de <c>settings.json</c>.
/// Seuls des seuils, des noms d'exécutables et des interrupteurs passent par ces routes.
/// </summary>
public static class SettingsApi
{
    public const string Path = "/api/settings";

    /// <summary>
    /// Plus d'applications exclues que n'en tient une liste écrite à la main ; une liste pleine de noms
    /// courants tient dans le corps de requête accepté (<see cref="WebRouter.MaxBody"/>).
    /// </summary>
    public const int MaxExcludedApps = 64;

    public static void Register(WebRouter router, string settingsPath, IAgentControl agent)
    {
        ArgumentNullException.ThrowIfNull(router);
        ArgumentException.ThrowIfNullOrEmpty(settingsPath);
        ArgumentNullException.ThrowIfNull(agent);
        router.Get(Path, () => WebResponse.Json(View(SettingsStore.Load(settingsPath)), WebJson.Default.SettingsView));
        router.Post(Path, async body =>
        {
            if (Parse(body) is not { } patch) return WebResponse.BadRequest();
            var (outcome, restart, view) = Save(settingsPath, patch);
            bool applied = outcome == SettingsOutcome.Saved
                // Reprise sur le thread de l'interface, qui pose la réponse.
                && await (restart ? agent.RestartAsync() : agent.ReloadAsync()).ConfigureAwait(true);
            return WebResponse.Json(new SettingsSaved(outcome, applied, applied && restart, view), WebJson.Default.SettingsSaved);
        });
    }

    public static SettingsView View(SettingsLoad load)
    {
        var s = load.Settings;
        var d = AnalysisSettings.Default;
        return new SettingsView(load.Status != SettingsStatus.Invalid,
            new Thresholds(s.Analysis.ActiveGapMs, s.Analysis.SessionGapMs), new Thresholds(d.ActiveGapMs, d.SessionGapMs),
            s.ExcludedApps ?? Focus.ExclusionList.Defaults,
            new LiveCounterView(s.LiveCounter.Enabled, s.LiveCounter.Position, s.LiveCounter.Opacity));
    }

    /// <summary>
    /// Applique <paramref name="patch"/> au fichier tel qu'il est sur le disque, puis le réécrit depuis le
    /// modèle : les clés connues gardent leur valeur, les clés inconnues et les commentaires disparaissent.
    /// <c>Restart</c> : les seuils ont changé, l'agent ne les lit qu'au démarrage.
    /// </summary>
    public static (string Outcome, bool Restart, SettingsView View) Save(string settingsPath, SettingsPatch patch)
    {
        ArgumentNullException.ThrowIfNull(patch);
        var load = SettingsStore.Load(settingsPath);
        if (load.Status == SettingsStatus.Invalid) return (SettingsOutcome.Unreadable, false, View(load));
        IReadOnlyList<string>? apps = null;
        if (patch.ExcludedApps is not null && (apps = CleanApps(patch.ExcludedApps)) is null) return (SettingsOutcome.Invalid, false, View(load));
        var s = load.Settings;
        var a = s.Analysis;
        bool restart = (patch.ActiveGapMs is { } active && active != a.ActiveGapMs)
            | (patch.SessionGapMs is { } session && session != a.SessionGapMs);
        a.ActiveGapMs = patch.ActiveGapMs ?? a.ActiveGapMs;
        a.SessionGapMs = patch.SessionGapMs ?? a.SessionGapMs;
        if (patch.LiveCounter is { } live)
        {
            s.LiveCounter.Enabled = live.Enabled ?? s.LiveCounter.Enabled;
            s.LiveCounter.Position = live.Position ?? s.LiveCounter.Position;
            s.LiveCounter.Opacity = live.Opacity ?? s.LiveCounter.Opacity;
        }
        if (apps is not null) s.ExcludedApps = apps;
        if (!SettingsStore.IsValid(s)) return (SettingsOutcome.Invalid, false, View(SettingsStore.Load(settingsPath)));
        try
        {
            SettingsStore.Save(settingsPath, s);
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            return (SettingsOutcome.Failed, false, View(SettingsStore.Load(settingsPath)));
        }
        return (SettingsOutcome.Saved, restart, View(new SettingsLoad(s, SettingsStatus.Loaded)));
    }

    /// <summary>
    /// Noms de fichiers <c>.exe</c> seuls, sans chemin ni caractère interdit, sans doublon (casse ignorée,
    /// comme à la comparaison). Null si un nom est refusé ou si la liste est trop longue.
    /// </summary>
    public static IReadOnlyList<string>? CleanApps(IReadOnlyList<string> apps)
    {
        ArgumentNullException.ThrowIfNull(apps);
        if (apps.Count > MaxExcludedApps) return null;
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var clean = new List<string>(apps.Count);
        foreach (string? raw in apps)
        {
            string name = raw?.Trim() ?? "";
            if (!IsExeName(name)) return null;
            if (seen.Add(name)) clean.Add(name);
        }
        return clean;
    }

    private static bool IsExeName(string name) =>
        name.Length is > 4 and <= 255
        && name.EndsWith(".exe", StringComparison.OrdinalIgnoreCase)
        && name.IndexOfAny(System.IO.Path.GetInvalidFileNameChars()) < 0
        // GetInvalidFileNameChars ne couvre pas tous les systèmes de la même façon : les séparateurs sont refusés ici.
        && name.IndexOfAny(['\\', '/', ':']) < 0;

    private static SettingsPatch? Parse(byte[] body)
    {
        try
        {
            return JsonSerializer.Deserialize(body, WebJson.Default.SettingsPatch);
        }
        catch (JsonException)
        {
            return null;
        }
    }
}
