namespace Th01.Assets.Model;

/// <summary>Width x Height pixels, one palette index (0..15) per byte, row-major. Palette may be null.</summary>
public class IndexedImage
{
    public int Width { get; }
    public int Height { get; }
    public byte[] Pixels { get; }
    public Palette4? Palette { get; }

    public IndexedImage(int width, int height, byte[] pixels, Palette4? palette = null)
    {
        if (width <= 0) throw new ArgumentOutOfRangeException(nameof(width));
        if (height <= 0) throw new ArgumentOutOfRangeException(nameof(height));
        ArgumentNullException.ThrowIfNull(pixels);
        if (pixels.Length != width * height)
            throw new ArgumentException($"Expected {width * height} pixels, got {pixels.Length}.", nameof(pixels));
        Width = width; Height = height; Pixels = pixels; Palette = palette;
    }
}
