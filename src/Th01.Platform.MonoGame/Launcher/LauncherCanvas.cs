using Th01.Core.Graphics;

namespace Th01.Platform.MonoGame.Launcher;

/// <summary>Colours of the launcher design (0xAARRGGBB).</summary>
public static class LauncherColors
{
    public const uint Background = 0xFF080616, White = 0xFFF4F4FF, Red = 0xFFFF4455, Cyan = 0xFF4DF0FF, Yellow = 0xFFFFE14D, Green = 0xFF4DFF91;
    public const uint Purple1 = 0xFF1B1650, Purple2 = 0xFF3A3670, Purple3 = 0xFFA9A6D6, Star = 0xFF5A56A8;
    public const uint Panel = 0xFF0E0B2A, Disabled = 0xFF8B88BD, PinkText = 0xFFFF9AA5, Dark = 0xFF080616;
}

/// <summary>
/// The launcher's own 1280x720 picture (separate from the 640x400 game framebuffer). Pixels are 0xAARRGGBB with the alpha
/// ignored on the destination (always opaque). Text comes from an <see cref="IFontSource"/> at a whole-number scale.
/// </summary>
public sealed class LauncherCanvas
{
    public const int Width = 1280, Height = 720;

    public uint[] Pixels { get; } = new uint[Width * Height];

    public void CopyFrom(uint[] source) => Array.Copy(source, Pixels, Pixels.Length);

    public uint Get(int x, int y) => Pixels[y * Width + x];

    /// <summary>Blends one pixel over the destination; <paramref name="alpha"/> 0..255.</summary>
    public void Put(int x, int y, uint color, int alpha = 255)
    {
        if ((uint)x >= Width || (uint)y >= Height || alpha <= 0) return;
        ref uint d = ref Pixels[y * Width + x];
        if (alpha >= 255) { d = color | 0xFF000000; return; }
        d = Mix(d, color, alpha);
    }

    public static uint Mix(uint dst, uint src, int alpha)
    {
        int ia = 255 - alpha;
        uint r = (uint)((((src >> 16) & 0xFF) * alpha + ((dst >> 16) & 0xFF) * ia) / 255);
        uint g = (uint)((((src >> 8) & 0xFF) * alpha + ((dst >> 8) & 0xFF) * ia) / 255);
        uint b = (uint)(((src & 0xFF) * alpha + (dst & 0xFF) * ia) / 255);
        return 0xFF000000 | (r << 16) | (g << 8) | b;
    }

    public void FillRect(int x, int y, int w, int h, uint color, int alpha = 255)
    {
        int x0 = Math.Max(x, 0), y0 = Math.Max(y, 0), x1 = Math.Min(x + w, Width), y1 = Math.Min(y + h, Height);
        for (int yy = y0; yy < y1; yy++)
            for (int xx = x0; xx < x1; xx++) Put(xx, yy, color, alpha);
    }

    /// <summary>Rectangle outline of the given thickness, drawn inside the box.</summary>
    public void StrokeRect(int x, int y, int w, int h, int thick, uint color, int alpha = 255)
    {
        FillRect(x, y, w, thick, color, alpha);
        FillRect(x, y + h - thick, w, thick, color, alpha);
        FillRect(x, y + thick, thick, h - 2 * thick, color, alpha);
        FillRect(x + w - thick, y + thick, thick, h - 2 * thick, color, alpha);
    }

    /// <summary>Dashed outline (the disabled button).</summary>
    public void DashedRect(int x, int y, int w, int h, int thick, uint color, int dash = 6)
    {
        for (int i = 0; i < w; i += 2 * dash)
        {
            FillRect(x + i, y, Math.Min(dash, w - i), thick, color);
            FillRect(x + i, y + h - thick, Math.Min(dash, w - i), thick, color);
        }
        for (int i = 0; i < h; i += 2 * dash)
        {
            FillRect(x, y + i, thick, Math.Min(dash, h - i), color);
            FillRect(x + w - thick, y + i, thick, Math.Min(dash, h - i), color);
        }
    }

    /// <summary>Filled circle, or a ring when <paramref name="thick"/> is above 0.</summary>
    public void Circle(int cx, int cy, int r, uint color, int thick = 0)
    {
        for (int y = -r; y <= r; y++)
            for (int x = -r; x <= r; x++)
            {
                int d2 = x * x + y * y;
                if (d2 > r * r) continue;
                if (thick > 0 && d2 < (r - thick) * (r - thick)) continue;
                Put(cx + x, cy + y, color);
            }
    }

    /// <summary>The yin-yang orb of the design (red right half, two dots), optionally with a dark outline.</summary>
    public void YinYang(int cx, int cy, int r, int outline = 0)
    {
        if (outline > 0) Circle(cx, cy, r + outline, LauncherColors.Dark);
        double h = r / 2.0;
        for (int y = -r; y <= r; y++)
            for (int x = -r; x <= r; x++)
            {
                if (x * x + y * y > r * r) continue;
                double dxu = x, dyu = y + h, dyl = y - h;
                double dot = Math.Max(1.5, r * 0.15);
                uint c;
                if (dxu * dxu + dyu * dyu <= dot * dot) c = LauncherColors.Red;               // dot in the upper (white) bulb
                else if (dxu * dxu + dyl * dyl <= dot * dot) c = LauncherColors.White;        // dot in the lower (red) bulb
                else if (dxu * dxu + dyu * dyu <= h * h) c = LauncherColors.White;
                else if (dxu * dxu + dyl * dyl <= h * h) c = LauncherColors.Red;
                else c = x >= 0 ? LauncherColors.Red : LauncherColors.White;
                Put(cx + x, cy + y, c);
            }
    }

    /// <summary>Pixel width of a string: glyph widths times the scale plus <paramref name="spacing"/> after each character.</summary>
    public static int MeasureText(IFontSource font, string text, int scale = 1, int spacing = 0)
    {
        int w = 0;
        foreach (var rune in text.EnumerateRunes())
            w += (font.TryGetGlyph(rune.Value, out var g) ? g.Width : 8) * scale + spacing;
        return w;
    }

    /// <summary>Draws text with its top-left corner at (x, y); returns the x after the last character.</summary>
    public int DrawText(IFontSource font, string text, int x, int y, uint color, int scale = 1, int spacing = 0, int alpha = 255)
    {
        foreach (var rune in text.EnumerateRunes())
        {
            if (!font.TryGetGlyph(rune.Value, out var g)) { x += 8 * scale + spacing; continue; }
            int bytesPerRow = (g.Width + 7) / 8;
            var rows = g.Rows.Span;
            for (int row = 0; row < Glyph.Height; row++)
                for (int col = 0; col < g.Width; col++)
                    if ((rows[row * bytesPerRow + col / 8] & (0x80 >> (col & 7))) != 0)
                        FillRect(x + col * scale, y + row * scale, scale, scale, color, alpha);
            x += g.Width * scale + spacing;
        }
        return x;
    }

    /// <summary>Text with a hard offset shadow (the design's "3px 3px 0" title shadow).</summary>
    public void DrawTextShadow(IFontSource font, string text, int x, int y, uint color, uint shadow, int offset, int scale = 1, int spacing = 0)
    {
        DrawText(font, text, x + offset, y + offset, shadow, scale, spacing);
        DrawText(font, text, x, y, color, scale, spacing);
    }

    /// <summary>Darkens one row in three, like the design's scan lines (12 % black).</summary>
    public void Scanlines()
    {
        for (int y = 0; y < Height; y++)
        {
            if ((Height - 1 - y) % 3 != 0) continue;
            int o = y * Width;
            for (int x = 0; x < Width; x++) Pixels[o + x] = Mix(Pixels[o + x], 0xFF000000, 31);
        }
    }

    /// <summary>The picture as RGBA bytes in MonoGame's colour order (R, G, B, A in memory), ready for Texture2D.SetData.</summary>
    public void ToRgba(uint[] destination)
    {
        for (int i = 0; i < Pixels.Length; i++)
        {
            uint p = Pixels[i];
            destination[i] = (p & 0xFF00FF00u) | ((p & 0xFF) << 16) | ((p >> 16) & 0xFF);
        }
    }
}
