using KeyStats.Agent.Capture;
using KeyStats.Agent.Lifecycle;
using KeyStats.Agent.Recording;

namespace KeyStats.Agent;

internal static class Program
{
    [STAThread]
    private static int Main(string[] args)
    {
        if (args is ["--probe", var s, ..] && int.TryParse(s, out int seconds) && seconds > 0)
        {
            if (args.Length == 2) return Probe.Run(seconds, null);
            if (args is [_, _, "--db", var db] && !string.IsNullOrWhiteSpace(db)) return Probe.Run(seconds, db);
        }
        string? path = args switch
        {
            [] => AgentStorage.DefaultPath,
            ["--db", var p] when !string.IsNullOrWhiteSpace(p) => p,
            _ => null,
        };
        if (path is null)
        {
            Console.Error.WriteLine("Usage : KeyStats.Agent.exe [--db <chemin>] | --probe <secondes> [--db <chemin>]");
            return 2;
        }
        try
        {
            return AgentHost.Run(path);
        }
        catch (Exception e)
        {
            // Pas de message : il pourrait citer une valeur. Le type suffit au diagnostic.
            Console.Error.WriteLine($"KeyStats.Agent : arrêt sur {e.GetType().Name}");
            return 1;
        }
    }
}
