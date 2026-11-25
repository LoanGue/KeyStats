using System.Diagnostics;
using KeyStats.Agent.Recording;
using KeyStats.Core.Analysis;
using KeyStats.Core.Focus;
using KeyStats.Core.Input;
using KeyStats.Core.Recording;

namespace KeyStats.Agent.Capture;

/// <summary>
/// <c>--probe &lt;secondes&gt; [--db &lt;chemin&gt;]</c> : capture pendant la durée donnée, en enregistrant
/// dans la base donnée si <c>--db</c> est passé, puis écrit des compteurs sur la sortie standard.
/// Ni touche, ni caractère, ni appli, ni titre n'est écrit : seulement des nombres.
/// </summary>
internal static class Probe
{
    public static int Run(int seconds, string? dbPath)
    {
        var ring = new EventRing(4096);
        using var signal = new AutoResetEvent(false);
        var focus = new FocusChannel();
        using var capture = new RawInputCapture(ring, signal, focus);
        using var process = Process.GetCurrentProcess();
        var cpu = process.TotalProcessorTime;
        string extra = dbPath is null ? Count(seconds, ring, signal, focus, capture) : Record(seconds, dbPath, ring, signal, focus, capture);

        process.Refresh();
        var c = capture.Counters;
        var f = capture.FocusCounters;
        Console.Out.WriteLine($"""
            seconds={seconds}
            keyboard_messages={c.KeyboardMessages} mouse_messages={c.MouseMessages}
            injected_dropped={c.Injected} ignored={c.Ignored} read_errors={c.ReadErrors} callback_errors={c.CallbackErrors}
            written={c.Written} ring_dropped={ring.Dropped} devices={c.Devices}
            focus_events={f.Foreground} name_events={f.NameChanges} focus_changes={f.Changes} excluded={f.Excluded} elevated={f.Elevated}
            markers_dropped={f.MarkersDropped} unreadable={f.Unreadable} hook_errors={f.HookErrors} focus_callback_errors={f.CallbackErrors}
            {extra}
            private_mb={process.PrivateMemorySize64 / 1048576.0:F1} cpu_ms={(process.TotalProcessorTime - cpu).TotalMilliseconds:F0}
            """);
        return 0;
    }

    /// <summary>Capture seule : compte les événements lus dans la file.</summary>
    private static string Count(int seconds, EventRing ring, AutoResetEvent signal, FocusChannel focus, RawInputCapture capture)
    {
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
        return $"""
            key_down={tally.KeyDown} key_up={tally.KeyUp} mouse_down={tally.MouseDown} mouse_up={tally.MouseUp} non_monotonic={tally.NonMonotonic}
            focus_markers={tally.Markers} focus_taken={tally.Taken}
            """;
    }

    /// <summary>Chaîne complète vers la base donnée : analyse et écriture par lots.</summary>
    private static string Record(int seconds, string dbPath, EventRing ring, AutoResetEvent signal, FocusChannel focus, RawInputCapture capture)
    {
        var storage = AgentStorage.Open(dbPath);
        var recorder = new Recorder(storage.Db, storage.Archive, new LocalDayClock(), capture.Clock.PeekMs());
        var loop = new AnalysisLoop(new RingPump(ring, focus, recorder), capture.Clock, signal);
        bool stopped = false;
        try
        {
            loop.Start();
            capture.Start();
            Thread.Sleep(TimeSpan.FromSeconds(seconds));
        }
        finally
        {
            capture.Stop();
            stopped = loop.Stop(TimeSpan.FromSeconds(10));
            // Sans arrêt confirmé, le thread d'analyse peut encore écrire : la base reste ouverte.
            if (stopped) storage.Dispose();
        }
        var l = loop.Counters;
        var r = recorder.Counters;
        return $"""
            encrypted={storage.Archive is not null} title_key_replaced={storage.TitleKeyReplaced} loop_stopped={stopped} closed={loop.Closed}
            loop_events={l.Events} loop_errors={l.Errors} tick_errors={loop.TickErrors} unknown_markers={l.UnknownMarkers}
            batches={r.Batches} write_errors={r.WriteErrors} raw_dropped={r.RawDropped}
            """;
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
