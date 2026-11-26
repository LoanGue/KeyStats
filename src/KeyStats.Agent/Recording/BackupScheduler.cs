using KeyStats.Core.Storage;

namespace KeyStats.Agent.Recording;

/// <summary>
/// Sauvegarde nocturne : vérifie toutes les 10 minutes si elle est due (<see cref="BackupPlan"/>),
/// et met alors à jour le miroir sur un thread du pool, avec ses propres connexions. La capture et
/// l'analyse continuent pendant ce temps (WAL). Un échec est réessayé une heure plus tard.
/// </summary>
internal sealed class BackupScheduler : IDisposable
{
    private static readonly TimeSpan FirstCheck = TimeSpan.FromMinutes(2);
    private static readonly TimeSpan Period = TimeSpan.FromMinutes(10);

    private readonly string _source;
    private readonly Func<TimeZoneInfo> _zone;
    private readonly Timer _timer;
    private Target _target;
    private int _running;
    private volatile bool _disposed;

    /// <param name="folder">Dossier du miroir ; <c>null</c> pour <c>backup\</c> à côté de la base.</param>
    public BackupScheduler(string sourcePath, string? folder, Func<TimeZoneInfo> zone)
    {
        _source = sourcePath;
        _zone = zone;
        _target = new Target(BackupMirror.PathFor(sourcePath, folder));
        _timer = new Timer(static o => ((BackupScheduler)o!).Check(), this, Timeout.Infinite, Timeout.Infinite);
    }

    public void Start() => _timer.Change(FirstCheck, Period);

    /// <summary>
    /// Un autre dossier repart de sa propre date de dernière copie : un miroir neuf est fait à la
    /// prochaine vérification. Une copie en cours finit dans l'ancien dossier.
    /// </summary>
    public void SetFolder(string? folder)
    {
        string mirror = BackupMirror.PathFor(_source, folder);
        if (!string.Equals(mirror, Volatile.Read(ref _target).Mirror, StringComparison.OrdinalIgnoreCase))
            Volatile.Write(ref _target, new Target(mirror));
    }

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
        var target = Volatile.Read(ref _target);
        try
        {
            target.Plan ??= new BackupPlan(BackupMirror.LastRunMs(target.Mirror));
            if (_disposed || !target.Plan.ShouldRun(now, Environment.TickCount64, _zone())) return;
            BackupMirror.Run(_source, target.Mirror, now);
            target.Plan.Succeeded(now);
        }
        catch (Exception)
        {
            // Le message pourrait citer un chemin : rien n'est gardé. L'heure d'attente part de l'échec,
            // pas du début d'une copie qui a pu durer.
            (target.Plan ??= new BackupPlan(null)).Failed(Environment.TickCount64);
        }
        finally
        {
            Volatile.Write(ref _running, 0);
        }
    }

    /// <summary>Un miroir et son calendrier, lus seulement par la vérification en cours (garde <c>_running</c>).</summary>
    private sealed class Target(string mirror)
    {
        public string Mirror { get; } = mirror;
        public BackupPlan? Plan { get; set; }
    }
}
