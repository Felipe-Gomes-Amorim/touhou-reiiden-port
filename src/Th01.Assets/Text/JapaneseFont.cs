using Th01.Core.Graphics;

namespace Th01.Assets.Text;

/// <summary>
/// The platform's text font when the user has no PC-98 font ROM: Shinonome 16 (kana, kanji, symbols, 16 px cells)
/// with the 8 px Spleen font for ASCII, because a 16 px cell font would draw ASCII too wide. Both are embedded in
/// Th01.Core. Glyphs are looked up by Unicode code point; <see cref="TryGetSjisGlyph"/> takes a
/// Shift-JIS code (a ranking-name cell).
/// </summary>
public sealed class JapaneseFont : IFontSource
{
    private readonly IFontSource _wide;
    private readonly IFontSource _narrow;

    public JapaneseFont(IFontSource wide, IFontSource narrow)
    {
        ArgumentNullException.ThrowIfNull(wide);
        ArgumentNullException.ThrowIfNull(narrow);
        _wide = wide;
        _narrow = narrow;
    }

    public static JapaneseFont CreateDefault() => new(BdfFont.LoadShinonome16(), BdfFont.LoadSpleen8x16());

    public bool TryGetGlyph(int codePoint, out Glyph glyph)
    {
        if (codePoint < 0x80 && _narrow.TryGetGlyph(codePoint, out glyph)) return true;
        if (_wide.TryGetGlyph(codePoint, out glyph) || _narrow.TryGetGlyph(codePoint, out glyph)) return true;
        // Code page 932 and JIS X 0208 disagree on a few symbols; Shinonome follows JIS.
        return JisAlias.TryGetValue(codePoint, out int alias) && _wide.TryGetGlyph(alias, out glyph);
    }

    /// <summary>CP932 code point to the JIS X 0208 one used by Shinonome (hyphen-minus, wave dash, double vertical line, cent, pound, not, em dash).</summary>
    private static readonly Dictionary<int, int> JisAlias = new()
    {
        [0xFF0D] = 0x2212, [0xFF5E] = 0x301C, [0x2225] = 0x2016, [0xFFE0] = 0x00A2, [0xFFE1] = 0x00A3, [0xFFE2] = 0x00AC, [0x2015] = 0x2014,
    };

    /// <summary>
    /// Glyph of a Shift-JIS double-byte code (kana, kanji, full-width letters), e.g. a ranking-name cell.
    /// Named differently from <see cref="TryGetGlyph(int, out Glyph)"/> because a <c>char</c> would otherwise bind to a <c>ushort</c> parameter.
    /// </summary>
    public bool TryGetSjisGlyph(ushort sjis, out Glyph glyph)
    {
        glyph = default;
        string? s = TextCodec.ToUnicode(sjis);
        return s is { Length: > 0 } && TryGetGlyph(char.ConvertToUtf32(s, 0), out glyph);
    }
}
