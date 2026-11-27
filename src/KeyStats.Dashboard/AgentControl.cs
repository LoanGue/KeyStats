using System.Diagnostics;
using KeyStats.Core.Control;
using KeyStats.Core.Web;

namespace KeyStats.Dashboard;

/// <summary>
/// L'agent de cette base, vu du tableau de bord : relecture des réglages et redémarrage par le tube
/// nommé, puis relance de <c>KeyStats.Agent.exe</c>, posé à côté du tableau de bord une fois installé.
/// </summary>
internal sealed class AgentControl(string dbPath) : IAgentControl
{
    private const string AgentExe = "KeyStats.Agent.exe";
    private static readonly TimeSpan PipeTimeout = TimeSpan.FromSeconds(3);
    // L'agent écrit ce qui est en attente avant de rendre son mutex.
    private static readonly TimeSpan QuitTimeout = TimeSpan.FromSeconds(15);
    private static readonly TimeSpan StartTimeout = TimeSpan.FromSeconds(10);

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

    /// <summary>Un agent arrêté par l'utilisateur reste arrêté : seul un agent qui tourne est relancé.</summary>
    public async Task<bool> RestartAsync()
    {
        if (!IsRunning) return false;
        if (await ControlClient.SendAsync(PipeName, AgentCommand.Quit, PipeTimeout).ConfigureAwait(true) != AgentReply.Ok
            || !await WaitAsync(running: false, QuitTimeout).ConfigureAwait(true))
            return false;
        return Start() && await WaitAsync(running: true, StartTimeout).ConfigureAwait(true);
    }

    private bool Start()
    {
        string exe = Path.Combine(AppContext.BaseDirectory, AgentExe);
        if (!File.Exists(exe)) return false;
        var info = new ProcessStartInfo(exe) { UseShellExecute = false };
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
