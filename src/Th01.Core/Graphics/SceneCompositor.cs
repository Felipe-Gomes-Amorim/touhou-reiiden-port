namespace Th01.Core.Graphics;

/// <summary>
/// Puts what a session drew on top of a stage background. Sessions that still clear the whole screen and use their own
/// few palette entries (the placeholder boxes) are handled by treating colour 0 of the session's page as transparent and
/// mapping each of its colours to the closest colour of the background palette. Pure software, testable without a window.
/// </summary>
public static class SceneCompositor
{
    /// <summary>Index in <paramref name="palette"/> closest (RGB distance) to the given 4-bit colour.</summary>
    public static int NearestColor(Framebuffer palette, int r4, int g4, int b4)
    {
        int best = 0, bestD = int.MaxValue;
        for (int i = 0; i < Framebuffer.ColorCount; i++)
        {
            palette.GetPalette4(i, out int r, out int g, out int b);
            int d = (r - r4) * (r - r4) + (g - g4) * (g - g4) + (b - b4) * (b - b4);
            if (d < bestD) { bestD = d; best = i; }
        }
        return best;
    }

    /// <summary>
    /// Copies <paramref name="background"/> (pixels, palette) into <paramref name="target"/>, then draws every non-zero pixel
    /// of <paramref name="overlay"/> over it, remapped to the background palette.
    /// </summary>
    public static void Compose(Framebuffer target, Framebuffer background, Framebuffer overlay)
    {
        target.CopyFrom(background);
        Span<byte> map = stackalloc byte[Framebuffer.ColorCount];
        for (int i = 0; i < map.Length; i++)
        {
            overlay.GetPalette4(i, out int r, out int g, out int b);
            map[i] = (byte)NearestColor(background, r, g, b);
        }
        var src = overlay.Pixels;
        for (int y = 0; y < Framebuffer.Height; y++)
            for (int x = 0; x < Framebuffer.Width; x++)
            {
                byte c = src[y * Framebuffer.Width + x];
                if (c != 0) target.SetPixel(x, y, map[c]);
            }
    }

    /// <summary>
    /// Dark panel with centred lines of text (used for the provisional GAME OVER / CLEARED screen).
    /// Colours are chosen from the framebuffer's current palette (darkest / brightest entry).
    /// </summary>
    public static void DrawMessage(Framebuffer fb, IFontSource font, params string[] lines)
    {
        int dark = NearestColor(fb, 0, 0, 0), light = NearestColor(fb, 15, 15, 15);
        int width = 0;
        foreach (var l in lines) width = Math.Max(width, TextRenderer.Measure(font, l));
        int height = lines.Length * (Glyph.Height + 4) + 16;
        int boxW = width + 32, boxX = (Framebuffer.Width - boxW) / 2, boxY = (Framebuffer.Height - height) / 2;
        fb.FillRect(boxX - 2, boxY - 2, boxW + 4, height + 4, (byte)light);
        fb.FillRect(boxX, boxY, boxW, height, (byte)dark);
        for (int i = 0; i < lines.Length; i++)
        {
            int w = TextRenderer.Measure(font, lines[i]);
            TextRenderer.DrawString(fb, font, lines[i], (Framebuffer.Width - w) / 2, boxY + 8 + i * (Glyph.Height + 4), (byte)light);
        }
    }
}
