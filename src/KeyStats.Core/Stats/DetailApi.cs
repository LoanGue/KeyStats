using System.Runtime.InteropServices;
using System.Security.Cryptography;
using KeyStats.Core.Crypto;
using KeyStats.Core.Input;
using KeyStats.Core.Recording;
using KeyStats.Core.Storage;
using KeyStats.Core.Web;

namespace KeyStats.Core.Stats;

/// <param name="Wpm">Vitesse de frappe de la session (temps actif), en mots de 5 caractères par minute.</param>
public sealed record DetailSession(long Id, long StartMs, int AppId, long DurationMs, double Wpm);

/// <param name="Truncated">Plus de <see cref="DetailApi.MaxSessions"/> sessions : seules les plus récentes sont listées.</param>
public sealed record DetailSessions(string From, string To, IReadOnlyList<DetailSession> Sessions, bool Truncated);

/// <param name="Presses">Appuis dans l'ordre, au plus <see cref="DetailApi.MaxPresses"/>.</param>
/// <param name="Truncated">La session a plus d'appuis que le plafond.</param>
/// <param name="Incomplete">Une partie du brut manque ou n'a pas pu être déchiffrée (époque perdue, bloc altéré).</param>
public sealed record DetailKeys(long Id, long StartMs, long EndMs, IReadOnlyList<DetailPress> Presses, bool Truncated, bool Incomplete);

/// <summary>
/// Routes de la page « Détail à la milliseconde ». <c>/api/detail</c> liste les sessions des filtres, de la plus
/// récente à la plus ancienne. <c>/api/detail/keys?session=</c> rejoue une session touche par touche depuis le brut,
/// déchiffré en mémoire avec la clé de <see cref="KeySession"/>, après déverrouillage seulement (403 sinon). La
/// réponse ne porte que des codes de touche et des durées, jamais de caractère ni de texte.
/// </summary>
public static class DetailApi
{
    public const string Path = "/api/detail";
    public const string KeysPath = Path + "/keys";
    public const int MaxSessions = 1000;
    public const int MaxPresses = 20_000;

    /// <summary>Relâchements lus après la dernière touche de la session.</summary>
    public const long TailMs = 5_000;

    public static void Register(WebRouter router, StatsContext stats, KeySession session)
    {
        ArgumentNullException.ThrowIfNull(router);
        ArgumentNullException.ThrowIfNull(stats);
        ArgumentNullException.ThrowIfNull(session);
        router.Get(Path, q => stats.Respond(q, f => Sessions(stats.Db, f), StatsJson.Default.DetailSessions));
        router.Get(KeysPath, q =>
        {
            if (!q.TryGetId("session", out int? id) || id is null) return WebResponse.BadRequest();
            if (Find(stats.Db, id.Value) is not { } row) return WebResponse.NotFound();
            // Verrouillage vérifié sous le verrou de la session : un Lock() concurrent donne 403, pas une exception.
            var keys = session.TryUse(key => Keys(stats.Db, key, id.Value, row.StartMs, row.EndMs, row.Device));
            return keys is null ? WebResponse.Forbidden() : WebResponse.Json(keys, StatsJson.Default.DetailKeys);
        });
    }

    public static DetailSessions Sessions(KeyStatsDb db, StatsFilter filter)
    {
        ArgumentNullException.ThrowIfNull(db);
        ArgumentNullException.ThrowIfNull(filter);
        var (where, values) = filter.SessionWhere(filter.Range);
        using var cmd = db.Command(
            $"SELECT id, start_ms, app_id, end_ms - start_ms, gross_cpm FROM session WHERE {where} ORDER BY start_ms DESC, id DESC LIMIT {MaxSessions + 1}",
            values);
        using var r = cmd.ExecuteReader();
        var list = new List<DetailSession>();
        while (r.Read())
            list.Add(new DetailSession(r.GetInt64(0), r.GetInt64(1), r.GetInt32(2), r.GetInt64(3), r.GetDouble(4) / 5));
        bool truncated = list.Count > MaxSessions;
        if (truncated) list.RemoveAt(MaxSessions);
        return new DetailSessions(StatsFilter.Iso(filter.Range.FromDay), StatsFilter.Iso(filter.Range.ToDay), list, truncated);
    }

    /// <summary>Appuis de la session <paramref name="id"/> sur le clavier <paramref name="device"/>.</summary>
    public static DetailKeys Keys(KeyStatsDb db, MasterKey key, long id, long startMs, long endMs, int device)
    {
        ArgumentNullException.ThrowIfNull(db);
        ArgumentNullException.ThrowIfNull(key);
        using var reader = new RawArchiveReader(db, key);
        bool incomplete = false;
        // Un bloc fini avant la session n'a rien à lui apprendre : ses frappes ne sont pas déchiffrées.
        var blocks = db.GetRawBlocks(startMs - HistoryReplay.BlockMarginMs, endMs + TailMs).Where(b => b.EndMs >= startMs).ToList();
        // Dimensionnée d'avance : une liste qui grandit laisserait des copies des frappes dans ses anciens tampons.
        var events = new List<InputEvent>(blocks.Sum(b => b.EventCount));
        try
        {
            foreach (var block in blocks)
            {
                try
                {
                    var read = reader.ReadBlock(block);
                    events.AddRange(read);
                    Array.Clear(read);
                }
                catch (Exception e) when (e is CryptographicException or InvalidDataException)
                {
                    incomplete = true;
                }
            }
            var (presses, truncated) = DetailPresses.Extract(events, device, startMs, endMs, MaxPresses);
            // Une session sans aucun appui dans le brut : enregistrée avant le chiffrement, ou brut perdu.
            incomplete |= presses.Count == 0;
            return new DetailKeys(id, startMs, endMs, presses, truncated, incomplete);
        }
        finally
        {
            // Les frappes déchiffrées ne restent pas en mémoire, d'où elles pourraient finir dans le fichier d'échange.
            CollectionsMarshal.AsSpan(events).Clear();
        }
    }

    private static (long StartMs, long EndMs, int Device)? Find(KeyStatsDb db, long id)
    {
        using var cmd = db.Command("SELECT start_ms, end_ms, device_id FROM session WHERE id = $id", id);
        using var r = cmd.ExecuteReader();
        return r.Read() ? (r.GetInt64(0), r.GetInt64(1), r.GetInt32(2)) : null;
    }
}
