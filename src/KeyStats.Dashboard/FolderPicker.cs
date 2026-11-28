using KeyStats.Core.Web;

namespace KeyStats.Dashboard;

/// <summary>Sélecteur de dossier de Windows, modal sur la fenêtre du tableau de bord.</summary>
internal sealed class FolderPicker : IFolderPicker
{
    /// <summary>Fenêtre propriétaire, posée une fois la fenêtre créée.</summary>
    public IWin32Window? Owner { get; set; }

    public async Task<string?> PickAsync(string initialFolder, string title)
    {
        // La route est appelée depuis un événement de WebView2 : la fenêtre modale s'ouvre une fois
        // l'événement rendu, pas dans sa boucle de messages.
        await Task.Yield();
        using var dialog = new FolderBrowserDialog
        {
            Description = title,
            UseDescriptionForTitle = true,
            ShowNewFolderButton = true,
            InitialDirectory = Directory.Exists(initialFolder) ? initialFolder : "",
        };
        return dialog.ShowDialog(Owner) == DialogResult.OK ? dialog.SelectedPath : null;
    }
}
