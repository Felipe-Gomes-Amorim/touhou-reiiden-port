using System.Text;
using Th01.Assets.Config;
using Th01.Assets.Text;
using Th01.Core.Game;

namespace Th01.Assets.Tests;

public class TitleTextLoaderTests
{
    // A synthetic executable: junk, then the pool in the order of the real one, with invented strings.
    internal static byte[] FakeExe(bool withGoodbye = true, bool breakFormat = false)
    {
        var parts = new List<string> { "junk", "x.mdt", "reimu.mdt", "REIIDEN2.grp", "REIIDEN3.grp", "op_win.grp", "reiiden", "k1" };
        parts.AddRange(["m1 ", " m2", "m3", "m4"]);                                    // main (padding must survive)
        parts.AddRange(["o1", "o2", "o3", "o4", "o5"]);
        parts.AddRange(["r1", "r2", "r3", "r4"]);
        parts.AddRange(["b1", "b2", "b3", "b4", "b5", "b6", "b7"]);
        parts.Add(breakFormat ? "%s%x" : "%s%s");
        parts.AddRange(["c1", "c2"]);
        parts.AddRange(Enumerable.Range(1, 15).Select(i => $"t{i:00}"));
        parts.Add("%s%.2d");
        parts.AddRange(Enumerable.Range(1, 14).Select(i => $"f{i:00}.mdt"));
        parts.Add("batch");
        parts.Add("CON");
        if (withGoodbye) parts.Add("bye\n");
        if (withGoodbye) parts.Add("tail");

        var ms = new MemoryStream();
        foreach (string p in parts)
        {
            ms.Write(TextCodec.Encode(p));
            ms.WriteByte(0);
        }
        return ms.ToArray();
    }

    [Fact]
    public void ReadsThePoolAndKeepsPadding()
    {
        var t = TitleTextLoader.FromExe(FakeExe());
        Assert.Equal("k1", t.HitKey);
        Assert.Equal(["m1 ", " m2", "m3", "m4"], t.MainChoices);
        Assert.Equal(["o1", "o2", "o3", "o4", "o5"], t.OptionChoices);
        Assert.Equal(["r1", "r2", "r3", "r4"], t.Ranks);
        Assert.Equal(["b1", "b2"], t.BgmModes);
        Assert.Equal(["c1", "c2"], t.MusicChoices);
        Assert.Equal("t01", t.MusicTitles[0]);
        Assert.Equal("t15", t.MusicTitles[14]);
        Assert.Equal("reimu.mdt", t.MusicFiles[0]);
        Assert.Equal("f01.mdt", t.MusicFiles[1]);
        Assert.Equal("f14.mdt", t.MusicFiles[14]);
        Assert.Equal("bye\n", t.Goodbye);
        Assert.False(t.FullwidthText);
    }

    [Fact]
    public void FullwidthTextIsDetectedAndDecoded()
    {
        var exe = FakeExe();
        string asText = TextCodec.Decode(exe).Replace("m3", "ＡＢ");
        var t = TitleTextLoader.FromExe(TextCodec.Encode(asText));
        Assert.True(t.FullwidthText);
        Assert.Equal("ＡＢ", t.MainChoices[2]);
    }

    [Fact]
    public void MissingPieces_GiveDefaults()
    {
        Assert.Same(TitleTexts.Default, TitleTextLoader.FromExe(new byte[100]));
        Assert.Same(TitleTexts.Default, TitleTextLoader.FromExe(FakeExe(breakFormat: true)));
        Assert.Same(TitleTexts.Default, TitleTextLoader.FromExe(Encoding.ASCII.GetBytes("op_win.grp\0reiiden\0")));
        Assert.Same(TitleTexts.Default, TitleTextLoader.Load(new MemorySource()));
    }

    [Fact]
    public void MissingGoodbye_KeepsTheDefaultGoodbye()
    {
        var t = TitleTextLoader.FromExe(FakeExe(withGoodbye: false));
        Assert.NotEqual(TitleTexts.Default, t);
        Assert.Equal(TitleTexts.Default.Goodbye, t.Goodbye);
        Assert.Equal("m1 ", t.MainChoices[0]);
    }

    [Fact]
    public void LoadsFromASource()
    {
        var src = new MemorySource();
        src.Add("OP.EXE", FakeExe());
        Assert.Equal("k1", TitleTextLoader.Load(src).HitKey);
    }

    [SkippableFact]
    public void RealOpExe_HasTheFullTables()
    {
        string? folder = GameDataConfig.Resolve();
        Skip.If(folder is null || !File.Exists(Path.Combine(folder!, "OP.EXE")), $"OP.EXE not available. Set {GameDataConfig.EnvVar}.");
        var t = TitleTextLoader.Load(new FolderAssetSource(folder!));
        Assert.NotSame(TitleTexts.Default, t);
        Assert.True(t.FullwidthText);
        Assert.All(t.MainChoices, s => Assert.False(string.IsNullOrWhiteSpace(s)));
        Assert.All(t.OptionChoices, s => Assert.False(string.IsNullOrWhiteSpace(s)));
        Assert.Equal(4, t.Ranks.Select(s => s.Trim()).Distinct().Count());
        Assert.Equal(2, t.BgmModes.Select(s => s.Trim()).Distinct().Count());
        Assert.All(t.MusicTitles, s => Assert.False(string.IsNullOrWhiteSpace(s)));
        Assert.All(t.MusicFiles, f => Assert.EndsWith(".mdt", f, StringComparison.OrdinalIgnoreCase));
        Assert.All(t.MusicFiles, f => Assert.True(File.Exists(Path.Combine(folder!, f)) || f.Length > 0));
        Assert.False(string.IsNullOrWhiteSpace(t.Goodbye));
        Assert.NotEqual(TitleTexts.Default.Goodbye, t.Goodbye);
        Assert.Equal(15, t.MusicTitles.Length);
    }
}
