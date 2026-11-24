using KeyStats.Core.Analysis;
using KeyStats.Core.Input;
using KeyStats.Core.Text;

namespace KeyStats.Core.Tests;

/// <summary>
/// Produit le flux d'événements qu'enverrait un clavier AZERTY : Maj autour des majuscules,
/// faux Ctrl gauche avant AltGr comme le fait Windows, touche morte avant les lettres accentuées.
/// </summary>
internal sealed class Typist
{
    /// <summary>Lundi 2 novembre 2026, 10:00 UTC.</summary>
    public const long Start = 1_793_613_600_000;

    private readonly List<InputEvent> _events = [];
    private readonly LayoutTable _layout;

    public Typist(long startMs = Start, LayoutTable? layout = null)
    {
        Ms = startMs;
        _layout = layout ?? LayoutTable.FrenchAzerty;
    }

    /// <summary>Heure du prochain appui.</summary>
    public long Ms { get; set; }

    /// <summary>Écart entre deux appuis successifs.</summary>
    public int Step { get; set; } = 300;

    /// <summary>Durée d'appui d'une touche.</summary>
    public int HoldMs { get; set; } = 80;

    public ushort Device { get; set; } = 1;

    public IReadOnlyList<InputEvent> Events => _events;

    public InputEvent[] ToArray() => [.. _events];

    public Typist Type(string text)
    {
        foreach (char c in text) TypeChar(c);
        return this;
    }

    public Typist TypeChar(char c)
    {
        if (c == '\n') return Key(KeyCode.Enter);
        if (_layout.TryFindKey(c, out ushort key, out int slot) && slot != LayoutTable.NumLockSlot)
            return Chord(key, slot);
        foreach (char dead in "^¨`~")
        {
            foreach (char letter in "aeiouyAEIOUYn")
            {
                if (DeadKeys.Compose(dead, letter) != c) continue;
                FindDeadKey(dead, out ushort deadKey, out int deadSlot);
                Chord(deadKey, deadSlot);
                return TypeChar(letter);
            }
        }
        throw new ArgumentException($"Caractère introuvable sur la disposition : {c}");
    }

    /// <summary>Appui simple sur une touche physique.</summary>
    public Typist Key(ushort key)
    {
        Add(Ms, EventKind.KeyDown, key);
        Add(Ms + HoldMs, EventKind.KeyUp, key);
        Ms += Step;
        return this;
    }

    public Typist Keys(ushort key, int count)
    {
        for (int i = 0; i < count; i++) Key(key);
        return this;
    }

    public Typist Backspace(int count = 1) => Keys(KeyCode.Backspace, count);

    /// <summary>Modificateurs tenus pendant l'appui de la dernière touche (Ctrl+C, Ctrl+Maj+T…).</summary>
    public Typist Combo(params ushort[] keys)
    {
        long t = Ms - 40;
        for (int i = 0; i < keys.Length - 1; i++) Add(t + i, EventKind.KeyDown, keys[i]);
        Add(Ms, EventKind.KeyDown, keys[^1]);
        Add(Ms + HoldMs, EventKind.KeyUp, keys[^1]);
        for (int i = keys.Length - 2; i >= 0; i--) Add(Ms + HoldMs + 10, EventKind.KeyUp, keys[i]);
        Ms += Step;
        return this;
    }

    /// <summary>Touche maintenue : un appui puis <paramref name="repeats"/> répétitions automatiques.</summary>
    public Typist HoldKey(ushort key, int repeats, int intervalMs = 33)
    {
        Add(Ms, EventKind.KeyDown, key);
        long t = Ms + 500;
        for (int i = 0; i < repeats; i++, t += intervalMs) Add(t, EventKind.KeyDown, key);
        Add(t, EventKind.KeyUp, key);
        Ms = t + Step;
        return this;
    }

    /// <summary>Alt tenu, chiffres du pavé, Alt relâché (Alt+0156 → œ).</summary>
    public Typist AltCode(string digits)
    {
        Add(Ms - 40, EventKind.KeyDown, KeyCode.LeftAlt);
        foreach (char d in digits) Key(NumpadKey(d - '0'));
        Add(Ms - Step + HoldMs + 20, EventKind.KeyUp, KeyCode.LeftAlt);
        return this;
    }

    public Typist Down(ushort key)
    {
        Add(Ms, EventKind.KeyDown, key);
        Ms += Step;
        return this;
    }

    public Typist Click()
    {
        Add(Ms, EventKind.MouseDown, MouseButton.Left);
        Add(Ms + HoldMs, EventKind.MouseUp, MouseButton.Left);
        Ms += Step;
        return this;
    }

    public Typist Reset(LockKeys locks = LockKeys.NumLock)
    {
        Add(Ms, EventKind.Reset, 0, flags: (byte)locks);
        Ms += Step;
        return this;
    }

    public Typist ForcedUp(ushort key)
    {
        Add(Ms, EventKind.ForcedUp, key);
        Ms += Step;
        return this;
    }

    public Typist Wait(int ms)
    {
        Ms += ms;
        return this;
    }

    /// <summary>Rejoue le flux dans un analyseur neuf (jours en UTC) et conclut tout.</summary>
    public StatsAccumulator Analyze(AnalysisSettings? settings = null) => Analyze(_events, settings);

    public static StatsAccumulator Analyze(IEnumerable<InputEvent> events, AnalysisSettings? settings = null)
    {
        var stats = new StatsAccumulator(new LocalDayClock(TimeZoneInfo.Utc));
        var analyzer = new Analyzer(stats, settings);
        analyzer.SetFocus(new FocusInfo(Start - 1000, 1, 0, null, FocusState.NewWindow));
        foreach (var e in events) analyzer.Process(e);
        analyzer.EndAll();
        return stats;
    }

    private Typist Chord(ushort key, int slot)
    {
        bool shift = (slot & (int)ShiftState.Shift) != 0, altGr = (slot & (int)ShiftState.AltGr) != 0;
        long before = Ms - 40, after = Ms + HoldMs + 10;
        if (shift) Add(before, EventKind.KeyDown, KeyCode.LeftShift);
        if (altGr)
        {
            // Windows envoie un Ctrl gauche à la même milliseconde que l'Alt de droite.
            Add(before + 1, EventKind.KeyDown, KeyCode.LeftCtrl);
            Add(before + 1, EventKind.KeyDown, KeyCode.RightAlt);
        }
        Add(Ms, EventKind.KeyDown, key);
        Add(Ms + HoldMs, EventKind.KeyUp, key);
        if (altGr)
        {
            Add(after, EventKind.KeyUp, KeyCode.LeftCtrl);
            Add(after, EventKind.KeyUp, KeyCode.RightAlt);
        }
        if (shift) Add(after + 1, EventKind.KeyUp, KeyCode.LeftShift);
        Ms += Step;
        return this;
    }

    private void FindDeadKey(char dead, out ushort key, out int slot)
    {
        foreach (int s in (int[])[0, (int)ShiftState.Shift, (int)ShiftState.AltGr, (int)(ShiftState.AltGr | ShiftState.Shift)])
        {
            for (ushort k = 1; k < KeyCode.ScanCodeLimit; k++)
            {
                if (_layout.Get(k, s, out bool isDead) == dead && isDead)
                {
                    key = k;
                    slot = s;
                    return;
                }
            }
        }
        throw new ArgumentException($"Touche morte introuvable : {dead}");
    }

    private static ushort NumpadKey(int digit) => digit switch
    {
        0 => KeyCode.Num0,
        1 => KeyCode.Num1,
        2 => KeyCode.Num2,
        3 => KeyCode.Num3,
        4 => KeyCode.Num4,
        5 => KeyCode.Num5,
        6 => KeyCode.Num6,
        7 => KeyCode.Num7,
        8 => KeyCode.Num8,
        9 => KeyCode.Num9,
        _ => throw new ArgumentOutOfRangeException(nameof(digit)),
    };

    private void Add(long ms, EventKind kind, ushort key, byte flags = 0)
    {
        var e = new InputEvent(ms, kind, key, kind is EventKind.MouseDown or EventKind.MouseUp ? (ushort)2 : Device, flags);
        // Le flux réel est trié par heure ; à heure égale, l'ordre d'ajout est gardé.
        int i = _events.Count;
        while (i > 0 && _events[i - 1].Ms > ms) i--;
        _events.Insert(i, e);
    }
}
