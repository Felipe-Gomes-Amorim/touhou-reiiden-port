using Th01.Assets.Model;

namespace Th01.Assets.Formats;

/// <summary>
/// Helpers for the PC-98 planar layout: four bit planes (B, R, G, E = bits 0..3 of the colour
/// index), 8 pixels per byte, most significant bit leftmost.
/// </summary>
public static class Planar
{
    /// <summary>Combines four planes of widthBytes x height bytes into one index per pixel.</summary>
    public static byte[] ToIndices(ReadOnlySpan<byte> b, ReadOnlySpan<byte> r, ReadOnlySpan<byte> g, ReadOnlySpan<byte> e,
        int widthBytes, int height)
    {
        int size = widthBytes * height;
        if (b.Length < size || r.Length < size || g.Length < size || e.Length < size)
            throw new ArgumentException("Plane data is shorter than width x height.");
        byte[] px = new byte[size * 8];
        for (int i = 0; i < size; i++)
        {
            for (int bit = 0; bit < 8; bit++)
            {
                int m = 0x80 >> bit;
                px[i * 8 + bit] = (byte)(((b[i] & m) != 0 ? 1 : 0) | ((r[i] & m) != 0 ? 2 : 0)
                                       | ((g[i] & m) != 0 ? 4 : 0) | ((e[i] & m) != 0 ? 8 : 0));
            }
        }
        return px;
    }

    /// <summary>Expands one plane into 0/1 per pixel (optionally inverted).</summary>
    public static byte[] ToBits(ReadOnlySpan<byte> plane, int widthBytes, int height, bool invert = false)
    {
        int size = widthBytes * height;
        if (plane.Length < size) throw new ArgumentException("Plane data is shorter than width x height.");
        byte[] px = new byte[size * 8];
        for (int i = 0; i < size; i++)
            for (int bit = 0; bit < 8; bit++)
            {
                bool set = (plane[i] & (0x80 >> bit)) != 0;
                px[i * 8 + bit] = (byte)(set != invert ? 1 : 0);
            }
        return px;
    }

    /// <summary>Reads a 48-byte header palette (R,G,B per entry). Channels are masked to 4 bits.</summary>
    internal static Palette4 ReadPalette(ReadOnlySpan<byte> raw48)
    {
        byte[] v = new byte[48];
        for (int i = 0; i < 48; i++) v[i] = (byte)(raw48[i] & 0x0F);
        return new Palette4(v);
    }
}
