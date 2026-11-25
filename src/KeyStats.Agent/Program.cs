using KeyStats.Agent.Capture;

namespace KeyStats.Agent;

internal static class Program
{
    [STAThread]
    private static int Main(string[] args)
    {
        if (args is ["--probe", var s, ..] && int.TryParse(s, out int seconds) && seconds > 0)
        {
            if (args.Length == 2) return Probe.Run(seconds, null);
            if (args is [_, _, "--db", var db]) return Probe.Run(seconds, db);
        }
        if (args.Length > 0)
        {
            Console.Error.WriteLine("Usage : KeyStats.Agent.exe [--probe <secondes> [--db <chemin>]]");
            return 2;
        }
        // L'agent complet (analyse, écriture, icône) arrive dans les PR suivantes de l'étape 4.
        return 0;
    }
}
