namespace Th01.Core.Graphics;

/// <summary>The PC-98 text layer's eight digital colours.</summary>
public enum TextColor : byte { Black, Blue, Red, Magenta, Green, Cyan, Yellow, White }

public sealed partial class Framebuffer
{
    // The PC-98 has a text layer drawn over the graphics. A cell is either transparent (the graphics show through) or a
    // solid block of one of 8 fixed colours that hides the graphics under it. The game uses it for full-screen banners
    // (the scene title, "STAGE nn"). It does not scroll with the graphics. 0 = transparent, otherwise TextColor + 1.
    static readonly uint[] TextPalette =
    [
        0xFF000000, 0xFF0000FF, 0xFFFF0000, 0xFFFF00FF, 0xFF00FF00, 0xFF00FFFF, 0xFFFFFF00, 0xFFFFFFFF,
    ];

    byte[]? _text;

    /// <summary>True while at least one pixel of the text layer is a solid block.</summary>
    public bool HasTextLayer { get; private set; }

    /// <summary>Makes the whole text layer transparent again.</summary>
    public void ClearTextLayer()
    {
        if (!HasTextLayer) return;
        Array.Clear(_text!);
        HasTextLayer = false;
    }

    /// <summary>Covers a rectangle with a solid block of <paramref name="color"/> (clipped to the screen).</summary>
    public void TextFill(int x, int y, int w, int h, TextColor color) => TextSet(x, y, w, h, (byte)(color + 1));

    /// <summary>Makes a rectangle transparent again, so the graphics show through (clipped to the screen).</summary>
    public void TextClear(int x, int y, int w, int h)
    {
        if (!HasTextLayer) return;
        TextSet(x, y, w, h, 0);
    }

    /// <summary>The colour of the solid text block over a pixel, or null when the graphics show through.</summary>
    public TextColor? TextAt(int x, int y)
    {
        if (_text == null || (uint)x >= Width || (uint)y >= Height) return null;
        byte v = _text[y * Width + x];
        return v == 0 ? null : (TextColor)(v - 1);
    }

    void TextSet(int x, int y, int w, int h, byte value)
    {
        _text ??= new byte[Width * Height];
        for (int yy = Math.Max(0, y); yy < Math.Min(Height, y + h); yy++)
            for (int xx = Math.Max(0, x); xx < Math.Min(Width, x + w); xx++)
                _text[yy * Width + xx] = value;
        if (value != 0) HasTextLayer = true;
    }

    /// <summary>Paints the text layer over already converted pixels (called by <see cref="ToRgba"/>).</summary>
    void ApplyTextLayer(Span<uint> dest)
    {
        if (!HasTextLayer || _text == null) return;
        for (int i = 0; i < _text.Length; i++)
            if (_text[i] != 0) dest[i] = TextPalette[_text[i] - 1];
    }
}
