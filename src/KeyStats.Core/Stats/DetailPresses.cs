using KeyStats.Core.Input;

namespace KeyStats.Core.Stats;

/// <summary>Un appui de touche de la frise « Détail à la milliseconde ». Aucun caractère : la touche physique seulement.</summary>
/// <param name="Key">Touche physique (<see cref="KeyCode"/>).</param>
/// <param name="DownMs">Enfoncement, en ms depuis le début de la session.</param>
/// <param name="HoldMs">Durée d'appui ; null si le relâchement n'a pas été vu (remise à zéro, fin du brut).</param>
/// <param name="OverlapMs">
/// Chevauchement avec l'appui précédent : de cet enfoncement au premier des deux relâchements ; null sans chevauchement.
/// </param>
/// <param name="Erased">Effacée ensuite par un retour arrière.</param>
public sealed record DetailPress(int Key, int DownMs, int? HoldMs, int? OverlapMs, bool Erased);

/// <summary>
/// Appuis d'une session, relus dans le brut déchiffré : modificateurs et verrous exclus, répétitions automatiques
/// fondues dans leur appui. Les touches effacées suivent un modèle simple, sans disposition ni texte : chaque touche
/// de caractère empile son appui, un retour arrière dépile le dernier (Ctrl : jusqu'à l'espace précédent), et tout ce
/// qui déplace le curseur (flèches, Suppr, Tab, clic, raccourci) vide la pile, car la suite n'efface plus ces touches.
/// </summary>
public static class DetailPresses
{
    private struct Press
    {
        public ushort Key;
        public long DownMs;
        public long? UpMs;
        public bool Erased;
    }

    /// <summary>
    /// Appuis du clavier <paramref name="device"/> enfoncés dans [<paramref name="startMs"/>, <paramref name="endMs"/>],
    /// <paramref name="max"/> au plus. Les événements suivants servent aux relâchements et aux effacements.
    /// </summary>
    public static (List<DetailPress> Presses, bool Truncated) Extract(IEnumerable<InputEvent> events, int device, long startMs, long endMs, int max)
    {
        ArgumentNullException.ThrowIfNull(events);
        var presses = new List<Press>();
        var held = new Dictionary<ushort, int>();
        var typed = new Stack<int>();
        bool ctrl = false, leftAlt = false, rightAlt = false, truncated = false;
        foreach (var e in events)
        {
            if (e.Kind == EventKind.MouseDown)
            {
                typed.Clear();
                continue;
            }
            if (e.Device != device) continue;
            if (e.Kind == EventKind.Reset)
            {
                held.Clear();
                typed.Clear();
                ctrl = leftAlt = rightAlt = false;
                continue;
            }
            if (!e.IsKeyboard) continue;
            bool down = e.Kind == EventKind.KeyDown;
            if (KeyCode.IsModifier(e.Key))
            {
                if (e.Key is KeyCode.LeftCtrl or KeyCode.RightCtrl) ctrl = down;
                else if (e.Key == KeyCode.LeftAlt) leftAlt = down;
                else if (e.Key == KeyCode.RightAlt) rightAlt = down;
                continue;
            }
            if (KeyCode.IsLock(e.Key)) continue;
            if (!down)
            {
                if (held.Remove(e.Key, out int index) && index >= 0)
                {
                    var p = presses[index];
                    p.UpMs = e.Ms;
                    presses[index] = p;
                }
                continue;
            }
            // Une touche déjà tenue se répète : même appui. Hors de la session ou au-delà du plafond, l'appui vaut -1.
            if (!held.TryGetValue(e.Key, out int at))
            {
                at = -1;
                if (e.Ms >= startMs && e.Ms <= endMs && !truncated)
                {
                    if (presses.Count == max) truncated = true;
                    else
                    {
                        at = presses.Count;
                        presses.Add(new Press { Key = e.Key, DownMs = e.Ms });
                    }
                }
                held[e.Key] = at;
            }
            // AltGr arrive précédé d'un faux Ctrl gauche : avec Alt droit tenu, Ctrl n'est pas un raccourci.
            if (e.Ms <= endMs) Edit(presses, typed, e.Key, at, ctrl && !rightAlt, leftAlt && !rightAlt);
        }
        return (Results(presses, startMs), truncated);
    }

    /// <summary>Effet d'un enfoncement (ou d'une répétition) sur la pile des touches effaçables ; <paramref name="at"/> : appui de la frise, ou -1.</summary>
    private static void Edit(List<Press> presses, Stack<int> typed, ushort key, int at, bool ctrl, bool alt)
    {
        if (key == KeyCode.Backspace && !alt)
        {
            if (!ctrl) Erase(presses, typed);
            else
            {
                while (typed.TryPeek(out int top) && IsSpace(presses, top)) Erase(presses, typed);
                while (typed.TryPeek(out int top) && !IsSpace(presses, top)) Erase(presses, typed);
            }
        }
        else if (IsCharacter(key) && !ctrl && !alt) typed.Push(at);
        else typed.Clear();
    }

    private static bool IsSpace(List<Press> presses, int index) => index >= 0 && presses[index].Key == KeyCode.Space;

    private static void Erase(List<Press> presses, Stack<int> typed)
    {
        if (!typed.TryPop(out int index) || index < 0) return;
        var p = presses[index];
        p.Erased = true;
        presses[index] = p;
    }

    /// <summary>Touche qui écrit un caractère (lettres, chiffres, ponctuation, espace, Entrée, pavé numérique).</summary>
    internal static bool IsCharacter(ushort key) =>
        key is KeyCode.NumEnter or KeyCode.NumDivide
        || (key < KeyCode.E0 && key is not (KeyCode.Escape or KeyCode.Backspace or KeyCode.Tab)
            && key is not (>= KeyCode.F1 and <= KeyCode.F10) and not KeyCode.F11 and not KeyCode.F12);

    private static List<DetailPress> Results(List<Press> presses, long startMs)
    {
        var list = new List<DetailPress>(presses.Count);
        for (int i = 0; i < presses.Count; i++)
        {
            var p = presses[i];
            int? overlap = null;
            if (i > 0 && presses[i - 1].UpMs is long previousUp && previousUp > p.DownMs)
                overlap = (int)(Math.Min(previousUp, p.UpMs ?? previousUp) - p.DownMs);
            list.Add(new DetailPress(p.Key, (int)(p.DownMs - startMs), p.UpMs is long up ? (int)(up - p.DownMs) : null,
                overlap > 0 ? overlap : null, p.Erased));
        }
        return list;
    }
}
