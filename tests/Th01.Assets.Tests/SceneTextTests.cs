using System.Text;
using Th01.Assets.Formats;
using Th01.Assets.Text;
using Th01.Core.Graphics;

namespace Th01.Assets.Tests;

public class SceneTextTests
{
    static byte[] Sjis(string s)
    {
        Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
        return Encoding.GetEncoding(932).GetBytes(s);
    }


    static ushort Cell(string s) { byte[] b = Sjis(s); return (ushort)((b[0] << 8) | b[1]); }

    [Fact]
    public void DefaultFont_HasKanaKanjiFullWidthLettersAndNarrowAscii()
    {
        var font = JapaneseFont.CreateDefault();
        foreach (string s in new[] { "あ", "ア", "霊", "夢", "魔", "Ａ" })
        {
            Assert.True(font.TryGetSjisGlyph(Cell(s), out var g), s);
            Assert.Equal(16, g.Width);
        }
        Assert.True(font.TryGetGlyph('A', out var a));
        Assert.Equal(8, a.Width); // ASCII comes from the 8 px font, not the 16 px cell of Shinonome
        Assert.False(font.TryGetSjisGlyph((ushort)0x8120, out _)); // invalid Shift-JIS
    }

    [Fact]
    public void DefaultFont_CoversEveryRankingNameLetter()
    {
        // The name entry stores A-Z, 0-9, '-', '.' as full-width Shift-JIS; each must have a glyph.
        var font = JapaneseFont.CreateDefault();
        for (int c = 0x8260; c <= 0x8279; c++) Assert.True(font.TryGetSjisGlyph((ushort)c, out _), c.ToString("X"));
        for (int c = 0x824F; c <= 0x8258; c++) Assert.True(font.TryGetSjisGlyph((ushort)c, out _), c.ToString("X"));
        Assert.True(font.TryGetSjisGlyph((ushort)0x817C, out _));
        Assert.True(font.TryGetSjisGlyph((ushort)0x8144, out _));
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
            Assert.All(text.Runs, r => Assert.Equal(r.Text, TextCodec.Decode(bytes.AsSpan(r.Offset, r.Length))));
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

    /// <summary>Minimal MZ image: code with <c>mov ax, imm16</c> for each string, then a data block, one relocation naming its segment.</summary>
    internal static byte[] SyntheticExe(string[] strings, out int[] dataOffsets)
    {
        const int header = 32;
        var data = new List<byte> { 0, 0, 0, 0 };
        var offsets = new List<int>();
        foreach (string str in strings)
        {
            offsets.Add(data.Count);
            data.AddRange(Encoding.ASCII.GetBytes(str));
            data.Add(0);
        }
        dataOffsets = offsets.ToArray();
        var code = new List<byte>();
        foreach (int o in dataOffsets) { code.Add(0xB8); code.Add((byte)o); code.Add((byte)(o >> 8)); code.Add(0x90); }
        code.AddRange(new byte[(16 - code.Count % 16) % 16 + 16]);
        int dataSeg = code.Count / 16;
        // the relocated word (a segment value) sits in the code, after the string loads
        int relocAt = code.Count; code.AddRange(new byte[] { (byte)dataSeg, (byte)(dataSeg >> 8), 0, 0 });
        code.AddRange(new byte[(16 - code.Count % 16) % 16]);
        dataSeg = code.Count / 16;
        code[relocAt] = (byte)dataSeg; code[relocAt + 1] = (byte)(dataSeg >> 8);
        var exe = new byte[header + code.Count + data.Count];
        exe[0] = (byte)'M'; exe[1] = (byte)'Z';
        BitConverter.GetBytes((ushort)(exe.Length % 512)).CopyTo(exe, 2);
        BitConverter.GetBytes((ushort)((exe.Length + 511) / 512)).CopyTo(exe, 4);
        BitConverter.GetBytes((ushort)1).CopyTo(exe, 6);          // one relocation
        BitConverter.GetBytes((ushort)(header / 16)).CopyTo(exe, 8);
        BitConverter.GetBytes((ushort)28).CopyTo(exe, 0x18);       // relocation table offset
        BitConverter.GetBytes((ushort)relocAt).CopyTo(exe, 28);    // offset
        BitConverter.GetBytes((ushort)0).CopyTo(exe, 30);          // segment 0
        code.CopyTo(exe, header);
        data.CopyTo(exe, header + code.Count);
        return exe;
    }

    [Fact]
    public void StringRefs_GroupLinesUnderTheImageThatPrecedesThem()
    {
        string[] strings = ["BEFORE LINE X", "A1.GRP", "first line here", "second line here", "A2.GRP", "third line here", "A3.GRP"];
        byte[] exe = SyntheticExe(strings, out _);
        var runs = ExeText.Scan("T.EXE", exe).Runs;
        var refs = ExeStringRefs.Scan(exe, runs);
        Assert.NotNull(refs);
        var scenes = ExeStringRefs.Scenes(refs!.Refs);
        Assert.Equal(["A1.GRP", "A2.GRP", "A3.GRP"], scenes.Select(s => s.Image));
        Assert.Equal([2, 1, 0], scenes.Select(s => s.Lines.Count));
        Assert.Equal("first line here", scenes[0].Lines[0].Text);
    }

    [Fact]
    public void StringRefs_NotAnExe_ReturnsNull() => Assert.Null(ExeStringRefs.Scan(new byte[100], []));

    [SkippableFact]
    public void RealFuuin_EndingScenesFollowTheCodeOrder()
    {
        string? folder = Th01.Assets.Config.GameDataConfig.Resolve();
        Skip.If(folder is null || !File.Exists(Path.Combine(folder!, "FUUIN.EXE")), "FUUIN.EXE not available.");
        var scenes = new GameAssets(new FolderAssetSource(folder!)).GetCutsceneScenes("FUUIN.EXE");
        var images = scenes.Select(s => s.Image.ToUpperInvariant()).ToList();
        string[] expected = ["ED1A.GRP", "ED1B.GRP", "ED1C.GRP", "ED1D.GRP", "ED1E.GRP", "ED2A.GRP", "ED4A.GRP", "ED3A.GRP", "ED3B.GRP",
            "ED5A.GRP", "ED5B.GRP", "ED5C.GRP", "ENDB_A.GRP", "ENDB_B.GRP", "ENDM_A.GRP"];
        Assert.Equal(expected, images.Where(expected.Contains));
        Assert.All(ExpectedFiles.CutsceneImages.Where(i => i.StartsWith("ED")), name => Assert.Contains(name, images));
    }

    [SkippableFact]
    public void RealScoreFile_NamesDecodeAndHaveGlyphs()
    {
        string? folder = Th01.Assets.Config.GameDataConfig.Resolve();
        string? path = folder is null ? null : Path.Combine(folder, "REYHILU.DAT");
        Skip.If(path is null || !File.Exists(path), "REYHILU.DAT not available.");
        var file = ScoreFileIO.Parse(File.ReadAllBytes(path!));
        var font = JapaneseFont.CreateDefault();
        int cells = 0;
        foreach (ushort[] name in file.Names)
        {
            Assert.NotEmpty(TextCodec.DecodeCells(name));
            foreach (ushort cell in name)
            {
                if (cell == 0x2020) continue; // two ASCII spaces
                cells++;
                // a cell is one double-byte character, or two single-byte ones
                if (cell >> 8 >= 0x81) Assert.True(font.TryGetSjisGlyph(cell, out _), $"no glyph for 0x{cell:X4}");
            }
        }
        Assert.True(cells > 0);
    }

    static readonly string[] VerdictLayout =
    [
        "endm_a.grp", "Title", "ver", "1.00", "Rank   %s", "Highest %7lu", "Score   %7lu", "Continues", "Shrine %3lu",
        "Gate %3lu", "Wayside %3lu", "Vina %3lu", "Cesspool %3lu", "Fallen %3lu", "Quiet %3lu", "Makai total %5lu", "Hell total %5lu",
        "Thank you", "Assessment",
    ];

    [Fact]
    public void Verdict_TakesTheStringsInCodeOrder_AndPairsMakaiAndJigokuLines()
    {
        byte[] exe = SyntheticExe(VerdictLayout, out _);
        var refs = ExeStringRefs.Scan(exe, ExeText.Scan("T.EXE", exe).Runs);
        Assert.NotNull(refs);
        var v = ExeStringRefs.Verdict(exe, refs!);
        Assert.NotNull(v);
        Assert.Equal("Rank   %s", v!.Rank);
        Assert.Equal("Highest %7lu", v.HighScore);
        Assert.Equal("Shrine %3lu", v.Shrine);
        Assert.Equal(["Gate %3lu", "Vina %3lu", "Fallen %3lu"], v.Makai);
        Assert.Equal(["Wayside %3lu", "Cesspool %3lu", "Quiet %3lu"], v.Jigoku);
        Assert.Equal("Hell total %5lu", v.JigokuTotal);
        Assert.Equal("Assessment", v.Assessment);
    }

    [Fact]
    public void Verdict_IsNullWhenTheCodeDoesNotLookLikeTheVerdictScreen()
    {
        string[] wrong = [.. VerdictLayout];
        wrong[4] = "Rank without a value"; // the rank line must have a placeholder
        byte[] exe = SyntheticExe(wrong, out _);
        var refs = ExeStringRefs.Scan(exe, ExeText.Scan("T.EXE", exe).Runs)!;
        Assert.Null(ExeStringRefs.Verdict(exe, refs));
    }

    [Fact]
    public void PrintfLite_FillsTheGamesPlaceholders()
    {
        Assert.Equal("Rank   NORMAL", PrintfLite.Format("Rank   %s", "NORMAL"));
        Assert.Equal("Score     12345", PrintfLite.Format("Score   %7lu", 12345u));
        Assert.Equal("a   7 b    42", PrintfLite.Format("a %3lu b %5lu", 7, 42));
        Assert.Equal("%7lu", PrintfLite.Format("%7lu"));                    // no argument: left as it is
        Assert.Equal("no placeholder", PrintfLite.Format("no placeholder", 1));
    }

    [SkippableFact]
    public void RealFuuin_HasTheVerdictStrings()
    {
        string? folder = Th01.Assets.Config.GameDataConfig.Resolve();
        Skip.If(folder is null || !File.Exists(Path.Combine(folder!, "FUUIN.EXE")), "FUUIN.EXE not available.");
        byte[] exe = File.ReadAllBytes(Path.Combine(folder!, "FUUIN.EXE"));
        var refs = ExeStringRefs.Scan(exe, ExeText.Scan("FUUIN.EXE", exe).Runs)!;
        var v = ExeStringRefs.Verdict(exe, refs);
        Assert.NotNull(v);
        Assert.Contains("%s", v!.Rank);
        Assert.Contains("%7lu", v.HighScore);
        Assert.Contains("%7lu", v.Score);
        Assert.Contains("%3lu", v.Shrine);
        Assert.All(v.Makai.Concat(v.Jigoku), l => Assert.Contains("%3lu", l));
        Assert.Contains("%5lu", v.MakaiTotal);
        Assert.Contains("%5lu", v.JigokuTotal);
    }
}
