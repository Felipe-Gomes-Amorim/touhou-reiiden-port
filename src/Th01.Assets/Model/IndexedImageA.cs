namespace Th01.Assets.Model;

/// <summary>An <see cref="IndexedImage"/> with a per-pixel alpha mask (0 = transparent, 1 = opaque).</summary>
public sealed class IndexedImageA : IndexedImage
{
    public byte[] Alpha { get; }

    public IndexedImageA(int width, int height, byte[] pixels, byte[] alpha, Palette4? palette = null)
        : base(width, height, pixels, palette)
    {
        ArgumentNullException.ThrowIfNull(alpha);
        if (alpha.Length != pixels.Length)
            throw new ArgumentException("Alpha must have one byte per pixel.", nameof(alpha));
        Alpha = alpha;
    }
}
