using System.Globalization;
using KeyStats.Core.Analysis;
using KeyStats.Core.Input;
using KeyStats.Core.Storage;

namespace KeyStats.Core.Stats;

/// <summary>Fichiers de l'export CSV, à cocher dans la page Réglages.</summary>
[Flags]
public enum CsvTables
{
    None = 0,
    Days = 1,
    Sessions = 2,
    Keys = 4,
}

/// <summary>
/// Export CSV des statistiques en clair : par jour et par session (table <c>session</c>, avec les filtres de
/// l'en-tête), par touche (<c>key_minute</c>, filtre clavier seul). Rien ne vient du brut ni des titres de
/// fenêtre : seuls des nombres, des noms d'exécutables et des noms de claviers sont écrits.
/// </summary>
public static class CsvExport
{
    /// <summary>
    /// Écrit les fichiers de <paramref name="tables"/> dans <paramref name="folder"/>, chacun par un fichier
    /// temporaire renommé ensuite, et rend leurs noms. Un export de la même période remplace le précédent.
    /// </summary>
    public static IReadOnlyList<string> Write(KeyStatsDb db, LocalDayClock clock, StatsFilter filter, CsvTables tables, string folder)
    {
        ArgumentNullException.ThrowIfNull(db);
        ArgumentNullException.ThrowIfNull(clock);
        ArgumentNullException.ThrowIfNull(filter);
        string suffix = $"{StatsFilter.Iso(filter.Range.FromDay)}_{StatsFilter.Iso(filter.Range.ToDay)}.csv";
        var written = new List<string>();
        if (tables.HasFlag(CsvTables.Days)) written.Add(Save(folder, "keystats-jours-" + suffix, Days(db, filter)));
        if (tables.HasFlag(CsvTables.Sessions)) written.Add(Save(folder, "keystats-sessions-" + suffix, Sessions(db, clock, filter)));
        if (tables.HasFlag(CsvTables.Keys)) written.Add(Save(folder, "keystats-touches-" + suffix, Keys(db, clock, filter)));
        return written;
    }

    /// <summary>Une ligne par jour qui a des sessions ; les vitesses et la précision se calculent sur les sommes.</summary>
    public static CsvTable Days(KeyStatsDb db, StatsFilter filter)
    {
        ArgumentNullException.ThrowIfNull(db);
        ArgumentNullException.ThrowIfNull(filter);
        var csv = new CsvTable("jour", "sessions", "durée (min)", "écriture active (min)", "touches (en session)", "caractères",
            "vitesse réelle (mots/min)", "précision (%)");
        var (where, values) = filter.SessionWhere(filter.Range);
        using var cmd = db.Command(
            "SELECT day, COUNT(*), TOTAL(end_ms - start_ms), TOTAL(active_ms), TOTAL(keys), TOTAL(chars), "
            + $"TOTAL(CASE WHEN kind = {(int)SessionKind.Prose} THEN typed END), TOTAL(CASE WHEN kind = {(int)SessionKind.Prose} THEN typos END) "
            + $"FROM session WHERE {where} GROUP BY day ORDER BY day", values);
        using var r = cmd.ExecuteReader();
        while (r.Read())
        {
            double duration = r.GetDouble(2), chars = r.GetDouble(5);
            csv.Text(StatsFilter.Iso(r.GetInt32(0))).Number(r.GetInt64(1)).Number(duration / 60000)
                .Number(r.GetDouble(3) / 60000).Number((long)r.GetDouble(4)).Number((long)chars)
                .Number(duration > 0 ? chars / 5 / (duration / 60000) : null).Number(Accuracy(r.GetDouble(6), r.GetDouble(7)));
        }
        return csv;
    }

    /// <summary>Une ligne par session, dans l'ordre chronologique, heures locales du fuseau de <paramref name="clock"/>.</summary>
    public static CsvTable Sessions(KeyStatsDb db, LocalDayClock clock, StatsFilter filter)
    {
        ArgumentNullException.ThrowIfNull(db);
        ArgumentNullException.ThrowIfNull(clock);
        ArgumentNullException.ThrowIfNull(filter);
        var apps = db.GetApps();
        var devices = Devices(db);
        var csv = new CsvTable("début", "fin", "application", "nature", "clavier", "écriture active (s)", "touches",
            "caractères", "tapés", "effacés", "vitesse réelle (mots/min)", "vitesse de frappe (mots/min)",
            "pointe (mots/min)", "fautes", "corrections", "précision (%)");
        var (where, values) = filter.SessionWhere(filter.Range);
        using var cmd = db.Command(
            "SELECT start_ms, end_ms, app_id, kind, device_id, active_ms, keys, chars, typed, deleted, real_cpm, gross_cpm, "
            + $"peak_cpm, typos, corrections FROM session WHERE {where} ORDER BY start_ms, id", values);
        using var r = cmd.ExecuteReader();
        while (r.Read())
        {
            var kind = (SessionKind)r.GetInt32(3);
            long typed = r.GetInt64(8);
            csv.Text(Local(clock, r.GetInt64(0))).Text(Local(clock, r.GetInt64(1)))
                .Text(apps.GetValueOrDefault(r.GetInt32(2))).Text(KindName(kind)).Text(devices.GetValueOrDefault(r.GetInt32(4)))
                .Number(r.GetInt64(5) / 1000.0).Number(r.GetInt64(6)).Number(r.GetInt64(7)).Number(typed).Number(r.GetInt64(9))
                .Number(r.GetDouble(10) / 5).Number(r.GetDouble(11) / 5).Number(r.IsDBNull(12) ? null : r.GetDouble(12) / 5)
                .Number(r.GetInt64(13)).Number(r.GetInt64(14))
                .Number(kind == SessionKind.Prose ? Accuracy(typed, r.GetInt64(13)) : null);
        }
        return csv;
    }

    /// <summary>Appuis par touche sur la période, toutes frappes comptées hors applis exclues.</summary>
    public static CsvTable Keys(KeyStatsDb db, LocalDayClock clock, StatsFilter filter)
    {
        ArgumentNullException.ThrowIfNull(db);
        ArgumentNullException.ThrowIfNull(clock);
        ArgumentNullException.ThrowIfNull(filter);
        var csv = new CsvTable("touche", "code", "appuis", "répétitions");
        string where = KeyboardApi.KeyMinuteWhere(clock, filter, filter.Range, out object?[] values);
        using var cmd = db.Command(
            $"SELECT key, TOTAL(presses), TOTAL(repeats) FROM key_minute WHERE {where} GROUP BY key ORDER BY TOTAL(presses) DESC, key", values);
        using var r = cmd.ExecuteReader();
        while (r.Read())
        {
            var key = (ushort)r.GetInt32(0);
            csv.Text(KeyCode.Name(key)).Text("0x" + key.ToString("X3", CultureInfo.InvariantCulture))
                .Number((long)r.GetDouble(1)).Number((long)r.GetDouble(2));
        }
        return csv;
    }

    private static double? Accuracy(double typed, double typos) => typed > 0 ? 100 * (1 - typos / typed) : null;

    private static string KindName(SessionKind kind) => kind switch
    {
        SessionKind.Prose => "prose",
        SessionKind.Mixed => "mixte",
        SessionKind.Code => "code",
        _ => "",
    };

    private static string Local(LocalDayClock clock, long ms) =>
        TimeZoneInfo.ConvertTimeFromUtc(DateTimeOffset.FromUnixTimeMilliseconds(ms).UtcDateTime, clock.Zone)
            .ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture);

    private static Dictionary<int, string> Devices(KeyStatsDb db)
    {
        var list = new Dictionary<int, string>();
        using var cmd = db.Command("SELECT id, label FROM device");
        using var r = cmd.ExecuteReader();
        while (r.Read()) list[r.GetInt32(0)] = r.GetString(1);
        return list;
    }

    private static string Save(string folder, string name, CsvTable csv)
    {
        string path = Path.Combine(folder, name), temp = path + ".tmp";
        try
        {
            File.WriteAllBytes(temp, csv.ToBytes());
            File.Move(temp, path, overwrite: true);
        }
        finally
        {
            File.Delete(temp);
        }
        return name;
    }
}
