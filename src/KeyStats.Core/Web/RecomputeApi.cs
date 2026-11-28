using System.Globalization;
using System.Text.Json;
using KeyStats.Core.Analysis;
using KeyStats.Core.Crypto;
using KeyStats.Core.Recording;
using KeyStats.Core.Settings;
using KeyStats.Core.Stats;

namespace KeyStats.Core.Web;

/// <param name="From">Premier jour (aaaa-mm-jj).</param>
/// <param name="To">Dernier jour, au plus hier : l'agent écrit encore aujourd'hui, avec une session peut-être ouverte.</param>
public sealed record RecomputeRequest(string? From, string? To);

/// <param name="Outcome">
/// <c>started</c>, <c>busy</c> (un recalcul tourne déjà), <c>locked</c> (déverrouillage nécessaire) ou
/// <c>unreadable</c> (<c>settings.json</c> refusé : les seuils actuels sont inconnus).
/// </param>
public sealed record RecomputeStart(string Outcome, RecomputeView Job);

/// <summary>
/// Routes de la carte « Recalcul de l'historique » : <c>POST /api/recompute</c> lance le recalcul d'une
/// période avec les seuils de <c>settings.json</c>, <c>GET /api/recompute</c> en donne l'avancement et
/// <c>POST /api/recompute/cancel</c> l'arrête après le jour en cours. Les réponses ne donnent que des dates et des nombres.
/// </summary>
public static class RecomputeApi
{
    public const string Path = "/api/recompute", CancelPath = Path + "/cancel";

    public static void Register(WebRouter router, StatsContext stats, string dbPath, KeySession session, RecomputeJob job)
    {
        ArgumentNullException.ThrowIfNull(router);
        ArgumentNullException.ThrowIfNull(stats);
        ArgumentException.ThrowIfNullOrEmpty(dbPath);
        ArgumentNullException.ThrowIfNull(session);
        ArgumentNullException.ThrowIfNull(job);
        string settingsPath = SettingsStore.PathFor(dbPath);
        router.Get(Path, () => WebResponse.Json(job.View(), WebJson.Default.RecomputeView));
        router.Post(Path, body =>
        {
            if (Range(body, stats.Today) is not var (from, to)) return Task.FromResult(WebResponse.BadRequest());
            var load = SettingsStore.Load(settingsPath);
            string outcome = load.Status == SettingsStatus.Invalid ? "unreadable"
                : !session.IsUnlocked ? "locked"
                : job.TryStart(from, to, stats.Clock, load.Settings.Analysis) ? "started" : "busy";
            return Task.FromResult(WebResponse.Json(new RecomputeStart(outcome, job.View()), WebJson.Default.RecomputeStart));
        });
        router.Post(CancelPath, _ =>
        {
            job.Cancel();
            return Task.FromResult(WebResponse.Json(job.View(), WebJson.Default.RecomputeView));
        });
    }

    /// <summary>Jours demandés ; null si le corps ou une date est invalide, ou si la période va au-delà d'hier.</summary>
    public static (int From, int To)? Range(byte[] body, int today)
    {
        ArgumentNullException.ThrowIfNull(body);
        RecomputeRequest? request;
        try
        {
            request = JsonSerializer.Deserialize(body, WebJson.Default.RecomputeRequest);
        }
        catch (JsonException)
        {
            return null;
        }
        if (Day(request?.From) is not { } from || Day(request?.To) is not { } to || from > to || to >= today) return null;
        return (from, to);
    }

    private static int? Day(string? iso) =>
        DateTime.TryParseExact(iso, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var date)
            ? LocalDayClock.KeyOf(date) : null;
}
