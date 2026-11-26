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
