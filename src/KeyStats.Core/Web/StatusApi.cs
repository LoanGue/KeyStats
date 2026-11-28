using System.Text.Json.Serialization;
using KeyStats.Core.Recording;
using KeyStats.Core.Storage;

namespace KeyStats.Core.Web;

/// <param name="Setup">Clé publique maîtresse en place : l'agent chiffre le brut et les titres.</param>
/// <param name="AgentRunning">Un agent tourne sur cette base.</param>
public sealed record DashboardStatus(bool Setup, bool AgentRunning);

/// <summary>Route <c>/api/status</c> : ce que la page doit savoir avant d'afficher quoi que ce soit.</summary>
public static class StatusApi
{
    public const string Path = "/api/status";

    public static DashboardStatus Read(KeyStatsDb db, bool agentRunning)
    {
        ArgumentNullException.ThrowIfNull(db);
        return new DashboardStatus(db.GetMeta(MetaKeys.MasterPublicKey) is { Length: > 0 }, agentRunning);
    }

    public static WebResponse Respond(KeyStatsDb db, bool agentRunning) =>
        WebResponse.Json(Read(db, agentRunning), WebJson.Default.DashboardStatus);
}

[JsonSourceGenerationOptions(PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase)]
[JsonSerializable(typeof(DashboardStatus))]
[JsonSerializable(typeof(KeyState))]
[JsonSerializable(typeof(UnlockRequest))]
[JsonSerializable(typeof(SetupRequest))]
[JsonSerializable(typeof(SetupResult))]
[JsonSerializable(typeof(PassphraseChange))]
[JsonSerializable(typeof(KeyChangeResult))]
[JsonSerializable(typeof(SettingsView))]
[JsonSerializable(typeof(LiveCounterView))]
[JsonSerializable(typeof(LiveCounterPatch))]
[JsonSerializable(typeof(SettingsPatch))]
[JsonSerializable(typeof(SettingsSaved))]
[JsonSerializable(typeof(BackupView))]
[JsonSerializable(typeof(BackupOutcome))]
[JsonSerializable(typeof(ExportRequest))]
[JsonSerializable(typeof(ExportOutcome))]
[JsonSerializable(typeof(RecomputeRequest))]
[JsonSerializable(typeof(RecomputeStart))]
[JsonSerializable(typeof(RecomputeView))]
public sealed partial class WebJson : JsonSerializerContext;
