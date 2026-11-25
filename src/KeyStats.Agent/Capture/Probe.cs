using System.Diagnostics;
using KeyStats.Core.Focus;
using KeyStats.Core.Input;

namespace KeyStats.Agent.Capture;

/// <summary>
/// <c>--probe &lt;secondes&gt;</c> : capture pendant la durée donnée puis écrit des compteurs sur la
/// sortie standard. Ni touche, ni caractère, ni appli, ni titre n'est écrit : seulement des nombres.
/// </summary>
internal static class Probe
{
    public static int Run(int seconds)
    {
        var ring = new EventRing(4096);
        using var signal = new AutoResetEvent(false);
        var focus = new FocusChannel();
        using var capture = new RawInputCapture(ring, signal, focus);
        using var process = Process.GetCurrentProcess();
        var cpu = process.TotalProcessorTime;
        capture.Start();

        var buf = new InputEvent[256];
        var tally = new Tally(focus);
        long deadline = Environment.TickCount64 + seconds * 1000L;
        for (long left; (left = deadline - Environment.TickCount64) > 0;)
        {
            signal.WaitOne((int)Math.Min(left, 1000));
            tally.Drain(ring, buf);
        }
        capture.Stop();
        tally.Drain(ring, buf);

        process.Refresh();
        var c = capture.Counters;
        var f = capture.FocusCounters;
        Console.Out.WriteLine($"""
            seconds={seconds}
            keyboard_messages={c.KeyboardMessages} mouse_messages={c.MouseMessages}
            injected_dropped={c.Injected} ignored={c.Ignored} read_errors={c.ReadErrors} callback_errors={c.CallbackErrors}
            written={c.Written} ring_dropped={ring.Dropped} devices={c.Devices}
            key_down={tally.KeyDown} key_up={tally.KeyUp} mouse_down={tally.MouseDown} mouse_up={tally.MouseUp}
            non_monotonic={tally.NonMonotonic}
            focus_events={f.Foreground} name_events={f.NameChanges} focus_changes={f.Changes} excluded={f.Excluded} elevated={f.Elevated}
            focus_markers={tally.Markers} focus_taken={tally.Taken} markers_dropped={f.MarkersDropped} unreadable={f.Unreadable}
            hook_errors={f.HookErrors} focus_callback_errors={f.CallbackErrors}
            private_mb={process.PrivateMemorySize64 / 1048576.0:F1} cpu_ms={(process.TotalProcessorTime - cpu).TotalMilliseconds:F0}
            """);
        return 0;
    }

    private sealed class Tally(FocusChannel focus)
    {
        public long KeyDown, KeyUp, MouseDown, MouseUp, NonMonotonic, Markers, Taken;
        private long _lastMs = long.MinValue;

        public void Drain(EventRing ring, InputEvent[] buf)
        {
            for (int n; (n = ring.Read(buf)) > 0;)
                foreach (ref readonly var e in buf.AsSpan(0, n))
                {
                    if (e.Ms < _lastMs) NonMonotonic++;
                    _lastMs = e.Ms;
                    switch (e.Kind)
                    {
                        case EventKind.KeyDown: KeyDown++; break;
                        case EventKind.KeyUp: KeyUp++; break;
                        case EventKind.MouseDown: MouseDown++; break;
                        case EventKind.MouseUp: MouseUp++; break;
                        case EventKind.Focus:
                            Markers++;
                            if (focus.Take(e) is not null) Taken++;
                            break;
                    }
                }
        }
    }
}
