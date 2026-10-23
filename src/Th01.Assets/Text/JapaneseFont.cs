using Th01.Core.Graphics;

namespace Th01.Assets.Text;

/// <summary>
/// Glyph source for Japanese text. Wraps a BDF font indexed by Unicode (16-row cell, 8 or 16 wide; any free
/// font such as Shinonome 16 or GNU Unifont in BDF form works) and looks glyphs up by Shift-JIS code.
/// The port ships no game font: the PC-98 ROM font is not in the game files. Falls back to
/// <paramref name="fallback"/> (e.g. the ASCII font) for code points the main font lacks.
/// </summary>
public sealed class JapaneseFont : IFontSource
{
    private readonly IFontSource _main;
    private readonly IFontSource? _fallback;

    public JapaneseFont(IFontSource main, IFontSource? fallback = null)
    {
        ArgumentNullException.ThrowIfNull(main);
        _main = main;
        _fallback = fallback;
    }

    public static JapaneseFont Load(Stream bdf, IFontSource? fallback = null)
    {
        ArgumentNullException.ThrowIfNull(bdf);
        using var reader = new StreamReader(bdf);
        return new JapaneseFont(BdfFont.Parse(reader), fallback);
    }

    public bool TryGetGlyph(int codePoint, out Glyph glyph) =>
        _main.TryGetGlyph(codePoint, out glyph) || (_fallback?.TryGetGlyph(codePoint, out glyph) ?? false);

    /// <summary>Glyph for a 16-bit Shift-JIS cell (see <see cref="ShiftJis.DecodeCell"/>).</summary>
    public bool TryGetGlyph(ushort sjisCell, out Glyph glyph)
    {
        glyph = default;
        return ShiftJis.CodePointOf(sjisCell) is { } cp && TryGetGlyph(cp, out glyph);
    }
}
