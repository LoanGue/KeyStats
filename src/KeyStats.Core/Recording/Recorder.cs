using KeyStats.Core.Analysis;
using KeyStats.Core.Focus;
using KeyStats.Core.Input;
using KeyStats.Core.Storage;
using Microsoft.Data.Sqlite;

namespace KeyStats.Core.Recording;

/// <summary>Compteurs de diagnostic de l'enregistreur : aucun contenu, seulement des nombres.</summary>
public readonly record struct RecorderCounters(long Batches, long WriteErrors, long RawDropped, long UnknownMarkers, long PausedEvents);

/// <summary>
/// Thread d'analyse de l'agent : fait passer les événements et les changements de fenêtre dans
/// l'<see cref="Analyzer"/>, garde le brut en mémoire et écrit le tout par lots, en une seule
/// transaction : statistiques, bloc brut chiffré, périodes non observées et, au changement de
/// fenêtre, l'appli, le titre chiffré et la ligne <c>focus</c>. Sans clé publique maîtresse
/// (chiffrement pas encore configuré), seules les statistiques sont écrites : ni brut, ni titre.
/// Pas sûr entre threads : il vit sur le thread d'analyse.
/// </summary>
public sealed class Recorder
{
    public const long FlushIntervalMs = 60_000;
    public const int RawCapacity = 4096;

    private readonly KeyStatsDb _db;
    private readonly RawArchiveWriter? _archive;
    private readonly LocalDayClock _clock;
    private readonly StatsWriter _writer;
    private readonly InputEvent[] _raw = new InputEvent[RawCapacity];
    private readonly PauseLog _pauses = new();
    private int _rawCount;
    private long? _focusId;
    // Tant qu'aucune fenêtre n'est connue, on ne garde pas de brut : il pourrait venir d'une appli exclue.
    private bool _countersOnly = true;
    private long _lastWriteMs;
    private long _batches, _writeErrors, _rawDropped, _unknownMarkers, _pausedEvents;

    /// <param name="archive">Écriture du brut et des titres ; nul tant que le chiffrement n'est pas configuré.</param>
    public Recorder(KeyStatsDb db, RawArchiveWriter? archive, LocalDayClock clock, long nowMs,
        AnalysisSettings? settings = null, LiveMeter? live = null)
    {
        _db = db;
        _archive = archive;
        _clock = clock;
        _writer = new StatsWriter(db);
        _lastWriteMs = nowMs;
        Analyzer = new Analyzer(new StatsAccumulator(clock), settings, live);
    }

    public Analyzer Analyzer { get; }

    public RecorderCounters Counters => new(_batches, _writeErrors, _rawDropped, _unknownMarkers, _pausedEvents);

    /// <summary>Un lot lu dans la file : les marqueurs de fenêtre sont résolus par le canal.</summary>
    public void Consume(ReadOnlySpan<InputEvent> events, FocusChannel focus)
    {
        foreach (ref readonly var e in events)
        {
            if (e.Kind != EventKind.Focus) OnEvent(e);
            else if (focus.Take(e) is { } change) OnFocus(change);
            else _unknownMarkers++;
        }
    }

    public void OnEvent(in InputEvent e)
    {
        // Pendant une pause extérieure, rien n'est compté ; seule une remise à zéro passe.
        if (_pauses.External && e.Kind != EventKind.Reset)
        {
            _pausedEvents++;
            return;
        }
        Analyzer.Process(e);
        if (_archive is null || _countersOnly) return;
        if (_rawCount == _raw.Length && !Flush(e.Ms))
        {
            // La base refuse d'écrire : on perd ce brut plutôt que de grossir sans limite.
            _rawDropped += _rawCount;
            ClearRaw();
        }
        _raw[_rawCount++] = e;
    }

    /// <summary>Nouvelle fenêtre ou nouveau titre : le lot en cours est écrit sous l'ancienne fenêtre.</summary>
    public void OnFocus(FocusChange change)
    {
        ArgumentNullException.ThrowIfNull(change);
        var info = new FocusInfo(change.Ms, 0, 0, null, change.Flags);
        long? focusId = null;
        bool ok = Write(change.Ms, () =>
        {
            int app = _db.GetOrAddApp(change.Exe);
            long? title = _archive is not null && change.Title is { Length: > 0 } t
                ? _archive.GetOrAddTitle(app, change.Exe, t, change.Ms) : null;
            focusId = _db.AddFocus(change.Ms, app, title, null, change.Flags);
            info = info with { AppId = app, TitleId = title ?? 0 };
        });
        // En cas d'échec, la fenêtre reste inconnue (appli 0) : ses frappes sont comptées, le brut n'a pas de focus.
        if (!ok) info = new FocusInfo(change.Ms, 0, 0, null, change.Flags);
        _focusId = ok ? focusId : null;
        _countersOnly = change.Flags.CountersOnly();
        _pauses.SetFocusReason(change.Ms, PauseFor(change.Flags));
        Analyzer.SetFocus(info);
    }

    /// <summary>Appelé quand la file est vide : conclut ce qui doit l'être et écrit toutes les 60 s.</summary>
    public void Tick(long nowMs)
    {
        Analyzer.Tick(nowMs);
        if (nowMs - _lastWriteMs >= FlushIntervalMs || nowMs < _lastWriteMs) Flush(nowMs);
    }

    /// <summary>Écrit tout ce qui est en attente. Renvoie faux si la base a refusé (rien n'est perdu).</summary>
    public bool Flush(long nowMs) => Write(nowMs, null);

    /// <summary>Verrouillage, veille, pause par l'icône : la session en cours se termine.</summary>
    public void BeginPause(PauseReason reason, long nowMs)
    {
        Analyzer.EndAll();
        _pauses.Begin(nowMs, reason);
    }

    /// <summary>Fin de pause. L'appelant pousse ensuite une remise à zéro de l'état des touches.</summary>
    public void EndPause(long nowMs) => _pauses.End(nowMs);

    /// <summary>Verrouillage, veille : le lot est écrit, puis la clé d'époque oubliée.</summary>
    public void DropEpoch(long nowMs)
    {
        Flush(nowMs);
        _archive?.DropEpoch();
    }

    /// <summary>Changement de fuseau (<c>WM_TIMECHANGE</c>) : les frappes suivantes vont dans le nouveau jour local.</summary>
    public void SetZone(TimeZoneInfo zone, long nowMs)
    {
        Flush(nowMs);
        _clock.SetZone(zone);
    }

    /// <summary>Arrêt de l'agent : tout est conclu et écrit, le brut en mémoire est effacé.</summary>
    public bool Close(long nowMs)
    {
        Analyzer.EndAll();
        _pauses.CloseAll(nowMs);
        bool ok = Flush(nowMs);
        ClearRaw();
        return ok;
    }

    private bool Write(long nowMs, Action? inTransaction)
    {
        _lastWriteMs = nowMs;
        if (inTransaction is null && Analyzer.Stats.IsEmpty && _rawCount == 0 && !_pauses.HasDone) return true;
        try
        {
            using (var tx = _db.BeginTransaction())
            {
                _writer.Write(Analyzer.Stats);
                if (_archive is not null && _rawCount > 0) _archive.WriteBlock(_raw.AsSpan(0, _rawCount), _focusId, nowMs);
                _pauses.Write(_db);
                inTransaction?.Invoke();
                tx.Commit();
            }
        }
        catch (Exception e) when (e is SqliteException or InvalidOperationException or IOException)
        {
            // Tout reste en mémoire pour le lot suivant ; le message d'erreur n'est pas gardé.
            _writeErrors++;
            return false;
        }
        _batches++;
        Analyzer.Stats.Clear();
        _pauses.Clear();
        ClearRaw();
        return true;
    }

    private void ClearRaw()
    {
        // Le brut en clair ne reste pas en mémoire plus que nécessaire.
        Array.Clear(_raw, 0, _rawCount);
        _rawCount = 0;
    }

    private static PauseReason PauseFor(FocusState flags) =>
        (flags & FocusState.Elevated) != 0 ? PauseReason.Elevated
        : (flags & FocusState.Excluded) != 0 ? PauseReason.Excluded
        : (flags & FocusState.Secret) != 0 ? PauseReason.Secret
        : 0;
}
