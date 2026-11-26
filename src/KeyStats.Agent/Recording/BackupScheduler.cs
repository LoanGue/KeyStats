using KeyStats.Core.Storage;

namespace KeyStats.Agent.Recording;

/// <summary>
/// Sauvegarde nocturne : vérifie toutes les 10 minutes si elle est due (<see cref="BackupPlan"/>),
/// et met alors à jour le miroir sur un thread du pool, avec ses propres connexions. La capture et
/// l'analyse continuent pendant ce temps (WAL). Un échec est compté, puis réessayé une heure plus tard.
/// </summary>
internal sealed class BackupScheduler : IDisposable
{
    private static readonly TimeSpan FirstCheck = TimeSpan.FromMinutes(2);
    private static readonly TimeSpan Period = TimeSpan.FromMinutes(10);

    private readonly string _source;
    private readonly string _mirror;
    private readonly Func<TimeZoneInfo> _zone;
    private readonly Timer _timer;
    private BackupPlan? _plan;
    private int _running;
    private volatile bool _disposed;

    public BackupScheduler(string sourcePath, Func<TimeZoneInfo> zone)
    {
        _source = sourcePath;
        _zone = zone;
        // À côté de la base, dans backup\, sous le même nom.
        string full = Path.GetFullPath(sourcePath);
        _mirror = Path.Combine(Path.GetDirectoryName(full)!, "backup", Path.GetFileName(full));
        _timer = new Timer(static o => ((BackupScheduler)o!).Check(), this, Timeout.Infinite, Timeout.Infinite);
    }

    public void Start() => _timer.Change(FirstCheck, Period);

    /// <summary>
    /// Une vérification déjà en file ne lance plus rien. Une sauvegarde en cours n'est pas attendue : à
    /// la fin de la session, elle est interrompue avec le processus et la transaction du miroir est
    /// annulée, le miroir précédent reste intact.
    /// </summary>
    public void Dispose()
    {
        _disposed = true;
        _timer.Dispose();
    }

    private void Check()
    {
        if (_disposed || Interlocked.Exchange(ref _running, 1) != 0) return;
        long now = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
        long tick = Environment.TickCount64;
        try
        {
            _plan ??= new BackupPlan(BackupMirror.LastRunMs(_mirror));
            if (_disposed || !_plan.ShouldRun(now, tick, _zone())) return;
            BackupMirror.Run(_source, _mirror, now);
            _plan.Succeeded(now);
        }
        catch (Exception)
        {
            // Le message pourrait citer un chemin : rien n'est gardé, la vérification suivante réessaie.
            (_plan ??= new BackupPlan(null)).Failed(tick);
        }
        finally
        {
            Volatile.Write(ref _running, 0);
        }
    }
}
