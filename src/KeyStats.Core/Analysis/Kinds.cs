namespace KeyStats.Core.Analysis;

/// <summary>Nature d'une session. Les valeurs sont enregistrées en base : ne pas les renuméroter.</summary>
public enum SessionKind : byte
{
    /// <summary>Frappes qui ne forment pas une session d'écriture (frappes courtes).</summary>
    None = 0,
    Prose = 1,
    Mixed = 2,
    Code = 3,
}

/// <summary>Type de faute de frappe. Valeurs enregistrées en base.</summary>
public enum ErrorType : byte
{
    /// <summary>Un caractère à la place d'un autre (z au lieu de e).</summary>
    Substitution = 1,
    /// <summary>Caractère en trop.</summary>
    Insertion = 2,
    /// <summary>Caractère oublié.</summary>
    Omission = 3,
    /// <summary>Deux caractères inversés (teh pour the).</summary>
    Transposition = 4,
}

/// <summary>Modificateurs d'un raccourci. Valeurs enregistrées en base.</summary>
[Flags]
public enum ShortcutModifiers : byte
{
    None = 0,
    Ctrl = 1,
    Alt = 2,
    Shift = 4,
    Win = 8,
}

/// <summary>Ce qu'est devenu un épisode de correction (une suite de retours arrière).</summary>
public enum CorrectionOutcome : byte
{
    /// <summary>Le texte effacé n'était pas connu (clic, flèches, début de fenêtre).</summary>
    Unanalyzed = 0,
    /// <summary>Au moins une vraie faute, corrigée.</summary>
    Typo = 1,
    /// <summary>Retapé à l'identique : hésitation, pas de faute.</summary>
    Retyped = 2,
    /// <summary>Suite trop différente du texte effacé : changement d'idée.</summary>
    Reformulation = 3,
}

/// <summary>Nature d'un changement de fenêtre active (colonne <c>focus.flags</c>).</summary>
[Flags]
public enum FocusState : byte
{
    /// <summary>Même fenêtre, nouveau titre ou nouvelle disposition clavier.</summary>
    None = 0,
    /// <summary>Passage à une autre fenêtre.</summary>
    NewWindow = 1,
    /// <summary>Appli exclue : on ne compte que le nombre de touches.</summary>
    Excluded = 2,
    /// <summary>Fenêtre lancée en administrateur : les touches ne nous parviennent sans doute pas.</summary>
    Elevated = 4,
    /// <summary>
    /// Champ de mot de passe au premier plan (détection au mieux par l'agent). Traité comme une appli
    /// exclue : seul le nombre de touches est gardé, ni brut ni statistique de texte.
    /// </summary>
    Secret = 8,
}

public static class FocusStateExtensions
{
    /// <summary>Vrai si seules les touches sont comptées : appli exclue ou champ de mot de passe. L'agent n'écrit alors aucun brut.</summary>
    public static bool CountersOnly(this FocusState state) => (state & (FocusState.Excluded | FocusState.Secret)) != 0;
}

/// <summary>Compteurs journaliers (table <c>day_metric</c>). Valeurs enregistrées en base.</summary>
public enum DayMetric
{
    KeyPresses = 1,
    Repeats = 2,
    CharPresses = 3,
    Overlaps = 4,
    Shortcuts = 5,
    MouseClicks = 6,
    ExcludedKeys = 7,

    CharsTyped = 10,
    Backspaces = 11,
    WordBackspaces = 12,

    Sessions = 20,
    SessionChars = 21,
    SessionActiveMs = 22,
    SessionDurationMs = 23,
    ProseSessions = 24,
    ProseChars = 25,

    Corrections = 30,
    Typos = 31,
    Substitutions = 32,
    Insertions = 33,
    Omissions = 34,
    Transpositions = 35,
    Collateral = 36,
    Unanalyzed = 37,
    Retyped = 38,
    Reformulations = 39,
    HeldBackspace = 40,
    WordDeletes = 41,
    DetectionDelaySumMs = 42,
    DetectionDelayCount = 43,
    /// <summary>Retours arrière répétés par l'appui maintenu (un caractère chacun).</summary>
    BackspaceRepeats = 44,
    /// <summary>Caractères effacés par Ctrl+Retour arrière ; minorant, un appui sur un tampon inconnu compte pour un.</summary>
    WordBackspaceChars = 45,

    /// <summary>Chiffre 0 tapé sur la rangée du haut ; les chiffres 1 à 9 suivent (51 à 59).</summary>
    TopRowDigit0 = 50,
    /// <summary>Chiffre 0 tapé sur le pavé numérique ; les chiffres 1 à 9 suivent (61 à 69).</summary>
    NumpadDigit0 = 60,
}

/// <summary>Histogrammes journaliers (table <c>day_hist</c>). Valeurs enregistrées en base.</summary>
public enum HistogramKind
{
    /// <summary>Durée d'appui, par touche (sous-clé = code de touche).</summary>
    HoldMs = 1,
    /// <summary>Temps entre le relâchement d'une touche de texte et l'appui suivant.</summary>
    FlightMs = 2,
    /// <summary>Nombre de touches enfoncées ensemble à chaque appui (case = nombre).</summary>
    Chord = 3,
    /// <summary>Délai entre une faute et le premier retour arrière qui la corrige.</summary>
    DetectionDelayMs = 4,
}
