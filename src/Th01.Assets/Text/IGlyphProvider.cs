namespace Th01.Assets.Text;

/// <summary>
/// Source of monochrome glyph bitmaps addressed by JIS X 0208 position. Bitmaps have one byte per pixel
/// (0 = background, 1 = ink), row-major, w * h bytes. Th01.Core does not reference this interface; the
/// platform layer bridges it to the renderer.
/// </summary>
public interface IGlyphProvider
{
    /// <summary>Full-width glyph (normally 16x16) for a JIS X 0208 row/column (1..94). False when this provider does not have it.</summary>
    bool TryGet(int jisRow, int jisCol, out byte[] bitmap, out int w, out int h);

    /// <summary>Single-byte (ANK) glyph (normally 8x16) for a character code 0..255. False when not available.</summary>
    bool TryGetAnk(int code, out byte[] bitmap, out int w, out int h);
}

/// <summary>Tries several providers in order and returns the first glyph found (the font chain of the platform).</summary>
public sealed class GlyphChain : IGlyphProvider
{
    private readonly IGlyphProvider[] _providers;

    public GlyphChain(params IGlyphProvider[] providers)
    {
        ArgumentNullException.ThrowIfNull(providers);
        _providers = providers;
    }

    public bool TryGet(int jisRow, int jisCol, out byte[] bitmap, out int w, out int h)
    {
        foreach (var p in _providers)
            if (p.TryGet(jisRow, jisCol, out bitmap, out w, out h)) return true;
        bitmap = []; w = h = 0;
        return false;
    }

    public bool TryGetAnk(int code, out byte[] bitmap, out int w, out int h)
    {
        foreach (var p in _providers)
            if (p.TryGetAnk(code, out bitmap, out w, out h)) return true;
        bitmap = []; w = h = 0;
        return false;
    }
}
