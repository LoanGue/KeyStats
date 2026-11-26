using KeyStats.Core.Storage;

namespace KeyStats.Agent.Recording;

/// <summary>
/// Sauvegarde nocturne : vérifie toutes les 10 minutes si elle est due (<see cref="BackupSchedule"/>),
/// et met alors à jour le miroir sur un thread du pool, avec ses propres connexions. La capture et
/// l'analyse continuent pendant ce temps (WAL). Un échec est compté, puis réessayé une heure plus tard.
/// </summary>
internal sealed class BackupScheduler : IDisposable
{
    private static readonly TimeSpan FirstCheck = TimeSpan.FromMinutes(2);
    private static readonly TimeSpan Period = TimeSpan.FromMinutes(10);
    private const long RetryMs = 3_600_000;

    private readonly string _source;
    private readonly Func<TimeZoneInfo> _zone;
    private readonly Timer _timer;
    private long? _last;
    private bool _loaded;
    private long _retryAt;
    private int _running;
    private long _runs, _failures;

    public BackupScheduler(string sourcePath, Func<TimeZoneInfo> zone)
    {
        _source = sourcePath;
        _zone = zone;
        MirrorPath = MirrorOf(sourcePath);
        _timer = new Timer(static o => ((BackupScheduler)o!).Check(), this, Timeout.Infinite, Timeout.Infinite);
    }

    /// <summary>À côté de la base, dans <c>backup\</c>, sous le même nom.</summary>
    public static string MirrorOf(string dbPath)
    {
        string full = Path.GetFullPath(dbPath);
        return Path.Combine(Path.GetDirectoryName(full)!, "backup", Path.GetFileName(full));
    }

    public string MirrorPath { get; }

    public long Runs => Interlocked.Read(ref _runs);

    public long Failures => Interlocked.Read(ref _failures);

    public void Start() => _timer.Change(FirstCheck, Period);

    /// <summary>
    /// N'attend pas une sauvegarde en cours : à la fin de la session, elle est interrompue avec le
    /// processus et la transaction du miroir est annulée, le miroir précédent reste intact.
    /// </summary>
    public void Dispose() => _timer.Dispose();

    private void Check()
    {
        if (Interlocked.Exchange(ref _running, 1) != 0) return;
        long now = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
        try
        {
            if (!_loaded)
            {
                _loaded = true;
                _last = BackupMirror.LastRunMs(MirrorPath);
            }
            if (now < _retryAt || !BackupSchedule.IsDue(_last, now, _zone())) return;
            BackupMirror.Run(_source, MirrorPath, now);
            _last = now;
            Interlocked.Increment(ref _runs);
        }
        catch (Exception)
        {
            // Le message pourrait citer un chemin : seul le nombre d'échecs est gardé.
            Interlocked.Increment(ref _failures);
            _retryAt = now + RetryMs;
        }
        finally
        {
            Volatile.Write(ref _running, 0);
        }
    }
}
