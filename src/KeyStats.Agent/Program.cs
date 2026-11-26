using KeyStats.Agent.Capture;
using KeyStats.Agent.Lifecycle;
using KeyStats.Agent.Recording;
using KeyStats.Core.Control;

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
            // Un agent par base et par session : un second lancement s'arrête sans rien faire.
            using var instance = new Mutex(true, $@"Local\KeyStats.Agent.{ControlProtocol.InstanceKey(path)}", out bool first);
            return first ? AgentHost.Run(path) : 0;
        }
        catch (Exception e)
        {
            // Pas de message : il pourrait citer une valeur. Le type suffit au diagnostic.
            Console.Error.WriteLine($"KeyStats.Agent : arrêt sur {e.GetType().Name}");
            return 1;
        }
    }
}
