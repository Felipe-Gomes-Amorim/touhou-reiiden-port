using Th01.Core.Graphics;

namespace Th01.Core.Tests.Graphics;

public class TextRendererTests
{
    sealed class TestFont : IFontSource
    {
        public bool TryGetGlyph(int cp, out Glyph g)
        {
            if (cp == 'A') { var rows = new byte[16]; rows[0] = 0x80; rows[15] = 0x01; g = new Glyph(8, rows); return true; }
            if (cp == 0x6771) { var rows = new byte[32]; rows[0] = 0x80; rows[31] = 0x01; g = new Glyph(16, rows); return true; }
            g = default;
            return false;
        }
    }

    [Fact]
    public void DrawsHalfAndFullWidthGlyphsAndAdvances()
    {
        var fb = new Framebuffer();
        int end = TextRenderer.DrawString(fb, new TestFont(), "A東?A", 10, 20, 7, background: 1);
        Assert.Equal(10 + 8 + 16 + 8 + 8, end);
        Assert.Equal(7, fb.GetPixel(10, 20));
        Assert.Equal(7, fb.GetPixel(17, 35));
        Assert.Equal(1, fb.GetPixel(11, 20));          // background colour
        Assert.Equal(7, fb.GetPixel(18, 20));          // wide glyph top-left
        Assert.Equal(7, fb.GetPixel(18 + 15, 35));     // wide glyph bottom-right
        Assert.Equal(0, fb.GetPixel(34, 20));          // missing glyph draws nothing
        Assert.Equal(7, fb.GetPixel(42, 20));
        Assert.Equal(40, TextRenderer.Measure(new TestFont(), "A東?A"));
    }
}
