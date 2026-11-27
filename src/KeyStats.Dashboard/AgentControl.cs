using System.Diagnostics;
using KeyStats.Core.Control;
using KeyStats.Core.Web;

namespace KeyStats.Dashboard;

/// <summary>
/// L'agent de cette base, vu du tableau de bord : relecture des réglages et redémarrage par le tube
/// nommé, puis relance du même <c>KeyStats.Agent.exe</c> que celui qui tournait.
/// </summary>
internal sealed class AgentControl(string dbPath) : IAgentControl
{
    private const string AgentExe = "KeyStats.Agent.exe";
    private static readonly TimeSpan PipeTimeout = TimeSpan.FromSeconds(3);
    // L'agent écrit ce qui est en attente avant de rendre son mutex.
    private static readonly TimeSpan QuitTimeout = TimeSpan.FromSeconds(15);
    private static readonly TimeSpan StartTimeout = TimeSpan.FromSeconds(10);
    // Deux enregistrements rapprochés ne croisent pas deux arrêts et deux relances.
    private static readonly SemaphoreSlim Restart = new(1, 1);

    private string PipeName => ControlProtocol.PipeName(Process.GetCurrentProcess().SessionId, dbPath);

    public bool IsRunning
    {
        get
        {
            if (!Mutex.TryOpenExisting(ControlProtocol.MutexName(dbPath), out var mutex)) return false;
            mutex.Dispose();
            return true;
        }
    }

    public async Task<bool> ReloadAsync() =>
        await ControlClient.SendAsync(PipeName, AgentCommand.Reload, PipeTimeout).ConfigureAwait(true) == AgentReply.Ok;

    /// <summary>
    /// Un agent arrêté par l'utilisateur reste arrêté : seul un agent qui tourne est relancé. Son exécutable
    /// est trouvé avant l'arrêt ; sans lui, rien n'est arrêté et les seuils attendent le prochain démarrage.
    /// </summary>
    public async Task<bool> RestartAsync()
    {
        await Restart.WaitAsync().ConfigureAwait(true);
        try
        {
            if (!IsRunning || AgentPath() is not { } exe) return false;
            if (await ControlClient.SendAsync(PipeName, AgentCommand.Quit, PipeTimeout).ConfigureAwait(true) != AgentReply.Ok
                || !await WaitAsync(running: false, QuitTimeout).ConfigureAwait(true))
                return false;
            return Start(exe) && await WaitAsync(running: true, StartTimeout).ConfigureAwait(true);
        }
        finally
        {
            Restart.Release();
        }
    }

    /// <summary>
    /// Exécutable d'un agent qui tourne dans cette session, sinon celui posé à côté du tableau de bord une
    /// fois installé ; null si aucun n'existe.
    /// </summary>
    private static string? AgentPath()
    {
        int session = Process.GetCurrentProcess().SessionId;
        foreach (var process in Process.GetProcessesByName(Path.GetFileNameWithoutExtension(AgentExe)))
        {
            using (process)
            {
                try
                {
                    if (process.SessionId == session && process.MainModule?.FileName is { } path && File.Exists(path))
                        return path;
                }
                catch (Exception e) when (e is System.ComponentModel.Win32Exception or InvalidOperationException)
                {
                    // Processus terminé entre-temps ou inaccessible : au suivant.
                }
            }
        }
        string installed = Path.Combine(AppContext.BaseDirectory, AgentExe);
        return File.Exists(installed) ? installed : null;
    }

    private bool Start(string exe)
    {
        var info = new ProcessStartInfo(exe) { UseShellExecute = false, WorkingDirectory = Path.GetDirectoryName(exe)! };
        // La base par défaut est celle de l'agent installé : pas d'argument, comme au démarrage de session.
        if (!string.Equals(Path.GetFullPath(dbPath), Path.GetFullPath(DashboardDb.DefaultPath), StringComparison.OrdinalIgnoreCase))
        {
            info.ArgumentList.Add("--db");
            info.ArgumentList.Add(dbPath);
        }
        try
        {
            using var process = Process.Start(info);
            return process is not null;
        }
        catch (Exception e) when (e is System.ComponentModel.Win32Exception or InvalidOperationException)
        {
            return false;
        }
    }

    private async Task<bool> WaitAsync(bool running, TimeSpan timeout)
    {
        var deadline = Stopwatch.StartNew();
        while (IsRunning != running)
        {
            if (deadline.Elapsed > timeout) return false;
            await Task.Delay(100).ConfigureAwait(true);
        }
        return true;
    }
}
