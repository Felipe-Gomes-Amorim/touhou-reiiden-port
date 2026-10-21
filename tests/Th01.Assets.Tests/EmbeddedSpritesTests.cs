using Th01.Assets.Config;
using Th01.Assets.Formats;

namespace Th01.Assets.Tests;

public class EmbeddedSpritesTests
{
    [Fact]
    public void NothingFoundInUnrelatedData()
    {
        var e = EmbeddedSprites.Locate(new byte[5000]);
        Assert.Empty(e.Found);
        Assert.Equal(EmbeddedSprites.KnownNames.Count, e.Missing.Count);
        Assert.Throws<KeyNotFoundException>(() => e.Get("pellet"));
        Assert.Throws<KeyNotFoundException>(() => e.Get("nope"));
        Assert.Empty(EmbeddedSprites.Locate(new byte[3]).Found);
    }

    [SkippableFact]
    public void RealExe_ContainsEveryKnownTable()
    {
        string? path = GameDataConfig.Resolve();
        Skip.If(path is null || !File.Exists(Path.Combine(path!, "REIIDEN.EXE")), $"Game data not configured. Set {GameDataConfig.EnvVar}.");
        var e = new GameAssets(new FolderAssetSource(path!)).GetEmbeddedSprites();
        Assert.Empty(e.Missing);
        Assert.Equal(24, e.Get("pellet").Frames.Count);
        Assert.Equal(16, e.Get("mousecur").FrameWidth);
        Assert.Equal(10, e.Get("ileave_m").Frames.Count);
        Assert.Contains((byte)1, e.Get("shape8x8").Frames[0].Pixels);
    }
}
