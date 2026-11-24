namespace KeyStats.Dashboard;

internal static class Program
{
    [STAThread]
    private static void Main()
    {
        ApplicationConfiguration.Initialize();
        using var form = new Form { Text = "KeyStats", Width = 1280, Height = 860 };
        Application.Run(form);
    }
}
