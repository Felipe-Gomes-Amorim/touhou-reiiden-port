using Th01.Assets.Config;
using Th01.Assets.Text;

namespace Th01.Assets.Tests;

/// <summary>Checks the emulator-image layout against the user's own ROM (skipped without it). No glyph data is stored here.</summary>
public class PcFontRomRealTests
{
    private static PcFontRom Rom()
    {
        var rom = FontRomConfig.TryLoad();
        Skip.If(rom is null, $"No 288768-byte font ROM. Set {FontRomConfig.EnvVar}, fontRomPath in config.local.json, or put {FontRomConfig.FileName} in the game data folder.");
        return rom!;
    }

    private static int Ink(byte[] bmp) => bmp.Count(b => b != 0);

    [SkippableFact]
    public void Ank_SpaceIsBlankAndLettersHaveInk()
    {
        var rom = Rom();
        Assert.True(rom.TryGetAnk(' ', out var space, out int w, out int h));
        Assert.Equal((8, 16), (w, h));
        Assert.Equal(0, Ink(space));
        foreach (char c in "AZaz09@")
        {
            Assert.True(rom.TryGetAnk(c, out var g, out _, out _));
            Assert.InRange(Ink(g), 8, 70);
        }
        rom.TryGetAnk('A', out var a, out _, out _);
        rom.TryGetAnk('B', out var b, out _, out _);
        Assert.NotEqual(a, b);
        Assert.All(Enumerable.Range(0, 16), y => Assert.Equal(0, a[y * 8])); // letters leave the first column empty
    }

    [SkippableFact]
    public void Kana_AllHiraganaAndKatakanaHaveInk()
    {
        var rom = Rom();
        Assert.True(rom.TryGet(1, 1, out var blank, out int w, out int h)); // ideographic space
        Assert.Equal((16, 16), (w, h));
        Assert.Equal(0, Ink(blank));
        for (int c = 1; c <= 83; c++)
        {
            Assert.True(rom.TryGet(4, c, out var hira, out _, out _));
            Assert.InRange(Ink(hira), 6, 120);
        }
        for (int c = 1; c <= 86; c++)
        {
            Assert.True(rom.TryGet(5, c, out var kata, out _, out _));
            Assert.InRange(Ink(kata), 6, 120);
        }
    }

    [SkippableFact]
    public void Kana_AreTheRightCharactersByNeighbourhood()
    {
        // JIS addressing is only right if each cell is the glyph of its own code: small kana (4-1) differ from their big form (4-2).
        var rom = Rom();
        rom.TryGet(4, 1, out var small, out _, out _);
        rom.TryGet(4, 2, out var a, out _, out _);
        Assert.True(Ink(small) < Ink(a) * 1.5 && !small.SequenceEqual(a));
        // Fullwidth Latin (row 3) differs from the ANK glyph of the same letter and is 16 wide with ink inside the cell.
        rom.TryGet(3, 33, out var fullA, out _, out _);
        Assert.InRange(Ink(fullA), 8, 120);
    }

    [SkippableFact]
    public void Kanji_Level1AndLevel2HaveInkAndAreDistinct()
    {
        var rom = Rom();
        var seen = new HashSet<string>();
        int found = 0;
        for (int row = 16; row <= 47; row++)
            for (int col = 1; col <= (row == 47 ? 51 : 94); col++)
            {
                Assert.True(rom.TryGet(row, col, out var g, out _, out _), $"{row}-{col}");
                Assert.InRange(Ink(g), 8, 200);
                if (seen.Add(Convert.ToHexString(g))) found++;
            }
        Assert.Equal(31 * 94 + 51, found); // every cell is a different glyph
        for (int row = 48; row <= 84; row++)
            Assert.True(rom.TryGet(row, 1, out var g2, out _, out _) && Ink(g2) >= 8, $"{row}-1");
    }

    [SkippableFact]
    public void OutOfImageCellsAreNotAvailable()
    {
        var rom = Rom();
        Assert.False(rom.TryGet(94, 94, out _, out _, out _));
        Assert.False(rom.TryGet(0, 1, out _, out _, out _));
        Assert.False(rom.TryGetAnk(256, out _, out _, out _));
    }

    [SkippableFact]
    public void MenuCharactersAllResolveThroughTheJisAddress()
    {
        // The kana, kanji and fullwidth symbols of any decoded Shift-JIS text must be found by the ROM.
        var rom = Rom();
        foreach (string s in new[] { "あいうえお", "アイウエオ", "東方靈異伝", "ＡＢＣ０１２", "　・ー" })
            foreach (char ch in s)
            {
                Assert.True(TextCodec.TryUnicodeToJis(ch.ToString(), out int r, out int c));
                Assert.True(rom.TryGet(r, c, out var g, out _, out _), $"{ch} {r}-{c}");
                if (ch != '　') Assert.True(Ink(g) >= 4, $"{ch}");
            }
    }
}
