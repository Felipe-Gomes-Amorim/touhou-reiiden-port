using Th01.Assets.Text;

namespace Th01.Assets.Tests;

public class TextCodecTests
{
    [Fact]
    public void DecodesAndEncodesMixedText()
    {
        byte[] sjis = [0x82, 0xA0, (byte)'A', 0x83, 0x41, 0xB1]; // あ A ア half-width ｱ
        string s = TextCodec.Decode(sjis);
        Assert.Equal("あAアｱ", s);
        Assert.Equal(sjis, TextCodec.Encode(s));
    }

    [Fact]
    public void InvalidBytesThrow()
    {
        Assert.Throws<InvalidDataException>(() => TextCodec.Decode([0x82]));
        Assert.False(TextCodec.TryDecode([0x81, 0x20], out _));
        Assert.Throws<InvalidDataException>(() => TextCodec.Encode("😀"));
    }

    [Theory]
    [InlineData(0x8140, 1, 1)]    // ideographic space
    [InlineData(0x889F, 16, 1)]   // first level 1 kanji
    [InlineData(0x82A0, 4, 2)]    // hiragana あ is 4-02
    [InlineData(0x8341, 5, 2)]    // katakana ア is 5-02
    [InlineData(0x9FFC, 62, 94)]  // end of level 1 row 62
    [InlineData(0xE040, 63, 1)]
    public void JisRowColumnRoundTrip(int sjis, int row, int col)
    {
        Assert.Equal((row, col), TextCodec.ToJis((ushort)sjis));
        Assert.Equal((ushort)sjis, TextCodec.FromJis(row, col));
    }

    [Fact]
    public void EveryJisCellRoundTripsThroughShiftJis()
    {
        for (int row = 1; row <= 94; row++)
            for (int col = 1; col <= 94; col++)
                Assert.Equal((row, col), TextCodec.ToJis(TextCodec.FromJis(row, col)));
    }

    [Fact]
    public void UnicodeToJis()
    {
        Assert.True(TextCodec.TryUnicodeToJis("あ", out int r, out int c));
        Assert.Equal((4, 2), (r, c));
        Assert.True(TextCodec.TryUnicodeToJis("亜", out r, out c));
        Assert.Equal((16, 1), (r, c));
        Assert.False(TextCodec.TryUnicodeToJis("A", out _, out _)); // single byte
        Assert.False(TextCodec.TryUnicodeToJis("😀", out _, out _));
        Assert.Equal("あ", TextCodec.ToUnicode(0x82A0));
        Assert.Null(TextCodec.ToUnicode(0x8100));
    }

    [Fact]
    public void RankingNameCells()
    {
        ushort[] cells = [0x82A0, 0x8341, 0x2020, 0x4142, 0x8140, 0x8140, 0x2020, 0x2020]; // あ ア "  " "AB" space space
        Assert.Equal("あア  AB　　    ", TextCodec.DecodeCells(cells));
        ushort[] back = TextCodec.EncodeCells("あア", 8);
        Assert.Equal(0x82A0, back[0]);
        Assert.Equal(0x8341, back[1]);
        Assert.Equal(0x2020, back[7]);
        Assert.Throws<ArgumentException>(() => TextCodec.EncodeCells("あいうえお", 2));
    }

    [Fact]
    public void CellsSplitsByCharacter()
    {
        Assert.Equal(new ushort[] { 0x82A0, 0x41, 0x8341 }, TextCodec.Cells([0x82, 0xA0, 0x41, 0x83, 0x41]));
    }
}
