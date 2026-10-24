using Th01.Assets.Text;

namespace Th01.Assets.Tests;

/// <summary>Synthetic ROM images with an invented layout: the real ROM layout is NOT VERIFIED.</summary>
public class GlyphTests
{
    // layout used only by the tests: ANK at 0x10 (256 * 16 bytes), kanji at 0x2000, rows of 94 * 32 bytes
    private static readonly FontRomLayout Test = new(0x2000, 94 * 32, 32, false, 0x10);
    private static readonly FontRomLayout TestSplit = new(0x2000, 94 * 32, 32, true, -1);
    private const int CodeA = 0x41;

    private static byte[] Rom()
    {
        var rom = new byte[0x2000 + 94 * 94 * 32];
        rom[0x10 + CodeA * 16 + 3] = 0b1000_0001;                        // ANK A, row 3: leftmost and rightmost pixel
        int g = 0x2000 + (4 - 1) * 94 * 32 + (2 - 1) * 32;               // JIS 4-02
        rom[g + 5 * 2] = 0b1010_0000; rom[g + 5 * 2 + 1] = 0b0000_0001;  // row 5: x = 0, 2 and 15
        return rom;
    }

    [Fact]
    public void ReadsKanjiBits()
    {
        var f = new PcFontRom(Rom(), Test);
        Assert.True(f.TryGet(4, 2, out var bmp, out int w, out int h));
        Assert.Equal((16, 16, 256), (w, h, bmp.Length));
        Assert.Equal(1, bmp[5 * 16 + 0]);
        Assert.Equal(0, bmp[5 * 16 + 1]);
        Assert.Equal(1, bmp[5 * 16 + 2]);
        Assert.Equal(1, bmp[5 * 16 + 15]);
        Assert.Equal(3, bmp.Count(b => b == 1));
    }

    [Fact]
    public void SplitHalvesLayoutIsDifferent()
    {
        var rom = new byte[0x2000 + 94 * 94 * 32];
        int g = 0x2000;
        rom[g + 2] = 0x80;        // left half, row 2
        rom[g + 16 + 2] = 0x01;   // right half, row 2
        var f = new PcFontRom(rom, TestSplit);
        Assert.True(f.TryGet(1, 1, out var bmp, out _, out _));
        Assert.Equal(1, bmp[2 * 16]);
        Assert.Equal(1, bmp[2 * 16 + 15]);
    }

    [Fact]
    public void ReadsAnkAndRejectsOutOfRange()
    {
        var f = new PcFontRom(Rom(), Test);
        Assert.True(f.TryGetAnk(CodeA, out var bmp, out int w, out int h));
        Assert.Equal((8, 16), (w, h));
        Assert.Equal(1, bmp[3 * 8]);
        Assert.Equal(1, bmp[3 * 8 + 7]);
        Assert.False(f.TryGet(0, 1, out _, out _, out _));
        Assert.False(f.TryGet(1, 95, out _, out _, out _));
        Assert.False(f.TryGetAnk(256, out _, out _, out _));
        Assert.False(new PcFontRom(Rom(), TestSplit).TryGetAnk(CodeA, out _, out _, out _)); // no ANK table
        Assert.False(new PcFontRom(new byte[100], Test).TryGet(1, 1, out _, out _, out _));  // ROM too short
    }

    [Fact]
    public void InvalidLayoutIsRejected()
    {
        Assert.Throws<ArgumentException>(() => new PcFontRom([], new FontRomLayout(-1, 10, 32, false, 0)));
        Assert.Throws<ArgumentException>(() => new PcFontRom([], new FontRomLayout(0, 0, 32, false, 0)));
    }

    [Fact]
    public void ChainUsesTheFirstProviderThatHasTheGlyph()
    {
        var a = new PcFontRom(new byte[100], Test); // too short: has nothing
        var b = new PcFontRom(Rom(), Test);
        var chain = new GlyphChain(a, b);
        Assert.True(chain.TryGet(4, 2, out _, out _, out _));
        Assert.True(chain.TryGetAnk(CodeA, out _, out _, out _));
        Assert.False(new GlyphChain(a).TryGet(4, 2, out var bmp, out int w, out _));
        Assert.Empty(bmp);
        Assert.Equal(0, w);
    }
}
