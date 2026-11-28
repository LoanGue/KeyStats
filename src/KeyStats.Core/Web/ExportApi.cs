using System.Globalization;
using System.Text.Json;
using KeyStats.Core.Analysis;
using KeyStats.Core.Stats;
using KeyStats.Core.Storage;

namespace KeyStats.Core.Web;

/// <param name="Filters">Filtres de l'en-tête, comme dans l'adresse des routes de statistiques (<c>period=all</c> pour tout l'historique).</param>
/// <param name="From">Premier jour d'une période personnalisée (aaaa-mm-jj), avec <paramref name="To"/> ; remplace celle des filtres.</param>
/// <param name="To">Dernier jour d'une période personnalisée, au plus aujourd'hui.</param>
public sealed record ExportRequest(string? Filters, string? From, string? To, bool Days, bool Sessions, bool Keys);

/// <param name="Outcome"><c>done</c>, <c>cancelled</c> (fenêtre fermée), <c>invalid</c> (dossier inutilisable) ou <c>failed</c>.</param>
/// <param name="Folder">Dossier choisi, après <c>done</c>.</param>
/// <param name="Files">Noms des fichiers écrits, après <c>done</c>.</param>
public sealed record ExportOutcome(string Outcome, string? Folder, IReadOnlyList<string> Files);

/// <summary>
/// <c>POST /api/export</c> : export CSV des statistiques en clair dans un dossier choisi dans la fenêtre du
/// système, jamais donné par la requête. La requête ne choisit que la période, les filtres et les fichiers.
/// </summary>
public static class ExportApi
{
    public const string Path = "/api/export";

    /// <param name="initialFolder">Dossier proposé à l'ouverture de la fenêtre (Documents).</param>
    public static void Register(WebRouter router, StatsContext stats, string dbPath, IFolderPicker picker, string initialFolder)
    {
        ArgumentNullException.ThrowIfNull(router);
        ArgumentNullException.ThrowIfNull(stats);
        ArgumentException.ThrowIfNullOrEmpty(dbPath);
        ArgumentNullException.ThrowIfNull(picker);
        router.Post(Path, async body =>
        {
            if (Parse(body) is not { } request || Plan(stats, request) is not { } plan) return WebResponse.BadRequest();
            string? folder = await picker.PickAsync(initialFolder, "Dossier de l'export CSV").ConfigureAwait(true);
            ExportOutcome outcome = folder is null ? new("cancelled", null, [])
                : !System.IO.Path.IsPathFullyQualified(folder) || !Directory.Exists(folder) ? new("invalid", null, [])
                : await WriteAsync(dbPath, stats.Clock, plan.Filter, plan.Tables, folder).ConfigureAwait(true);
            return WebResponse.Json(outcome, WebJson.Default.ExportOutcome);
        });
    }

    /// <summary>Filtre et fichiers demandés ; null si un filtre, une date ou le choix des fichiers est invalide.</summary>
    public static (StatsFilter Filter, CsvTables Tables)? Plan(StatsContext stats, ExportRequest request)
    {
        ArgumentNullException.ThrowIfNull(stats);
        ArgumentNullException.ThrowIfNull(request);
        var tables = (request.Days ? CsvTables.Days : 0) | (request.Sessions ? CsvTables.Sessions : 0) | (request.Keys ? CsvTables.Keys : 0);
        if (tables == CsvTables.None || stats.Filter(WebQuery.Parse(request.Filters)) is not { } filter) return null;
        if (request.From is null && request.To is null) return (filter, tables);
        if (Day(request.From) is not { } from || Day(request.To) is not { } to || from > to || to > stats.Today) return null;
        return (filter with { Range = new DayRange(from, to), Previous = null }, tables);
    }

    /// <summary>Lecture sur une connexion à part, hors du thread de l'interface ; une erreur n'est jamais citée.</summary>
    private static async Task<ExportOutcome> WriteAsync(string dbPath, LocalDayClock clock, StatsFilter filter, CsvTables tables, string folder)
    {
        try
        {
            var files = await Task.Run(() =>
            {
                using var db = KeyStatsDb.Open(dbPath, readOnly: true);
                return CsvExport.Write(db, clock, filter, tables, folder);
            }).ConfigureAwait(true);
            return new ExportOutcome("done", System.IO.Path.GetFullPath(folder), files);
        }
        catch (Exception)
        {
            // Le message pourrait citer un chemin ou un contenu : seul l'échec est rendu.
            return new ExportOutcome("failed", null, []);
        }
    }

    private static int? Day(string? iso) =>
        DateTime.TryParseExact(iso, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var date)
            ? LocalDayClock.KeyOf(date) : null;

    private static ExportRequest? Parse(byte[] body)
    {
        try
        {
            return JsonSerializer.Deserialize(body, WebJson.Default.ExportRequest);
        }
        catch (JsonException)
        {
            return null;
        }
    }
}
