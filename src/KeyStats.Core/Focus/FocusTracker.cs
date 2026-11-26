using KeyStats.Core.Analysis;

namespace KeyStats.Core.Focus;

/// <summary>
/// Fenêtre au premier plan telle que l'agent la lit. Le titre ne vit qu'en mémoire : il n'est écrit
/// sur disque que chiffré. <see cref="Exe"/> vide : exécutable inconnu, traité comme une appli exclue.
/// </summary>
public readonly record struct ForegroundWindow(long Ms, long Window, int ProcessId, string Exe, string Title, bool Elevated)
{
    /// <summary>Sans le titre : une trace ne doit pas révéler ce qui est à l'écran.</summary>
    public override string ToString() => $"{Ms} {ProcessId} {Exe}";
}

/// <summary>Changement de fenêtre ou de titre, dans l'ordre des frappes. <see cref="Title"/> est nul pour une appli exclue.</summary>
public sealed class FocusChange
{
    public required long Ms { get; init; }
    public required string Exe { get; init; }
    public required string? Title { get; init; }
    public required FocusState Flags { get; init; }

    /// <summary>Numéro donné par le <see cref="FocusChannel"/>.</summary>
    public long Seq { get; internal set; }

    /// <summary>Sans le titre : une trace ne doit pas révéler ce qui est à l'écran.</summary>
    public override string ToString() => $"{Ms} #{Seq} {Exe} {Flags}";
}

/// <summary>
/// Transforme les lectures successives de la fenêtre au premier plan en changements : nouvelle
/// fenêtre, nouveau titre, appli exclue, fenêtre administrateur. Une lecture identique à la
/// précédente ne produit rien. Pour une appli exclue, ou dont l'exécutable est inconnu, ni le titre
/// ni ses changements ne sont gardés.
/// </summary>
public sealed class FocusTracker(ExclusionList exclusions)
{
    private ExclusionList _exclusions = exclusions;
    private bool _has;
    private long _window;
    private int _processId;
    private string? _title;
    private FocusState _flags;

    /// <summary>
    /// Remplaçable depuis un autre thread (relecture des réglages). La fenêtre active change d'état
    /// à sa prochaine lecture, qui compte alors comme un changement.
    /// </summary>
    public ExclusionList Exclusions
    {
        get => Volatile.Read(ref _exclusions);
        set => Volatile.Write(ref _exclusions, value);
    }

    public FocusChange? Observe(in ForegroundWindow w)
    {
        bool newWindow = !_has || w.Window != _window || w.ProcessId != _processId;
        var flags = newWindow ? FocusState.NewWindow : FocusState.None;
        if (IsExcluded(w.Exe)) flags |= FocusState.Excluded;
        if (w.Elevated) flags |= FocusState.Elevated;
        string? title = (flags & FocusState.Excluded) != 0 ? null : w.Title;
        var stable = _flags & ~FocusState.NewWindow;
        if (!newWindow && title == _title && (flags & ~FocusState.NewWindow) == stable) return null;

        _has = true;
        _window = w.Window;
        _processId = w.ProcessId;
        _title = title;
        _flags = flags;
        return new FocusChange { Ms = w.Ms, Exe = w.Exe, Title = title, Flags = flags };
    }

    /// <summary>Exclue, ou inconnue : dans le doute, on ne garde que les compteurs.</summary>
    public bool IsExcluded(string exe) => exe.Length == 0 || Exclusions.Contains(exe);
}
