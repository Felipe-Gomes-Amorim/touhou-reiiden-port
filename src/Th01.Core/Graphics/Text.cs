namespace Th01.Core.Graphics;

/// <summary>One bitmap glyph: <see cref="Width"/> (8 or 16) by 16 rows, rows packed MSB first, ceil(Width/8) bytes per row.</summary>
public readonly record struct Glyph(int Width, ReadOnlyMemory<byte> Rows)
{
    public const int Height = 16;
}

/// <summary>Source of bitmap glyphs. Deliberately independent of the game's own font data.</summary>
public interface IFontSource
{
    bool TryGetGlyph(int codePoint, out Glyph glyph);
}

/// <summary>Draws text with an <see cref="IFontSource"/> straight into a framebuffer (8x16 half-width, 16x16 full-width).</summary>
public static class TextRenderer
{
    /// <summary>
    /// Draws <paramref name="text"/> starting at (x, y). <paramref name="background"/> &lt; 0 leaves the background untouched.
    /// Characters without a glyph advance by 8 dots and draw nothing. Returns the x position after the last character.
    /// </summary>
    public static int DrawString(Framebuffer fb, IFontSource font, string text, int x, int y, byte color, int background = -1)
    {
        foreach (var rune in text.EnumerateRunes())
        {
            if (!font.TryGetGlyph(rune.Value, out var g)) { x += 8; continue; }
            DrawGlyph(fb, g, x, y, color, background);
            x += g.Width;
        }
        return x;
    }

    public static int Measure(IFontSource font, string text)
    {
        int w = 0;
        foreach (var rune in text.EnumerateRunes())
            w += font.TryGetGlyph(rune.Value, out var g) ? g.Width : 8;
        return w;
    }

    static void DrawGlyph(Framebuffer fb, Glyph g, int x, int y, byte color, int background)
    {
        int bytesPerRow = (g.Width + 7) / 8;
        var rows = g.Rows.Span;
        for (int row = 0; row < Glyph.Height; row++)
            for (int col = 0; col < g.Width; col++)
            {
                bool on = (rows[row * bytesPerRow + col / 8] & (0x80 >> (col & 7))) != 0;
                if (on) fb.SetPixel(x + col, y + row, color);
                else if (background >= 0) fb.SetPixel(x + col, y + row, (byte)background);
            }
    }
}
