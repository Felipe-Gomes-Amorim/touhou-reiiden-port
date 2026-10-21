namespace Th01.Assets.Model;

/// <summary>16-entry palette with 4 bits per channel (PC-98 analog palette), values 0..15.</summary>
public sealed class Palette4
{
    public const int Count = 16;
    public const int MaxLevel = 15;

    private readonly byte[] _rgb = new byte[Count * 3];

    public Palette4() { }

    /// <param name="rgb">48 bytes: R,G,B for each of the 16 entries, each 0..15.</param>
    public Palette4(ReadOnlySpan<byte> rgb)
    {
        if (rgb.Length != Count * 3)
            throw new ArgumentException($"Palette4 needs {Count * 3} bytes, got {rgb.Length}.", nameof(rgb));
        for (int i = 0; i < rgb.Length; i++)
            if (rgb[i] > MaxLevel) throw new ArgumentOutOfRangeException(nameof(rgb), $"Channel value {rgb[i]} at {i} is above 15.");
        rgb.CopyTo(_rgb);
    }

    public (byte R, byte G, byte B) this[int index]
    {
        get
        {
            if ((uint)index >= Count) throw new ArgumentOutOfRangeException(nameof(index));
            return (_rgb[index * 3], _rgb[index * 3 + 1], _rgb[index * 3 + 2]);
        }
    }

    public void Set(int index, byte r, byte g, byte b)
    {
        if ((uint)index >= Count) throw new ArgumentOutOfRangeException(nameof(index));
        if (r > MaxLevel || g > MaxLevel || b > MaxLevel) throw new ArgumentOutOfRangeException(nameof(r), "Channels are 0..15.");
        _rgb[index * 3] = r; _rgb[index * 3 + 1] = g; _rgb[index * 3 + 2] = b;
    }

    /// <summary>Expands an entry to 8 bits per channel (x17, so 15 maps to 255).</summary>
    public (byte R, byte G, byte B) ToRgb888(int index)
    {
        var (r, g, b) = this[index];
        return ((byte)(r * 17), (byte)(g * 17), (byte)(b * 17));
    }
}
