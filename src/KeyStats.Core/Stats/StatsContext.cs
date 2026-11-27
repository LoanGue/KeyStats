using System.Text.Json.Serialization;
using System.Text.Json.Serialization.Metadata;
using KeyStats.Core.Analysis;
using KeyStats.Core.Storage;
using KeyStats.Core.Web;

namespace KeyStats.Core.Stats;

/// <summary>
/// Ce que partagent les routes de statistiques : la base, l'heure et le fuseau qui donnent
/// « aujourd'hui », et la lecture des filtres de l'en-tête.
/// </summary>
public sealed class StatsContext(KeyStatsDb db, Func<long> nowMs, TimeZoneInfo? zone = null)
{
    public KeyStatsDb Db => db;

    /// <summary>
    /// Fuseau relu à chaque requête : un changement d'heure ou de fuseau est pris sans redémarrer, pourvu que
    /// l'hôte vide le cache de <see cref="TimeZoneInfo"/> sur <c>WM_TIMECHANGE</c> (le tableau de bord le fait).
    /// </summary>
    public LocalDayClock Clock => new(zone ?? TimeZoneInfo.Local);

    /// <summary>Jour local en cours, au fuseau de <see cref="Clock"/>.</summary>
    public int Today => Clock.DayKey(nowMs());

    /// <summary>Premier jour qui a des statistiques ; null pour une base vide.</summary>
    public int? FirstDay => db.Scalar("SELECT MIN(day) FROM day_metric") is long day ? (int)day : null;

    public StatsFilter? Filter(WebQuery query) => StatsFilter.Parse(query, Today, FirstDay);

    /// <summary>Réponse JSON de <paramref name="read"/> ; 400 si les filtres sont invalides.</summary>
    public WebResponse Respond<T>(WebQuery query, Func<StatsFilter, T> read, JsonTypeInfo<T> type)
    {
        ArgumentNullException.ThrowIfNull(read);
        return Filter(query) is { } filter ? WebResponse.Json(read(filter), type) : WebResponse.BadRequest();
    }
}

/// <summary>Identifiant et nom affichable (appli, clavier).</summary>
public sealed record NamedId(int Id, string Name);

/// <param name="Apps">Applis qui ont des frappes enregistrées, exclues comprises (leurs touches sont comptées).</param>
/// <param name="Devices">Claviers vus par l'agent.</param>
/// <param name="FirstDay">Premier jour enregistré (aaaa-mm-jj), null pour une base vide.</param>
/// <param name="Today">Aujourd'hui (aaaa-mm-jj) dans le fuseau local.</param>
public sealed record FilterOptions(IReadOnlyList<NamedId> Apps, IReadOnlyList<NamedId> Devices, string? FirstDay, string Today);

/// <summary>Route <c>/api/filters</c> : les choix des listes de l'en-tête.</summary>
public static class FiltersApi
{
    public const string Path = "/api/filters";

    /// <summary>Valeur de <c>device.kind</c> pour un clavier (2 : souris ou pavé tactile).</summary>
    private const int KeyboardKind = 1;

    public static void Register(WebRouter router, StatsContext stats)
    {
        ArgumentNullException.ThrowIfNull(router);
        ArgumentNullException.ThrowIfNull(stats);
        router.Get(Path, () => WebResponse.Json(Read(stats), StatsJson.Default.FilterOptions));
    }

    public static FilterOptions Read(StatsContext stats)
    {
        ArgumentNullException.ThrowIfNull(stats);
        var apps = List(stats.Db, "SELECT id, exe FROM app WHERE id IN (SELECT DISTINCT app_id FROM app_hour) ORDER BY exe");
        var devices = List(stats.Db, $"SELECT id, label FROM device WHERE kind = {KeyboardKind} ORDER BY label, id");
        return new FilterOptions(apps, devices, stats.FirstDay is { } first ? StatsFilter.Iso(first) : null, StatsFilter.Iso(stats.Today));
    }

    private static List<NamedId> List(KeyStatsDb db, string sql)
    {
        var list = new List<NamedId>();
        using var cmd = db.Command(sql);
        using var r = cmd.ExecuteReader();
        while (r.Read()) list.Add(new NamedId(r.GetInt32(0), r.GetString(1)));
        return list;
    }
}

[JsonSourceGenerationOptions(PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase)]
[JsonSerializable(typeof(FilterOptions))]
[JsonSerializable(typeof(Overview))]
[JsonSerializable(typeof(Speed))]
[JsonSerializable(typeof(Typos))]
[JsonSerializable(typeof(Keyboard))]
public sealed partial class StatsJson : JsonSerializerContext;
