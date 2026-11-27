using System.Security.Cryptography;
using KeyStats.Core.Crypto;
using KeyStats.Core.Storage;
using KeyStats.Core.Web;

namespace KeyStats.Core.Stats;

/// <param name="Title">Titre de fenêtre déchiffré, en mémoire du tableau de bord seulement.</param>
public sealed record ActivityTitle(string Title, long WritingMs);

/// <param name="App">Appli dont les titres sont détaillés.</param>
/// <param name="Titles">Les <see cref="ActivityTitles.MaxTitles"/> titres les plus utilisés de la période, du plus au moins utilisé.</param>
/// <param name="OtherMs">Temps des autres titres, y compris ceux qui n'ont pas pu être déchiffrés.</param>
/// <param name="UntitledMs">Temps des sessions sans titre de fenêtre enregistré.</param>
public sealed record ActivityTitleList(int App, IReadOnlyList<ActivityTitle> Titles, long OtherMs, long UntitledMs);

/// <summary>
/// Route <c>/api/activity/titles?target=</c> : temps d'écriture par titre de fenêtre d'une appli, après déverrouillage
/// seulement (403 sinon). Les titres ne sont déchiffrés qu'ici, en mémoire, et ne vont que dans la réponse
/// (<c>Cache-Control: no-store</c>) : ni journal, ni fichier, ni message d'exception.
/// </summary>
public static class ActivityTitles
{
    public const string Path = ActivityApi.Path + "/titles";
    public const int MaxTitles = 10;

    internal static void Register(WebRouter router, StatsContext stats, KeySession session) =>
        router.Get(Path, q =>
        {
            if (!q.TryGetId("target", out int? target) || target is null) return WebResponse.BadRequest();
            if (!session.IsUnlocked) return WebResponse.Forbidden();
            return stats.Respond(q, f => session.Use(key => Read(stats.Db, key, f, target.Value)), StatsJson.Default.ActivityTitleList);
        });

    public static ActivityTitleList Read(KeyStatsDb db, MasterKey key, StatsFilter filter, int appId)
    {
        ArgumentNullException.ThrowIfNull(db);
        ArgumentNullException.ThrowIfNull(key);
        ArgumentNullException.ThrowIfNull(filter);
        var (where, values) = filter.SessionWhere(filter.Range);
        var rows = new List<(long? Id, long Ms)>();
        using (var cmd = db.Command($"SELECT title_id, TOTAL(active_ms) FROM session WHERE {where} AND app_id = $target "
            + "GROUP BY title_id ORDER BY 2 DESC, 1", [.. values, appId]))
        using (var r = cmd.ExecuteReader())
        {
            while (r.Read()) rows.Add((r.IsDBNull(0) ? null : r.GetInt64(0), (long)r.GetDouble(1)));
        }
        using var reader = new RawArchiveReader(db, key);
        var titles = new List<ActivityTitle>();
        long other = 0, untitled = 0;
        foreach (var (id, ms) in rows)
        {
            if (id is null) untitled += ms;
            else if (titles.Count < MaxTitles && Decrypt(reader, id.Value) is { } title) titles.Add(new ActivityTitle(title, ms));
            else other += ms;
        }
        return new ActivityTitleList(appId, titles, other, untitled);
    }

    /// <summary>Null pour un titre absent ou illisible (époque perdue, ligne altérée) : il compte avec les autres.</summary>
    private static string? Decrypt(RawArchiveReader reader, long id)
    {
        try
        {
            return reader.ReadTitle(id);
        }
        catch (Exception e) when (e is CryptographicException or InvalidDataException)
        {
            return null;
        }
    }
}
