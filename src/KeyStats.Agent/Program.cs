using KeyStats.Agent.Capture;

namespace KeyStats.Agent;

internal static class Program
{
    [STAThread]
    private static int Main(string[] args)
    {
        if (args is ["--probe", var s] && int.TryParse(s, out int seconds) && seconds > 0)
            return Probe.Run(seconds);
        // L'agent complet (analyse, écriture, icône) arrive dans les PR suivantes de l'étape 4.
        return 0;
    }
}
