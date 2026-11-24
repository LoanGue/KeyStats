namespace KeyStats.Tools;

internal static class Program
{
    private static int Main(string[] args)
    {
        Console.Error.WriteLine("keystats-tools : commandes à venir (replay, recompute).");
        return args.Length == 0 ? 0 : 1;
    }
}
