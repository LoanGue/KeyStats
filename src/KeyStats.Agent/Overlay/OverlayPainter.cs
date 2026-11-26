using KeyStats.Agent.Interop;
using KeyStats.Core.Analysis;

namespace KeyStats.Agent.Overlay;

/// <summary>
/// Dessine le compteur dans une image 32 bits et la pose sur la fenêtre calque (alpha par pixel).
/// GDI écrit le texte en blanc sur noir sans toucher l'alpha : sa luminosité donne la couverture,
/// composée ensuite sur un fond sombre aux coins arrondis. Quatre images par seconde au plus,
/// sur le thread de l'interface : les objets GDI sont créés puis libérés à chaque image.
/// </summary>
internal static unsafe class OverlayPainter
{
    private const uint Background = 0x1C1F24;
    private const float BackgroundAlpha = 0.86f, Radius = 0.22f;

    /// <returns>Faux si GDI a refusé une étape ; la fenêtre garde alors l'image précédente.</returns>
    public static bool Paint(nint hwnd, int x, int y, int width, int height, int dpi, LiveText text)
    {
        nint dc = OverlayApi.CreateCompatibleDC(0);
        if (dc == 0) return false;
        nint bitmap = 0, mainFont = 0, detailFont = 0;
        try
        {
            var info = new BITMAPINFOHEADER { biSize = (uint)sizeof(BITMAPINFOHEADER), biWidth = width, biHeight = -height, biPlanes = 1, biBitCount = 32 };
            void* bits;
            bitmap = OverlayApi.CreateDIBSection(dc, &info, 0, &bits, 0, 0);
            if (bitmap == 0) return false;
            _ = OverlayApi.SelectObject(dc, bitmap);
            mainFont = Fit(dc, text.Main, LiveDisplay.Scale(22, dpi), width - 2 * LiveDisplay.Scale(10, dpi));
            detailFont = Font(LiveDisplay.Scale(12, dpi), OverlayApi.FW_NORMAL);
            if (mainFont == 0 || detailFont == 0) return false;

            _ = OverlayApi.SetBkMode(dc, OverlayApi.TRANSPARENT);
            _ = OverlayApi.SetTextColor(dc, 0xFFFFFF);
            int split = height * 62 / 100;
            Draw(dc, mainFont, text.Main, 0, LiveDisplay.Scale(4, dpi), width, split);
            Draw(dc, detailFont, text.Detail, 0, split - LiveDisplay.Scale(2, dpi), width, height - LiveDisplay.Scale(4, dpi));
            _ = OverlayApi.GdiFlush();
            Compose((uint*)bits, width, height);

            var dst = new POINT { X = x, Y = y };
            var src = new POINT();
            var size = new SIZE { Cx = width, Cy = height };
            var blend = new BLENDFUNCTION { BlendOp = OverlayApi.AC_SRC_OVER, SourceConstantAlpha = 255, AlphaFormat = OverlayApi.AC_SRC_ALPHA };
            return OverlayApi.UpdateLayeredWindow(hwnd, 0, &dst, &size, dc, &src, 0, &blend, OverlayApi.ULW_ALPHA) != 0;
        }
        finally
        {
            _ = OverlayApi.DeleteDC(dc);
            if (bitmap != 0) _ = TrayApi.DeleteObject(bitmap);
            if (mainFont != 0) _ = TrayApi.DeleteObject(mainFont);
            if (detailFont != 0) _ = TrayApi.DeleteObject(detailFont);
        }
    }

    /// <summary>Police principale, réduite par pas de 1 px jusqu'à ce que le texte tienne dans la largeur (bilan).</summary>
    private static nint Fit(nint dc, string text, int pixels, int room)
    {
        for (int size = pixels; ; size--)
        {
            nint font = Font(size, OverlayApi.FW_SEMIBOLD);
            if (font == 0 || size <= pixels * 2 / 3 || Measure(dc, font, text) <= room) return font;
            _ = TrayApi.DeleteObject(font);
        }
    }

    private static int Measure(nint dc, nint font, string text)
    {
        _ = OverlayApi.SelectObject(dc, font);
        var rect = new RECT();
        fixed (char* chars = text)
            _ = OverlayApi.DrawText(dc, chars, text.Length, &rect, OverlayApi.DT_CALCRECT | OverlayApi.DT_SINGLELINE | OverlayApi.DT_NOPREFIX);
        return rect.Right - rect.Left;
    }

    private static nint Font(int pixels, int weight)
    {
        fixed (char* face = "Segoe UI")
            return OverlayApi.CreateFont(-pixels, 0, 0, 0, weight, 0, 0, 0, OverlayApi.DEFAULT_CHARSET, 0, 0,
                OverlayApi.ANTIALIASED_QUALITY, 0, face);
    }

    private static void Draw(nint dc, nint font, string text, int left, int top, int right, int bottom)
    {
        if (text.Length == 0) return;
        _ = OverlayApi.SelectObject(dc, font);
        var rect = new RECT { Left = left, Top = top, Right = right, Bottom = bottom };
        fixed (char* chars = text)
            _ = OverlayApi.DrawText(dc, chars, text.Length, &rect,
                OverlayApi.DT_CENTER | OverlayApi.DT_VCENTER | OverlayApi.DT_SINGLELINE | OverlayApi.DT_NOPREFIX);
    }

    /// <summary>Texte (couverture <c>c</c>) sur le fond, en alpha prémultiplié comme l'attend UpdateLayeredWindow.</summary>
    private static void Compose(uint* pixels, int width, int height)
    {
        float r = height * Radius;
        for (int py = 0; py < height; py++)
            for (int px = 0; px < width; px++)
            {
                uint p = pixels[py * width + px];
                float c = Math.Max((p >> 16) & 0xFF, Math.Max((p >> 8) & 0xFF, p & 0xFF)) / 255f;
                float bg = BackgroundAlpha * RoundRectCoverage(px + 0.5f, py + 0.5f, width, height, r);
                float alpha = c + bg * (1 - c);
                uint a = (uint)(alpha * 255 + 0.5f);
                uint Channel(int shift) => (uint)(((Background >> shift) & 0xFF) * bg * (1 - c) + 255 * c + 0.5f);
                pixels[py * width + px] = (a << 24) | (Channel(16) << 16) | (Channel(8) << 8) | Channel(0);
            }
    }

    /// <summary>Part du pixel de centre (x, y) dans le rectangle arrondi, lissée sur un pixel.</summary>
    private static float RoundRectCoverage(float x, float y, int width, int height, float r)
    {
        float dx = Math.Max(Math.Max(r - x, x - (width - r)), 0), dy = Math.Max(Math.Max(r - y, y - (height - r)), 0);
        float outside = MathF.Sqrt(dx * dx + dy * dy) - r;
        return Math.Clamp(0.5f - outside, 0, 1);
    }
}
