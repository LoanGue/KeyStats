using KeyStats.Agent.Interop;

namespace KeyStats.Agent.Tray;

/// <summary>
/// Dessine l'icône en code, à la taille demandée : un clavier, bleu quand l'enregistrement tourne,
/// gris avec un badge pause sinon. Formes en coordonnées 0..1, lissées par suréchantillonnage 4×4.
/// </summary>
internal static unsafe class IconArt
{
    private const uint Active = 0x2F6FDB, Idle = 0x8A8F98, Badge = 0xE08A1E, Key = 0xFFFFFF;

    /// <returns>L'icône, ou 0 si GDI a refusé de la créer.</returns>
    public static nint Create(int size, bool paused)
    {
        var pixels = new uint[size * size];
        for (int y = 0; y < size; y++)
            for (int x = 0; x < size; x++)
                pixels[y * size + x] = Pixel(x, y, size, paused);
        var mask = new byte[((size + 15) / 16) * 2 * size];
        fixed (uint* color = pixels)
        fixed (byte* bits = mask)
        {
            var info = new ICONINFO { fIcon = 1, hbmMask = TrayApi.CreateBitmap(size, size, 1, 1, bits), hbmColor = TrayApi.CreateBitmap(size, size, 1, 32, color) };
            nint icon = info.hbmMask != 0 && info.hbmColor != 0 ? TrayApi.CreateIconIndirect(&info) : 0;
            if (info.hbmMask != 0) _ = TrayApi.DeleteObject(info.hbmMask);
            if (info.hbmColor != 0) _ = TrayApi.DeleteObject(info.hbmColor);
            return icon;
        }
    }

    private static uint Pixel(int px, int py, int size, bool paused)
    {
        float r = 0, g = 0, b = 0, a = 0;
        for (int sy = 0; sy < 4; sy++)
            for (int sx = 0; sx < 4; sx++)
            {
                float x = (px + (sx + 0.5f) / 4) / size, y = (py + (sy + 0.5f) / 4) / size;
                uint c = Sample(x, y, paused);
                if (c == uint.MaxValue) continue;
                r += (c >> 16) & 0xFF; g += (c >> 8) & 0xFF; b += c & 0xFF; a += 1;
            }
        if (a == 0) return 0;
        // Alpha non prémultiplié, comme dans un fichier .ico : Windows prémultiplie lui-même.
        // La couleur est la moyenne des seuls échantillons couverts ; la couverture donne l'opacité.
        uint alpha = (uint)(a * 255 / 16);
        return (alpha << 24) | ((uint)(r / a) << 16) | ((uint)(g / a) << 8) | (uint)(b / a);
    }

    /// <summary>Couleur au point (x, y), <see cref="uint.MaxValue"/> si transparent.</summary>
    private static uint Sample(float x, float y, bool paused)
    {
        if (paused && InCircle(x, y, 0.72f, 0.72f, 0.28f))
            return InRect(x, y, 0.60f, 0.58f, 0.68f, 0.86f) || InRect(x, y, 0.76f, 0.58f, 0.84f, 0.86f) ? Key : Badge;
        if (!InRoundRect(x, y, 0.02f, 0.18f, 0.98f, 0.82f, 0.12f)) return uint.MaxValue;
        bool key = InKeyRow(x, y, 0.28f, 5) || InKeyRow(x, y, 0.44f, 4) || InRect(x, y, 0.26f, 0.60f, 0.74f, 0.70f);
        return key ? Key : paused ? Idle : Active;
    }

    private static bool InKeyRow(float x, float y, float top, int keys)
    {
        if (y < top || y > top + 0.10f) return false;
        float left = 0.5f - keys * 0.08f;
        float cell = (x - left) / 0.16f;
        return cell >= 0 && cell < keys && cell - MathF.Floor(cell) < 0.65f;
    }

    private static bool InRect(float x, float y, float x0, float y0, float x1, float y1) => x >= x0 && x <= x1 && y >= y0 && y <= y1;

    private static bool InCircle(float x, float y, float cx, float cy, float r) => (x - cx) * (x - cx) + (y - cy) * (y - cy) <= r * r;

    private static bool InRoundRect(float x, float y, float x0, float y0, float x1, float y1, float r)
    {
        if (!InRect(x, y, x0, y0, x1, y1)) return false;
        float dx = MathF.Max(MathF.Max(x0 + r - x, x - (x1 - r)), 0), dy = MathF.Max(MathF.Max(y0 + r - y, y - (y1 - r)), 0);
        return dx * dx + dy * dy <= r * r;
    }
}
