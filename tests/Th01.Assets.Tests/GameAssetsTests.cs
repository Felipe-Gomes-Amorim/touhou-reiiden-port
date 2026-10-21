using System.Text;
using Th01.Assets.Config;

namespace Th01.Assets.Tests;

internal sealed class MemorySource : IAssetSource
{
    private readonly Dictionary<string, byte[]> _files = new(StringComparer.OrdinalIgnoreCase);
    public void Add(string name, byte[] data) => _files[name] = data;
    public bool Exists(string name) => _files.ContainsKey(name);
    public long? GetSize(string name) => _files.TryGetValue(name, out var d) ? d.Length : null;
    public Stream Open(string name) => _files.TryGetValue(name, out var d) ? new MemoryStream(d) : throw new FileNotFoundException(name);
}

public class GameAssetsTests
{
    private static byte[] Ptn()
    {
        var f = new List<byte>(Encoding.ASCII.GetBytes("HPTN")) { 1, 1 };
        f.AddRange(new byte[48]); f.Add(0); f.AddRange(new byte[512]);
        return f.ToArray();
    }

    private static byte[] Stage()
    {
        var f = new byte[22 + 250 * 5];
        Encoding.ASCII.GetBytes("STAGE").CopyTo(f, 0);
        f[22 + 250 * 3 + 50] = 7; // scene stage 3, obstacle (0,0)
        return f;
    }

    private static MemorySource Source()
    {
        var s = new MemorySource();
        s.Add(ExpectedFiles.ArchiveName, PackBuilder.Build(("X.PTN", Ptn(), true), ("STAGE1.DAT", Stage(), false), ("BAD.BOS", [1, 2, 3], false)));
        return s;
    }

    [Fact]
    public void LoadsAndCaches()
    {
        var a = new GameAssets(Source());
        Assert.Contains("X.PTN", a.PackNames);
        var p = a.GetPtn("x.ptn");
        Assert.Single(p.Frames);
        Assert.Same(p, a.GetPtn("X.PTN"));
        Assert.Equal(7, a.GetStage(1, 3).Obstacles[0, 0]);
        Assert.Equal(7, a.GetStage(1 * 5 + 3).Obstacles[0, 0]);
        Assert.Same(a.GetScene(1), a.GetScene(1));
    }

    [Fact]
    public void ErrorsAreClear()
    {
        var a = new GameAssets(Source());
        Assert.Throws<FileNotFoundException>(() => a.GetPtn("NOPE.PTN"));
        Assert.Throws<FileNotFoundException>(() => a.GetGrp("ST0.GRP"));
        Assert.Throws<FileNotFoundException>(() => a.GetStage(2, 0));
        var e = Assert.Throws<InvalidDataException>(() => a.GetBos("BAD.BOS"));
        Assert.Contains("BAD.BOS", e.Message);
        Assert.Throws<ArgumentException>(() => a.GetBos("X.PTN"));
        Assert.Throws<ArgumentOutOfRangeException>(() => a.GetStage(1, 5));
        Assert.Throws<FileNotFoundException>(() => new GameAssets(new MemorySource()));
    }

    [Fact]
    public void BadArchive_NamesTheFile()
    {
        var s = new MemorySource();
        s.Add(ExpectedFiles.ArchiveName, new byte[10]);
        Assert.Contains(ExpectedFiles.ArchiveName, Assert.Throws<InvalidDataException>(() => new GameAssets(s)).Message);
    }

    [SkippableFact]
    public void RealData_LoadsEverythingTheGameNeeds()
    {
        string? path = GameDataConfig.Resolve();
        Skip.If(path is null || !Directory.Exists(path), $"Game data not configured. Set {GameDataConfig.EnvVar}.");
        var a = new GameAssets(new FolderAssetSource(path!));
        for (int scene = 0; scene <= 7; scene++) Assert.Equal(5, a.GetScene(scene).Count);
        Assert.Equal(34, a.GetPtn("STG.PTN").Frames.Count);
        Assert.Equal(28, a.GetPtn("MIKO.PTN").Frames.Count);
        Assert.Equal(13, a.GetPtn("NUMB.PTN").Frames.Count);
        Assert.Equal((640, 400), (a.GetGrp("ST0.GRP").Width, a.GetGrp("ST0.GRP").Height));
        Assert.Equal(16, a.GetGrz("BOSS8.GRZ").Count);
        Assert.Equal(66, a.GetHudMask().Height);
        Assert.NotEmpty(a.GetBos("BOSS1.BOS").Frames);
        Assert.NotEmpty(a.GetGrc("KUZI1.GRC").Frames);
    }
}
