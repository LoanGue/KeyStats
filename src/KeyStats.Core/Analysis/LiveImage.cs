namespace KeyStats.Core.Analysis;

/// <summary>
/// Image du compteur : texte blanc sur noir (sa luminosité donne la couverture) posé sur un fond sombre
/// aux coins arrondis, en ARGB à alpha prémultiplié comme l'attend une fenêtre calque.
/// </summary>
public static class LiveImage
{
    public const uint Background = 0x1C1F24;
    public const float BackgroundAlpha = 0.86f, Radius = 0.22f;

    /// <summary>Remplace sur place chaque pixel de texte (canal alpha ignoré) par le pixel final.</summary>
    public static void Compose(Span<uint> pixels, int width, int height)
    {
        float r = height * Radius;
        for (int py = 0; py < height; py++)
            for (int px = 0; px < width; px++)
            {
                uint p = pixels[py * width + px];
                float c = Math.Max((p >> 16) & 0xFF, Math.Max((p >> 8) & 0xFF, p & 0xFF)) / 255f;
                float bg = BackgroundAlpha * RoundRectCoverage(px + 0.5f, py + 0.5f, width, height, r);
                uint a = (uint)((c + bg * (1 - c)) * 255 + 0.5f);
                uint Channel(int shift) => (uint)(((Background >> shift) & 0xFF) * bg * (1 - c) + 255 * c + 0.5f);
                pixels[py * width + px] = (a << 24) | (Channel(16) << 16) | (Channel(8) << 8) | Channel(0);
            }
    }

    /// <summary>Part du pixel de centre (x, y) dans le rectangle arrondi, lissée sur un pixel.</summary>
    public static float RoundRectCoverage(float x, float y, int width, int height, float r)
    {
        float dx = Math.Max(Math.Max(r - x, x - (width - r)), 0), dy = Math.Max(Math.Max(r - y, y - (height - r)), 0);
        float outside = MathF.Sqrt(dx * dx + dy * dy) - r;
        return Math.Clamp(0.5f - outside, 0, 1);
    }
}
