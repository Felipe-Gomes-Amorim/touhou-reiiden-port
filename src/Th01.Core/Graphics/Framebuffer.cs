namespace Th01.Core.Graphics;

/// <summary>
/// 640x400, 16 colours, one palette index per pixel (byte per pixel for simplicity).
/// Stand-in for the PC-98 planar VRAM; planar semantics (GRCG/EGC) come later.
/// </summary>
public sealed partial class Framebuffer
{
    public const int Width = 640;
    public const int Height = 400;
    public const int ColorCount = 16;

    readonly byte[] _pixels = new byte[Width * Height];
    // 0xAARRGGBB
    readonly uint[] _palette = new uint[ColorCount];

    public ReadOnlySpan<byte> Pixels => _pixels;
    public ReadOnlySpan<uint> Palette => _palette;

    public void SetPixel(int x, int y, byte color)
    {
        if ((uint)x < Width && (uint)y < Height) _pixels[y * Width + x] = (byte)(color & 0xF);
    }

    public void FillRect(int x, int y, int w, int h, byte color)
    {
        for (int yy = Math.Max(0, y); yy < Math.Min(Height, y + h); yy++)
            for (int xx = Math.Max(0, x); xx < Math.Min(Width, x + w); xx++)
                _pixels[yy * Width + xx] = (byte)(color & 0xF);
    }

    /// <summary>Sets a palette entry from PC-98-style 4-bit components (0..15 each).</summary>
    public void SetPalette4(int index, int r4, int g4, int b4)
    {
        _palette[index] = 0xFF000000u | (uint)((r4 * 17) << 16) | (uint)((g4 * 17) << 8) | (uint)(b4 * 17);
    }

    /// <summary>
    /// Converts to 32-bit pixels into <paramref name="dest"/> (length Width*Height), honouring <see cref="ScrollY"/>.
    /// </summary>
    public void ToRgba(Span<uint> dest)
    {
        int top = ScrollY;
        for (int y = 0; y < Height; y++)
        {
            int src = ((y + top) % Height) * Width;
            int dst = y * Width;
            for (int x = 0; x < Width; x++) dest[dst + x] = _palette[_pixels[src + x]];
        }
    }
}
