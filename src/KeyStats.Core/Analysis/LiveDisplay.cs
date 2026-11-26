using System.Globalization;

namespace KeyStats.Core.Analysis;

/// <summary>Rectangle en pixels d'écran, bords droit et bas exclus.</summary>
public readonly record struct ScreenRect(int Left, int Top, int Right, int Bottom);

/// <summary>Ce que le compteur affiche : une ligne principale, une ligne secondaire plus petite.</summary>
public readonly record struct LiveText(string Main, string Detail);

/// <summary>Mise en forme et position du compteur en direct, sans dépendance Windows.</summary>
public static class LiveDisplay
{
    /// <summary>Taille et marge du compteur à 96 ppp.</summary>
    public const int Width = 150, Height = 52, Margin = 12;

    /// <summary>Vitesse en cours (wpm, cpm en petit), ou bilan de la rafale (wpm et précision).</summary>
    public static LiveText Text(LiveMode mode, double wpm, double cpm, double accuracy) => mode switch
    {
        LiveMode.Typing => new($"{Round(wpm)} wpm", $"{Round(cpm)} cpm"),
        LiveMode.Summary => new($"{Round(wpm)} wpm · {Round(accuracy * 100)}\u00A0%", "bilan"),
        _ => new("", ""),
    };

    /// <summary>Confirmation d'un clic sur l'icône : pause restante, arrondie à la minute supérieure.</summary>
    public static LiveText Snooze(long remainingMs) =>
        new($"\u23F8 {Math.Max(1, (remainingMs + 59_999) / 60_000).ToString(CultureInfo.InvariantCulture)}\u00A0min", "en pause");

    /// <summary>
    /// États de <c>SHQueryUserNotificationState</c> où le compteur se tait : application en plein écran ou réglages de présentation (2),
    /// jeu ou vidéo Direct3D en plein écran (3), mode présentation (4).
    /// </summary>
    public static bool Quiet(int notificationState) => notificationState is 2 or 3 or 4;

    /// <summary>
    /// Texte d'une image du compteur : pendant la frappe, vitesse lissée, le cpm suivant le même lissage ;
    /// le bilan tel quel.
    /// </summary>
    public static LiveText Frame(LiveReading reading, LiveSmoother smoother, long now)
    {
        if (reading.Mode != LiveMode.Typing) return Text(reading.Mode, reading.Wpm, reading.Cpm, reading.Accuracy);
        double wpm = smoother.Next(reading.Wpm, now);
        double cpm = reading.Wpm > 0 ? reading.Cpm * wpm / reading.Wpm : reading.Cpm;
        return Text(reading.Mode, wpm, cpm, reading.Accuracy);
    }

    /// <summary>Coin bas droit de la zone de travail, donc au-dessus de la barre des tâches.</summary>
    public static (int X, int Y) BottomRight(ScreenRect work, int width, int height, int margin) =>
        (work.Right - width - margin, work.Bottom - height - margin);

    /// <summary>Une mesure à 96 ppp, mise à l'échelle de l'écran.</summary>
    public static int Scale(int value, int dpi) => (value * dpi + 48) / 96;

    private static string Round(double value) =>
        ((int)Math.Round(Math.Max(0, value), MidpointRounding.AwayFromZero)).ToString(CultureInfo.InvariantCulture);
}

/// <summary>
/// Lissage exponentiel de la vitesse affichée (constante de temps 1 s) : le chiffre suit la frappe
/// sans sauter à chaque touche. Repart de la première valeur après <see cref="Reset"/>.
/// </summary>
public sealed class LiveSmoother
{
    public const double TimeConstantMs = 1000;

    private double _value;
    private long _lastMs;
    private bool _started;

    public void Reset() => _started = false;

    public double Next(double target, long now)
    {
        if (!_started)
        {
            _started = true;
            _lastMs = now;
            return _value = target;
        }
        double dt = Math.Max(0, now - _lastMs);
        _lastMs = now;
        _value += (target - _value) * (1 - Math.Exp(-dt / TimeConstantMs));
        return _value;
    }
}
