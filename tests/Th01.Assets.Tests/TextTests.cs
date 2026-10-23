using System.Text;
using Th01.Assets.Formats;
using Th01.Assets.Text;
using Th01.Core.Graphics;

namespace Th01.Assets.Tests;

public class TextTests
{
    static byte[] Sjis(string s)
    {
        Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
        return Encoding.GetEncoding(932).GetBytes(s);
    }

    [Fact]
    public void DecodeCell_HandlesAsciiKanjiAndInvalid()
    {
        Assert.Equal("A", ShiftJis.DecodeCell('A'));
        Assert.Equal("霊", ShiftJis.DecodeCell((ushort)((Sjis("霊")[0] << 8) | Sjis("霊")[1])));
        Assert.Null(ShiftJis.DecodeCell(0));
        Assert.Null(ShiftJis.DecodeCell(0x8100 | 0x20)); // lead byte with an invalid trail
    }

    [Fact]
    public void CodePointOf_ReturnsUnicode()
    {
        byte[] b = Sjis("あ");
        Assert.Equal(0x3042, ShiftJis.CodePointOf((ushort)((b[0] << 8) | b[1])));
    }

    [Fact]
    public void Font_LooksUpGlyphBySjisCell_AndFallsBack()
    {
        const string bdf = """
            STARTFONT 2.1
            FONTBOUNDINGBOX 16 16 0 -2
            STARTCHAR a
            ENCODING 12354
            BBX 16 16 0 -2
            BITMAP
            FFFF
            FFFF
            FFFF
            FFFF
            FFFF
            FFFF
            FFFF
            FFFF
            FFFF
            FFFF
            FFFF
            FFFF
            FFFF
            FFFF
            FFFF
            FFFF
            ENDCHAR
            ENDFONT
            """;
        var main = BdfFont.Parse(new StringReader(bdf));
        var fallback = BdfFont.LoadSpleen8x16();
        var font = new JapaneseFont(main, fallback);
        byte[] b = Sjis("あ");
        Assert.True(font.TryGetGlyph((ushort)((b[0] << 8) | b[1]), out var g));
        Assert.Equal(16, g.Width);
        Assert.True(font.TryGetGlyph('A', out _)); // from the fallback
        Assert.False(new JapaneseFont(main).TryGetGlyph('A', out _));
    }

    [Fact]
    public void ExeText_FindsJapaneseAndAsciiRuns_IgnoresNoise()
    {
        var data = new List<byte> { 0x00, 0xFF, 0x13, 0x00 };
        int jpAt = data.Count;
        data.AddRange(Sjis("霊夢は魔界の門をくぐった"));
        data.Add(0);
        int asciiAt = data.Count;
        data.AddRange(Encoding.ASCII.GetBytes("ED1A.GRP"));
        data.Add(0);
        data.AddRange(Encoding.ASCII.GetBytes("ab")); // too short
        data.Add(0);

        var t = ExeText.Scan("TEST.EXE", data.ToArray());
        Assert.Equal(2, t.Runs.Count);
        Assert.Equal(jpAt, t.Runs[0].Offset);
        Assert.Equal("霊夢は魔界の門をくぐった", t.Runs[0].Text);
        Assert.Equal(asciiAt, t.Runs[1].Offset);
        Assert.Equal("ED1A.GRP", t.Runs[1].Text);
        Assert.Single(t.Japanese);
    }

    [SkippableFact]
    public void RealExes_HoldJapaneseText()
    {
        string? folder = Th01.Assets.Config.GameDataConfig.Resolve();
        Skip.If(folder is null || !Directory.Exists(folder), "Game data not configured.");
        foreach (string exe in ExpectedFiles.TextExecutables)
        {
            string path = Path.Combine(folder!, exe);
            Skip.IfNot(File.Exists(path), $"{exe} is not in the game folder.");
            byte[] bytes = File.ReadAllBytes(path);
            var text = ExeText.Scan(exe, bytes);
            Assert.NotEmpty(text.Runs);
            Assert.All(text.Runs, r => Assert.Equal(r.Text, ShiftJis.TryDecode(bytes.AsSpan(r.Offset, r.Length))));
        }
        var main = ExeText.Scan("REIIDEN.EXE", File.ReadAllBytes(Path.Combine(folder!, "REIIDEN.EXE")));
        Assert.NotEmpty(main.Japanese);
    }

    [Fact]
    public void GameAssets_GetExeText_ReadsFromFolder()
    {
        var src = new MemorySource();
        src.Add(ExpectedFiles.ArchiveName, PackBuilder.Build());
        src.Add("REIIDEN.EXE", [..Sjis("霊夢は魔界の門をくぐった"), 0]);
        Assert.Equal("霊夢は魔界の門をくぐった", new GameAssets(src).GetExeText().Runs.Single().Text);
    }
}
