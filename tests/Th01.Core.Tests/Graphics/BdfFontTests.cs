using Th01.Core.Graphics;

namespace Th01.Core.Tests.Graphics;

public class BdfFontTests
{
    [Fact]
    public void Spleen_HasAsciiGlyphs_With16RowCells()
    {
        var font = BdfFont.LoadSpleen8x16();
        for (int c = 0x20; c <= 0x7E; c++)
        {
            Assert.True(font.TryGetGlyph(c, out var g), $"missing U+{c:X4}");
            Assert.Equal(8, g.Width);
            Assert.Equal(16, g.Rows.Length);
        }
        Assert.False(font.TryGetGlyph(0x6771, out _)); // no Japanese
    }

    [Fact]
    public void Spleen_DrawsVisibleInk_AndSpaceIsBlank()
    {
        var font = BdfFont.LoadSpleen8x16();
        var fb = new Framebuffer();
        TextRenderer.DrawString(fb, font, "A", 0, 0, 1);
        int ink = 0;
        for (int y = 0; y < 16; y++) for (int x = 0; x < 8; x++) if (fb.GetPixel(x, y) == 1) ink++;
        Assert.InRange(ink, 10, 80);

        font.TryGetGlyph(' ', out var sp);
        Assert.All(sp.Rows.ToArray(), b => Assert.Equal(0, b));
    }

    [Fact]
    public void Parser_PlacesGlyphUsingBoundingBox()
    {
        const string bdf = "FONTBOUNDINGBOX 8 16 0 -4\nSTARTCHAR dot\nENCODING 46\nBBX 2 2 1 -4\nBITMAP\nC0\nC0\nENDCHAR\n";
        var font = BdfFont.Parse(new StringReader(bdf));
        font.TryGetGlyph('.', out var g);
        var rows = g.Rows.ToArray();
        // Box bottom is at descent -4 => rows 14 and 15 of the cell; x offset 1 => bits 0x60.
        Assert.Equal(0x60, rows[14]);
        Assert.Equal(0x60, rows[15]);
        Assert.Equal(0, rows[13]);
    }
}
