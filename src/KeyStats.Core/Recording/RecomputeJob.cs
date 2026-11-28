using System.Globalization;
using KeyStats.Core.Analysis;
using KeyStats.Core.Crypto;
using KeyStats.Core.Storage;

namespace KeyStats.Core.Recording;

/// <param name="State"><c>idle</c>, <c>running</c>, <c>done</c>, <c>cancelled</c>, <c>locked</c> (session verrouillée en cours de route) ou <c>failed</c>.</param>
/// <param name="Day">Jour en cours de traitement (aaaa-mm-jj), pendant <c>running</c>.</param>
/// <param name="Done">Jours traités, recalculés ou gardés tels quels.</param>
/// <param name="Total">Jours de la période demandée.</param>
/// <param name="RemainingMs">Temps restant estimé, après le premier jour traité.</param>
/// <param name="Skipped">Jours gardés tels quels : brut chiffré absent ou incomplet, ou sessions reliant trop de jours (<see cref="DayReplay"/>).</param>
public sealed record RecomputeView(string State, string? Day, int Done, int Total, long? RemainingMs, int Skipped);

/// <summary>
/// Recalcul d'une période, jour après jour, hors du thread de l'interface et sur une connexion à part. Chaque
/// jour est rejoué sous le verrou de la <see cref="KeySession"/> : un verrouillage attend au plus la fin du
/// jour en cours, puis le recalcul s'arrête. Un seul recalcul à la fois ; l'annulation prend effet après le premier jour, entre deux jours.
/// </summary>
public sealed class RecomputeJob
{
    private readonly Lock _gate = new();
    private readonly string _dbPath;
    private readonly KeySession _session;
    private readonly Func<long> _nowMs;
    private RecomputeView _view = new("idle", null, 0, 0, null, 0);
    private Ticket? _run;

    public RecomputeJob(string dbPath, KeySession session, Func<long> nowMs)
    {
        ArgumentException.ThrowIfNullOrEmpty(dbPath);
        ArgumentNullException.ThrowIfNull(session);
        ArgumentNullException.ThrowIfNull(nowMs);
        _dbPath = dbPath;
        _session = session;
        _nowMs = nowMs;
    }

    /// <summary>Fin du dernier recalcul lancé ; terminée s'il n'y en a pas.</summary>
    public Task Completion { get; private set; } = Task.CompletedTask;

    public RecomputeView View()
    {
        lock (_gate) return _view;
    }

    /// <summary>
    /// Lance le recalcul des jours locaux <paramref name="fromDay"/> à <paramref name="toDay"/> (aaaammjj) ;
    /// faux si un recalcul tourne déjà.
    /// </summary>
    public bool TryStart(int fromDay, int toDay, LocalDayClock clock, AnalysisSettings settings)
    {
        ArgumentNullException.ThrowIfNull(clock);
        ArgumentNullException.ThrowIfNull(settings);
        var days = new List<int>();
        for (var d = LocalDayClock.DateOf(fromDay); LocalDayClock.KeyOf(d) <= toDay; d = d.AddDays(1)) days.Add(LocalDayClock.KeyOf(d));
        if (days.Count == 0) throw new ArgumentException("Période vide.", nameof(toDay));
        lock (_gate)
        {
            if (_view.State == "running") return false;
            var run = _run = new Ticket();
            _view = new RecomputeView("running", Iso(days[0]), 0, days.Count, null, 0);
            Completion = Task.Run(() => Run(days, clock, settings, run));
            return true;
        }
    }

    /// <summary>Demande l'arrêt après le jour en cours.</summary>
    public void Cancel()
    {
        lock (_gate)
        {
            if (_run is not null) _run.Cancelled = true;
        }
    }

    private void Run(List<int> days, LocalDayClock clock, AnalysisSettings settings, Ticket run)
    {
        string end = "done";
        try
        {
            using var db = KeyStatsDb.Open(_dbPath);
            var replay = new HistoryReplay(db, clock, settings);
            long started = _nowMs();
            int covered = 0, skipped = 0;
            // Un jour peut être rejoué avec ses voisins (session à cheval sur minuit) : ceux déjà couverts sont sautés.
            for (int i = 0; i < days.Count; i++)
            {
                if (days[i] <= covered) continue;
                if (i > 0 && run.Cancelled)
                {
                    end = "cancelled";
                    break;
                }
                int day = days[i];
                Update(v => v with { Day = Iso(day) });
                // Nul si la session a été verrouillée entre deux jours.
                if (_session.TryUse(master => replay.Replay(day, master)) is not { } result)
                {
                    end = "locked";
                    break;
                }
                covered = result.ToDay;
                int done = days.Count(d => d <= covered);
                if (result.Outcome != DayReplay.Done) skipped += done - i;
                long elapsed = _nowMs() - started;
                int skippedNow = skipped;
                Update(v => v with { Done = done, Skipped = skippedNow, RemainingMs = elapsed * (days.Count - done) / done });
            }
        }
        catch (Exception)
        {
            // Le message pourrait citer un chemin : seul l'échec est rendu.
            end = "failed";
        }
        Update(v => v with { State = end, Day = null, RemainingMs = null });
    }

    private void Update(Func<RecomputeView, RecomputeView> change)
    {
        lock (_gate) _view = change(_view);
    }

    /// <summary>Demande d'arrêt propre à un recalcul : une annulation tardive ne touche pas le suivant.</summary>
    private sealed class Ticket
    {
        public volatile bool Cancelled;
    }

    private static string Iso(int day) => LocalDayClock.DateOf(day).ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
}
