namespace Th01.Core.Graphics;

// Software equivalents of the PC-98 hardware operations the game relies on (docs/RECON.md section 2).
// Everything here is additive: the original Framebuffer members are unchanged.
public sealed partial class Framebuffer
{
    int _scrollY;

    /// <summary>
    /// Display start line (the GDC "scroll" command): display row d shows VRAM row (d + ScrollY) mod 400.
    /// Used for screen shakes. Only affects <see cref="ToRgba"/>, never the stored pixels.
    /// </summary>
    // ref: th01/hardware/scrollup.cpp
    public int ScrollY
    {
        get => _scrollY;
        set => _scrollY = ((value % Height) + Height) % Height;
    }

    public byte GetPixel(int x, int y) =>
        (uint)x < Width && (uint)y < Height ? _pixels[y * Width + x] : (byte)0;

    public void Clear(byte color = 0) => Array.Fill(_pixels, (byte)(color & 0xF));

    /// <summary>Copies pixels and palette from another framebuffer (not the scroll offset).</summary>
    public void CopyFrom(Framebuffer src)
    {
        Array.Copy(src._pixels, _pixels, _pixels.Length);
        Array.Copy(src._palette, _palette, _palette.Length);
    }

    /// <summary>Copies the pixels only, keeping this page's palette (the palette is hardware state, not part of VRAM).</summary>
    public void CopyPixelsFrom(Framebuffer src) => Array.Copy(src._pixels, _pixels, _pixels.Length);

    /// <summary>Reads a palette entry back as 4-bit components.</summary>
    public void GetPalette4(int index, out int r4, out int g4, out int b4)
    {
        uint c = _palette[index];
        r4 = (int)((c >> 16) & 0xFF) / 17;
        g4 = (int)((c >> 8) & 0xFF) / 17;
        b4 = (int)(c & 0xFF) / 17;
    }

    /// <summary>Exact rectangle copy from another page (clipped to both).</summary>
    public void CopyRect(Framebuffer src, int x, int y, int w, int h)
    {
        if (!Clip(ref x, ref y, ref w, ref h)) return;
        for (int yy = y; yy < y + h; yy++)
            Array.Copy(src._pixels, yy * Width + x, _pixels, yy * Width + x, w);
    }

    /// <summary>
    /// EGC-style restore ("copy page 1 to page 0"): the original works on 16-dot words, so the left edge is
    /// rounded down to a multiple of 16 and the right edge extends to the end of the last word touched.
    /// </summary>
    // ref: th01/hardware/egcrect.cpp (egc_copy_rect_1_to_0_16)
    public void CopyRectEgc16(Framebuffer src, int x, int y, int w, int h)
    {
        int right = x + w;
        int left = x & ~15;
        right = left + (((right - left) + 15) & ~15);
        CopyRect(src, left, y, right - left, h);
    }

    /// <summary>
    /// Draws a w*h block of palette indices. If <paramref name="alpha"/> is non-empty, pixels whose alpha byte is 0 are skipped.
    /// Takes raw arrays because Th01.Core cannot reference Th01.Assets.
    /// </summary>
    public void Blit(ReadOnlySpan<byte> pixels, int w, int h, int x, int y, ReadOnlySpan<byte> alpha = default)
    {
        if (pixels.Length < w * h) throw new ArgumentException("pixels shorter than w*h", nameof(pixels));
        if (!alpha.IsEmpty && alpha.Length < w * h) throw new ArgumentException("alpha shorter than w*h", nameof(alpha));
        for (int sy = 0; sy < h; sy++)
        {
            int dy = y + sy;
            if ((uint)dy >= Height) continue;
            for (int sx = 0; sx < w; sx++)
            {
                int dx = x + sx;
                if ((uint)dx >= Width) continue;
                int si = sy * w + sx;
                if (!alpha.IsEmpty && alpha[si] == 0) continue;
                _pixels[dy * Width + dx] = (byte)(pixels[si] & 0xF);
            }
        }
    }

    /// <summary>Blit where one palette index is transparent (the game's sprites use #15).</summary>
    public void BlitColorKey(ReadOnlySpan<byte> pixels, int w, int h, int x, int y, byte transparent = 15)
    {
        if (pixels.Length < w * h) throw new ArgumentException("pixels shorter than w*h", nameof(pixels));
        for (int sy = 0; sy < h; sy++)
        {
            int dy = y + sy;
            if ((uint)dy >= Height) continue;
            for (int sx = 0; sx < w; sx++)
            {
                int dx = x + sx;
                byte c = (byte)(pixels[sy * w + sx] & 0xF);
                if ((uint)dx >= Width || c == transparent) continue;
                _pixels[dy * Width + dx] = c;
            }
        }
    }

    /// <summary>
    /// GRCG read-modify-write fill: only the bit planes set in <paramref name="planeMask"/> (bit0=B, bit1=R, bit2=G, bit3=E)
    /// take the corresponding bits of <paramref name="color"/>; the other planes keep their old value.
    /// </summary>
    // ref: th01/hardware/grcg.hpp
    public void FillRectPlanes(int x, int y, int w, int h, byte color, byte planeMask)
    {
        if (!Clip(ref x, ref y, ref w, ref h)) return;
        int m = planeMask & 0xF;
        int c = color & m;
        for (int yy = y; yy < y + h; yy++)
            for (int xx = x; xx < x + w; xx++)
            {
                int i = yy * Width + xx;
                _pixels[i] = (byte)((_pixels[i] & ~m) | c);
            }
    }

    /// <summary>
    /// GRCG tile fill: the tile register is one pattern per plane repeated every 8 dots horizontally and identical on
    /// every row, so the result is a colour per (x mod 8). <paramref name="row8"/> must have 8 entries.
    /// </summary>
    public void TileRect(int x, int y, int w, int h, ReadOnlySpan<byte> row8)
    {
        if (row8.Length != 8) throw new ArgumentException("tile row must have 8 colours", nameof(row8));
        if (!Clip(ref x, ref y, ref w, ref h)) return;
        for (int yy = y; yy < y + h; yy++)
            for (int xx = x; xx < x + w; xx++)
                _pixels[yy * Width + xx] = (byte)(row8[xx & 7] & 0xF);
    }

    /// <summary>Inverts all four planes of a region (XOR 15).</summary>
    // ref: th01/hardware/grpinv32.cpp
    public void InvertRect(int x, int y, int w, int h)
    {
        if (!Clip(ref x, ref y, ref w, ref h)) return;
        for (int yy = y; yy < y + h; yy++)
            for (int xx = x; xx < x + w; xx++)
                _pixels[yy * Width + xx] ^= 0xF;
    }

    static bool Clip(ref int x, ref int y, ref int w, ref int h)
    {
        int x2 = Math.Min(Width, x + w), y2 = Math.Min(Height, y + h);
        x = Math.Max(0, x);
        y = Math.Max(0, y);
        w = x2 - x;
        h = y2 - y;
        return w > 0 && h > 0;
    }
}
