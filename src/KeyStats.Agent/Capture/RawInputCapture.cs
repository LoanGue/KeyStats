using System.ComponentModel;
using System.Runtime.InteropServices;
using KeyStats.Agent.Interop;
using KeyStats.Core.Input;

namespace KeyStats.Agent.Capture;

/// <summary>Compteurs de la capture, lus après <see cref="RawInputCapture.Stop"/>.</summary>
internal struct CaptureCounters
{
    public long KeyboardMessages, MouseMessages, Injected, Ignored, ReadErrors, Written, Devices;
}

/// <summary>
/// Thread de capture : une fenêtre de messages reçoit le clavier et la souris par Raw Input en mode
/// écoute (<c>RIDEV_INPUTSINK</c>), hors du trajet des touches. Chaque message devient un ou plusieurs
/// <see cref="InputEvent"/> poussés dans la file, sans allocation. Une seule capture par processus.
/// </summary>
internal sealed unsafe class RawInputCapture : IDisposable
{
    private const string ClassName = "KeyStats.RawInput";
    private const int BufferSize = 64; // en-tête 24 o + RAWMOUSE 24 o ou RAWKEYBOARD 16 o
    private static RawInputCapture? s_current;

    private readonly EventRing _ring;
    private readonly EventWaitHandle _signal;
    private readonly EventClock _clock = new();
    private readonly Dictionary<nint, ushort> _devices = new(8);
    private readonly ManualResetEventSlim _ready = new();
    private readonly Thread _thread;
    private CaptureCounters _counters;
    private nint _hwnd;
    private int _startError;

    /// <param name="signal">Levé après chaque écriture, pour réveiller le consommateur.</param>
    public RawInputCapture(EventRing ring, EventWaitHandle signal)
    {
        _ring = ring;
        _signal = signal;
        _thread = new Thread(Run) { Name = "KeyStats capture", IsBackground = true, Priority = ThreadPriority.AboveNormal };
    }

    public CaptureCounters Counters => _counters;

    public void Start()
    {
        if (Interlocked.CompareExchange(ref s_current, this, null) is not null)
            throw new InvalidOperationException("Une capture tourne déjà.");
        _thread.Start();
        _ready.Wait();
        if (_startError == 0) return;
        _thread.Join();
        s_current = null;
        throw new Win32Exception(_startError);
    }

    public void Stop()
    {
        if (s_current != this) return;
        _ = User32.PostMessage(_hwnd, User32.WM_CLOSE, 0, 0);
        _thread.Join();
        s_current = null;
    }

    public void Dispose()
    {
        Stop();
        _ready.Dispose();
    }

    private void Run()
    {
        Kernel32.OptOutOfEcoQos();
        nint instance = Kernel32.GetModuleHandle(null);
        fixed (char* cls = ClassName)
        {
            if (!Open(cls, instance))
            {
                _startError = Marshal.GetLastPInvokeError();
                if (_hwnd != 0) _ = User32.DestroyWindow(_hwnd);
                _ = User32.UnregisterClass(cls, instance);
                _ready.Set();
                return;
            }
            _ready.Set();
            MSG msg;
            while (User32.GetMessage(&msg, 0, 0, 0) > 0) User32.DispatchMessage(&msg);
            _ = Register(User32.RIDEV_REMOVE, 0);
            _ = User32.UnregisterClass(cls, instance);
        }
    }

    private bool Open(char* cls, nint instance)
    {
        var wc = new WNDCLASSEXW
        {
            cbSize = (uint)sizeof(WNDCLASSEXW),
            lpfnWndProc = &WndProc,
            hInstance = instance,
            lpszClassName = cls,
        };
        if (User32.RegisterClassEx(&wc) == 0 && Marshal.GetLastPInvokeError() != User32.ERROR_CLASS_ALREADY_EXISTS)
            return false;
        _hwnd = User32.CreateWindowEx(0, cls, null, 0, 0, 0, 0, 0, User32.HWND_MESSAGE, 0, instance, 0);
        return _hwnd != 0 && Register(User32.RIDEV_INPUTSINK, _hwnd);
    }

    /// <summary>Clavier (page 1, usage 6) et souris (page 1, usage 2).</summary>
    private static bool Register(uint flags, nint target)
    {
        var devices = stackalloc RAWINPUTDEVICE[2];
        devices[0] = new RAWINPUTDEVICE { usUsagePage = 1, usUsage = 6, dwFlags = flags, hwndTarget = target };
        devices[1] = new RAWINPUTDEVICE { usUsagePage = 1, usUsage = 2, dwFlags = flags, hwndTarget = target };
        return User32.RegisterRawInputDevices(devices, 2, (uint)sizeof(RAWINPUTDEVICE)) != 0;
    }

    [UnmanagedCallersOnly]
    private static nint WndProc(nint hwnd, uint msg, nint wParam, nint lParam)
    {
        if (msg == User32.WM_INPUT) s_current?.OnInput(lParam);
        else if (msg == User32.WM_DESTROY)
        {
            User32.PostQuitMessage(0);
            return 0;
        }
        // WM_INPUT passe aussi par DefWindowProc, qui libère le message.
        return User32.DefWindowProc(hwnd, msg, wParam, lParam);
    }

    private void OnInput(nint handle)
    {
        byte* buf = stackalloc byte[BufferSize];
        uint size = BufferSize;
        uint got = User32.GetRawInputData(handle, User32.RID_INPUT, buf, &size, (uint)sizeof(RAWINPUTHEADER));
        var header = (RAWINPUTHEADER*)buf;
        byte* body = buf + sizeof(RAWINPUTHEADER);
        if (got == uint.MaxValue || got < sizeof(RAWINPUTHEADER)) _counters.ReadErrors++;
        else if (header->dwType == User32.RIM_TYPEKEYBOARD && got >= sizeof(RAWINPUTHEADER) + sizeof(RAWKEYBOARD))
            OnKeyboard(header->hDevice, (RAWKEYBOARD*)body);
        else if (header->dwType == User32.RIM_TYPEMOUSE && got >= sizeof(RAWINPUTHEADER) + sizeof(RAWMOUSE))
            OnMouse(header->hDevice, (RAWMOUSE*)body);
    }

    private void OnKeyboard(nint device, RAWKEYBOARD* kb)
    {
        _counters.KeyboardMessages++;
        if (!RawInputDecoder.Keyboard(kb->MakeCode, kb->Flags, kb->VKey, device != 0, out var kind, out var key))
        {
            if (device == 0) _counters.Injected++;
            else _counters.Ignored++;
            return;
        }
        Push(new InputEvent(_clock.NowMs(), kind, key, DeviceId(device)));
    }

    private void OnMouse(nint device, RAWMOUSE* mouse)
    {
        _counters.MouseMessages++;
        if (mouse->usButtonFlags == 0) return; // mouvement seul : le cas le plus fréquent
        Span<InputEvent> events = stackalloc InputEvent[RawInputDecoder.MaxMouseEvents];
        // Pavé tactile de précision : pas d'appareil. Les clics comptent quand même (appareil 0).
        int n = RawInputDecoder.MouseButtons(mouse->usButtonFlags, _clock.NowMs(), device == 0 ? (ushort)0 : DeviceId(device), events);
        for (int i = 0; i < n; i++) Push(events[i]);
    }

    private void Push(in InputEvent e)
    {
        if (_ring.TryWrite(e)) _counters.Written++;
        _signal.Set();
    }

    /// <summary>Petit numéro stable par appareil, de 1 à 65535, attribué à la première frappe.</summary>
    private ushort DeviceId(nint handle)
    {
        if (_devices.TryGetValue(handle, out ushort id)) return id;
        if (_devices.Count >= ushort.MaxValue) return ushort.MaxValue;
        id = (ushort)(_devices.Count + 1);
        _devices.Add(handle, id);
        _counters.Devices = _devices.Count;
        return id;
    }
}
