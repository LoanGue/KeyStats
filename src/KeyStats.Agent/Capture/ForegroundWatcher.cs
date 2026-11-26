using System.Runtime.InteropServices;
using KeyStats.Agent.Interop;
using KeyStats.Core.Analysis;
using KeyStats.Core.Focus;
using KeyStats.Core.Input;

namespace KeyStats.Agent.Capture;

/// <summary>Compteurs du suivi de fenêtre, sans nom ni titre.</summary>
internal struct FocusCounters
{
    public long Foreground, NameChanges, Changes, Excluded, Elevated, MarkersDropped, Unreadable, HookErrors, CallbackErrors;
}

/// <summary>
/// Suit la fenêtre au premier plan par <c>SetWinEventHook</c>, sans sondage : passage à une autre
/// fenêtre, et changement de titre de celle-ci (crochet limité à son processus, refait à chaque
/// changement de processus). Vit sur le thread de capture, dont la boucle de messages livre les
/// événements. Le titre d'une appli exclue n'est même pas lu.
/// </summary>
internal sealed unsafe class ForegroundWatcher(FocusChannel channel, ExclusionList exclusions, EventRing ring, EventClock clock, EventWaitHandle signal) : IDisposable
{
    private const int TitleMax = 512, PathMax = 1024, ERROR_ACCESS_DENIED = 5;
    private static ForegroundWatcher? s_current;

    private readonly FocusTracker _tracker = new(exclusions);
    private FocusCounters _counters;
    private nint _foregroundHook, _nameHook, _window;
    private uint _pid = uint.MaxValue; // aucun processus lu
    private string _exe = "";
    private bool _elevated, _excluded;

    public FocusCounters Counters => _counters;

    /// <summary>Sur le thread de capture. Lit tout de suite la fenêtre active.</summary>
    public void Start()
    {
        if (Interlocked.CompareExchange(ref s_current, this, null) is not null)
            throw new InvalidOperationException("Un suivi de fenêtre tourne déjà.");
        _foregroundHook = WinEvents.SetWinEventHook(WinEvents.EVENT_SYSTEM_FOREGROUND, WinEvents.EVENT_SYSTEM_FOREGROUND, 0,
            &OnEvent, 0, 0, WinEvents.WINEVENT_OUTOFCONTEXT | WinEvents.WINEVENT_SKIPOWNPROCESS);
        if (_foregroundHook == 0) _counters.HookErrors++;
        try
        {
            Observe(WinEvents.GetForegroundWindow());
        }
        catch (Exception)
        {
            _counters.CallbackErrors++;
        }
    }

    /// <summary>
    /// Sur le thread de capture. La fenêtre active est relue comme si elle venait d'un autre processus :
    /// une appli nouvellement exclue l'est dès maintenant, sans attendre un changement de fenêtre.
    /// </summary>
    public void SetExclusions(ExclusionList exclusions)
    {
        _tracker.Exclusions = exclusions;
        _pid = uint.MaxValue;
        // La fenêtre active d'abord : la dernière vue a pu être détruite depuis.
        nint hwnd = WinEvents.GetForegroundWindow();
        Observe(hwnd != 0 ? hwnd : _window);
    }

    /// <summary>Sur le thread de capture, avant la fin de sa boucle de messages.</summary>
    public void Dispose()
    {
        Unhook(ref _nameHook);
        Unhook(ref _foregroundHook);
        s_current = null;
    }

    [UnmanagedCallersOnly]
    private static void OnEvent(nint hook, uint ev, nint hwnd, int idObject, int idChild, uint thread, uint time)
    {
        if (s_current is not { } w || hwnd == 0 || idObject != WinEvents.OBJID_WINDOW || idChild != WinEvents.CHILDID_SELF) return;
        try
        {
            if (ev == WinEvents.EVENT_SYSTEM_FOREGROUND)
            {
                w._counters.Foreground++;
                w.Observe(hwnd);
            }
            else if (hwnd == w._window)
            {
                w._counters.NameChanges++;
                w.Observe(hwnd);
            }
        }
        catch (Exception)
        {
            w._counters.CallbackErrors++;
        }
    }

    private void Observe(nint hwnd)
    {
        if (hwnd == 0) return;
        uint pid;
        _ = WinEvents.GetWindowThreadProcessId(hwnd, &pid);
        if (pid != _pid) SwitchProcess(pid);
        _window = hwnd;
        string title = _excluded ? "" : ReadTitle(hwnd);
        var change = _tracker.Observe(new ForegroundWindow(clock.NowMs(), hwnd, (int)pid, _exe, title, _elevated));
        if (change is null) return;
        _counters.Changes++;
        if (change.Flags.HasFlag(FocusState.Excluded)) _counters.Excluded++;
        if (change.Flags.HasFlag(FocusState.Elevated)) _counters.Elevated++;
        if (!channel.Publish(change, ring)) _counters.MarkersDropped++;
        signal.Set();
    }

    private void SwitchProcess(uint pid)
    {
        _pid = pid;
        ReadProcess(pid);
        _excluded = _tracker.IsExcluded(_exe);
        Unhook(ref _nameHook);
        if (_excluded || pid == 0) return;
        _nameHook = WinEvents.SetWinEventHook(WinEvents.EVENT_OBJECT_NAMECHANGE, WinEvents.EVENT_OBJECT_NAMECHANGE, 0,
            &OnEvent, pid, 0, WinEvents.WINEVENT_OUTOFCONTEXT);
        if (_nameHook == 0) _counters.HookErrors++;
    }

    /// <summary>
    /// Nom de l'exécutable et élévation. Accès refusé : fenêtre d'un niveau supérieur, donc
    /// administrateur. Nom illisible : chaîne vide, que le suivi traite comme une appli exclue.
    /// </summary>
    private void ReadProcess(uint pid)
    {
        _exe = "";
        nint process = Process32.OpenProcess(Process32.PROCESS_QUERY_LIMITED_INFORMATION, 0, pid);
        if (process == 0)
        {
            _counters.Unreadable++;
            _elevated = Marshal.GetLastPInvokeError() == ERROR_ACCESS_DENIED;
            return;
        }
        char* path = stackalloc char[PathMax];
        uint size = PathMax;
        if (Process32.QueryFullProcessImageName(process, 0, path, &size) != 0)
            _exe = Path.GetFileName(new string(path, 0, (int)size));
        _elevated = IsElevated(process);
        _ = Process32.CloseHandle(process);
    }

    private static bool IsElevated(nint process)
    {
        nint token;
        if (Process32.OpenProcessToken(process, Process32.TOKEN_QUERY, &token) == 0)
            return Marshal.GetLastPInvokeError() == ERROR_ACCESS_DENIED;
        uint elevation = 0, returned;
        bool ok = Process32.GetTokenInformation(token, Process32.TokenElevation, &elevation, sizeof(uint), &returned) != 0;
        _ = Process32.CloseHandle(token);
        return ok && elevation != 0;
    }

    private static string ReadTitle(nint hwnd)
    {
        char* text = stackalloc char[TitleMax];
        int n = WinEvents.GetWindowText(hwnd, text, TitleMax);
        return n > 0 ? new string(text, 0, n) : "";
    }

    private static void Unhook(ref nint hook)
    {
        if (hook != 0) _ = WinEvents.UnhookWinEvent(hook);
        hook = 0;
    }
}
