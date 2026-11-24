namespace KeyStats.Core.Input;

/// <summary>Doigt qui frappe la touche selon le doigté AZERTY standard (indicatif).</summary>
public enum Finger : byte
{
    None = 0,
    LeftPinky, LeftRing, LeftMiddle, LeftIndex, LeftThumb,
    RightThumb, RightIndex, RightMiddle, RightRing, RightPinky,
}

/// <summary>Position d'une touche sur un clavier ISO complet, en largeurs de touche.</summary>
public readonly record struct KeyPlacement(ushort Code, float X, float Y, float Width, float Height, Finger Finger)
{
    public float CenterX => X + Width / 2;
    public float CenterY => Y + Height / 2;
    public bool IsLeftHand => Finger is >= Finger.LeftPinky and <= Finger.LeftThumb;
    public bool IsRightHand => Finger >= Finger.RightThumb;
}

/// <summary>
/// Plan d'un clavier ISO français complet (pavé numérique compris). Sert à la carte de chaleur,
/// au doigté et à reconnaître une faute sur une touche voisine.
/// </summary>
public static class KeyboardGeometry
{
    private static readonly KeyPlacement[] s_keys = Build();
    private static readonly short[] s_index = BuildIndex();

    public static ReadOnlySpan<KeyPlacement> Keys => s_keys;

    public static bool TryFind(ushort code, out KeyPlacement placement)
    {
        int i = code < s_index.Length ? s_index[code] : -1;
        if (i < 0) { placement = default; return false; }
        placement = s_keys[i];
        return true;
    }

    public static Finger FingerOf(ushort code) => TryFind(code, out var p) ? p.Finger : Finger.None;

    /// <summary>
    /// Deux touches distinctes qui se touchent : même rangée côte à côte, ou rangées voisines avec
    /// un décalage horizontal inférieur à une touche.
    /// </summary>
    public static bool AreNeighbors(ushort a, ushort b)
    {
        if (a == b || !TryFind(a, out var pa) || !TryFind(b, out var pb)) return false;
        // Bord à bord : on mesure l'écart entre les rectangles, pas entre les centres,
        // pour que les grandes touches (Entrée, Maj, Espace) aient tous leurs voisins.
        float gapX = Math.Max(0, Math.Max(pa.X, pb.X) - Math.Min(pa.X + pa.Width, pb.X + pb.Width));
        float gapY = Math.Max(0, Math.Max(pa.Y, pb.Y) - Math.Min(pa.Y + pa.Height, pb.Y + pb.Height));
        if (gapX > 0.05f || gapY > 0.05f) return false;
        // Rectangles qui se touchent : il faut aussi un vrai chevauchement sur l'autre axe
        // (pas un simple coin), sauf sur une même rangée.
        float overlapX = Math.Min(pa.X + pa.Width, pb.X + pb.Width) - Math.Max(pa.X, pb.X);
        float overlapY = Math.Min(pa.Y + pa.Height, pb.Y + pb.Height) - Math.Max(pa.Y, pb.Y);
        return overlapY > 0.05f ? overlapX > -0.05f : overlapX > 0.2f;
    }

    private static short[] BuildIndex()
    {
        var index = new short[KeyCode.Count];
        Array.Fill(index, (short)-1);
        for (int i = 0; i < s_keys.Length; i++) index[s_keys[i].Code] = (short)i;
        return index;
    }

    private static KeyPlacement[] Build()
    {
        var k = new List<KeyPlacement>(110);
        void Add(ushort code, float x, float y, float w, Finger f, float h = 1) => k.Add(new(code, x, y, w, h, f));
        void Row(float y, float x, Finger[] fingers, params ushort[] codes)
        {
            for (int i = 0; i < codes.Length; i++) Add(codes[i], x + i, y, 1, fingers[i]);
        }

        const Finger LP = Finger.LeftPinky, LR = Finger.LeftRing, LM = Finger.LeftMiddle, LI = Finger.LeftIndex;
        const Finger RI = Finger.RightIndex, RM = Finger.RightMiddle, RR = Finger.RightRing, RP = Finger.RightPinky;
        const Finger N = Finger.None;

        // Rangée des fonctions.
        Add(KeyCode.Escape, 0, 0, 1, N);
        for (int i = 0; i < 4; i++) Add((ushort)(0x3B + i), 2 + i, 0, 1, N);
        for (int i = 0; i < 4; i++) Add((ushort)(0x3F + i), 6.5f + i, 0, 1, N);
        Add(0x43, 11, 0, 1, N); Add(0x44, 12, 0, 1, N); Add(KeyCode.F11, 13, 0, 1, N); Add(KeyCode.F12, 14, 0, 1, N);
        Add(KeyCode.PrintScreen, 15.25f, 0, 1, N); Add(KeyCode.ScrollLock, 16.25f, 0, 1, N); Add(KeyCode.Pause, 17.25f, 0, 1, N);

        // Rangée des chiffres : ² & é " ' ( - è _ ç à ) =
        Row(1.25f, 0, [LP, LP, LR, LM, LI, LI, RI, RI, RM, RR, RP, RP, RP],
            0x29, 0x02, 0x03, 0x04, 0x05, 0x06, 0x07, 0x08, 0x09, 0x0A, 0x0B, 0x0C, 0x0D);
        Add(KeyCode.Backspace, 13, 1.25f, 2, RP);

        // A Z E R T Y U I O P ^ $
        Add(KeyCode.Tab, 0, 2.25f, 1.5f, LP);
        Row(2.25f, 1.5f, [LP, LR, LM, LI, LI, RI, RI, RM, RR, RP, RP, RP],
            0x10, 0x11, 0x12, 0x13, 0x14, 0x15, 0x16, 0x17, 0x18, 0x19, 0x1A, 0x1B);
        // Entrée ISO en L : on garde la partie basse, la plus étroite.
        Add(KeyCode.Enter, 13.75f, 2.25f, 1.25f, RP, 2);

        // Q S D F G H J K L M ù *
        Add(KeyCode.CapsLock, 0, 3.25f, 1.75f, LP);
        Row(3.25f, 1.75f, [LP, LR, LM, LI, LI, RI, RI, RM, RR, RP, RP, RP],
            0x1E, 0x1F, 0x20, 0x21, 0x22, 0x23, 0x24, 0x25, 0x26, 0x27, 0x28, 0x2B);

        // < W X C V B N , ; : !
        Add(KeyCode.LeftShift, 0, 4.25f, 1.25f, LP);
        Row(4.25f, 1.25f, [LP, LP, LR, LM, LI, LI, RI, RI, RM, RR, RP],
            0x56, 0x2C, 0x2D, 0x2E, 0x2F, 0x30, 0x31, 0x32, 0x33, 0x34, 0x35);
        Add(KeyCode.RightShift, 12.25f, 4.25f, 2.75f, RP);

        // Rangée du bas.
        Add(KeyCode.LeftCtrl, 0, 5.25f, 1.25f, LP);
        Add(KeyCode.LeftWin, 1.25f, 5.25f, 1.25f, N);
        Add(KeyCode.LeftAlt, 2.5f, 5.25f, 1.25f, Finger.LeftThumb);
        Add(KeyCode.Space, 3.75f, 5.25f, 6.25f, Finger.RightThumb);
        Add(KeyCode.RightAlt, 10, 5.25f, 1.25f, Finger.RightThumb);
        Add(KeyCode.RightWin, 11.25f, 5.25f, 1.25f, N);
        Add(KeyCode.Apps, 12.5f, 5.25f, 1.25f, N);
        Add(KeyCode.RightCtrl, 13.75f, 5.25f, 1.25f, RP);

        // Bloc de navigation.
        Add(KeyCode.Insert, 15.25f, 1.25f, 1, N); Add(KeyCode.Home, 16.25f, 1.25f, 1, N); Add(KeyCode.PageUp, 17.25f, 1.25f, 1, N);
        Add(KeyCode.Delete, 15.25f, 2.25f, 1, N); Add(KeyCode.End, 16.25f, 2.25f, 1, N); Add(KeyCode.PageDown, 17.25f, 2.25f, 1, N);
        Add(KeyCode.Up, 16.25f, 4.25f, 1, N);
        Add(KeyCode.Left, 15.25f, 5.25f, 1, N); Add(KeyCode.Down, 16.25f, 5.25f, 1, N); Add(KeyCode.Right, 17.25f, 5.25f, 1, N);

        // Pavé numérique, main droite.
        Add(KeyCode.NumLock, 18.5f, 1.25f, 1, RI); Add(KeyCode.NumDivide, 19.5f, 1.25f, 1, RM);
        Add(KeyCode.NumMultiply, 20.5f, 1.25f, 1, RR); Add(KeyCode.NumMinus, 21.5f, 1.25f, 1, RP);
        Add(KeyCode.Num7, 18.5f, 2.25f, 1, RI); Add(KeyCode.Num8, 19.5f, 2.25f, 1, RM); Add(KeyCode.Num9, 20.5f, 2.25f, 1, RR);
        Add(KeyCode.NumPlus, 21.5f, 2.25f, 1, RP, 2);
        Add(KeyCode.Num4, 18.5f, 3.25f, 1, RI); Add(KeyCode.Num5, 19.5f, 3.25f, 1, RM); Add(KeyCode.Num6, 20.5f, 3.25f, 1, RR);
        Add(KeyCode.Num1, 18.5f, 4.25f, 1, RI); Add(KeyCode.Num2, 19.5f, 4.25f, 1, RM); Add(KeyCode.Num3, 20.5f, 4.25f, 1, RR);
        Add(KeyCode.NumEnter, 21.5f, 4.25f, 1, RP, 2);
        Add(KeyCode.Num0, 18.5f, 5.25f, 2, Finger.RightThumb); Add(KeyCode.NumDecimal, 20.5f, 5.25f, 1, RR);

        return [.. k];
    }
}
