using Th01.Assets.Model;
using Th01.Core.Graphics;
using Th01.Platform.MonoGame;
using Xunit.Abstractions;

namespace Th01.Platform.Tests;

public class MissingArtTests(ITestOutputHelper output) : IDisposable
{
    readonly string _dir = Directory.CreateTempSubdirectory("th01-miss-").FullName;
    public void Dispose() { try { Directory.Delete(_dir, true); } catch (IOException) { } }

    static SpriteSheet Sheet(int frames) =>
        new(Enumerable.Range(0, frames).Select(_ => new IndexedImageA(2, 2, new byte[4], new byte[4])).ToList());

    [Fact]
    public void AbsentSheet_OutOfRangeIndex_AndGoodIndex_AreLoggedOnlyWhenTheyFail()
    {
        var log = new MissingArtLog();
        var art = new GameAssetArt(log);
        art.AddSheet("OK.PTN", Sheet(3));

        Assert.True(art.TryGetFrame("OK.PTN", 2, out _));          // fine: nothing logged
        Assert.Equal(0, log.Count);

        Assert.False(art.TryGetFrame("NOPE.BOS", 4, out _));        // sheet not registered
        Assert.False(art.TryGetFrame("OK.PTN", 3, out _));          // index outside the sheet
        Assert.Equal(0, art.FrameCount("NOPE.BOS"));                // whole-sheet probe
        Assert.False(art.TryGetBackground(out _));
        Assert.Equal(4, log.Count);
        Assert.Contains("NOPE.BOS[4]: sheet not registered", log.Lines);
        Assert.Contains(log.Lines, l => l.StartsWith("OK.PTN[3]: frame index outside the sheet (it has 3 frame(s))"));
        Assert.Contains("NOPE.BOS: sheet not registered", log.Lines);
        Assert.Contains(log.Lines, l => l.StartsWith("background:"));
    }

    [Fact]
    public void TheSameFailureIsNotedOnce_AndALoadFailureGivesItsReason()
    {
        var log = new MissingArtLog();
        var art = new GameAssetArt(log);
        art.AddLazySheet("BAD.GRC", () => throw new InvalidDataException("truncated"));
        for (int i = 0; i < 5; i++) art.TryGetFrame("BAD.GRC", 0, out _);
        art.TryGetFrame("BAD.GRC", 1, out _);
        Assert.Equal(2, log.Count);                                  // [0] once, [1] once
        Assert.All(log.Lines, l => Assert.Contains("failed to load (truncated)", l));
    }

    [Fact]
    public void NoLog_NothingHappens()
    {
        var art = new GameAssetArt();
        Assert.False(art.TryGetFrame("X", 0, out _));
        Assert.Equal(0, art.FrameCount("X"));
    }

    [Fact]
    public void Save_WritesASortedFile_OnlyWhenThereIsSomething()
    {
        var log = new MissingArtLog();
        string path = Path.Combine(_dir, "sub", MissingArtLog.FileName);
        Assert.False(log.Save(path));
        Assert.False(File.Exists(path));
        log.Note("B.PTN", 1, "x");
        log.Note("A.PTN", -1, "y");
        Assert.True(log.Save(path));
        Assert.Equal(["A.PTN: y", "B.PTN[1]: x"], File.ReadAllLines(path));
    }

    /// <summary>The sheets the renderer asks for (GameRenderer and docs/FORMATS.md) must exist in the user's data, with enough frames.</summary>
    [SkippableFact]
    public void RealData_EverySheetTheRendererUsesIsRegisteredWithEnoughFrames()
    {
        string? path = Th01.Assets.Config.GameDataConfig.Resolve();
        Skip.If(path is null || !Directory.Exists(path), "Game data not configured.");
        var assets = new Th01.Assets.FolderAssetSource(path!);
        var log = new MissingArtLog();
        var art = new SessionFactory(assets, 0, 0, null, log).Create().Art;

        // (sheet, smallest frame count the renderer / FORMATS.md needs)
        var needed = new (string Sheet, int Min)[]
        {
            ("STG.PTN", 34), ("STG_B.PTN", 7), ("MIKO.PTN", 24), ("NUMB.PTN", 13),
            ("MIKO_AC.BOS", 28), ("MIKO_AC2.BOS", 6),
            ("BOSS1.BOS", 1), ("BOSS1_2.BOS", 1), ("BOSS1_3.BOS", 1),
            ("BOSS3_1.BOS", 1), ("BOSS3_2.BOS", 1), ("BOSS3_M.PTN", 5),
            ("KUZI1.GRC", 1), ("KUZI2.GRC", 1), ("TAMASII.BOS", 1), ("TAMASII2.BOS", 1),
            ("pellet", 3), ("pellet_c", 2), ("shape8x8", 7), ("laser_s", 8), ("pillar", 2), ("bonusbox", 1),
        };
        foreach (var (sheet, min) in needed)
        {
            int n = art.FrameCount(sheet);
            output.WriteLine($"{sheet}: {n} frame(s)");
            Assert.True(n >= min, $"{sheet} has {n} frame(s), needs at least {min}");
        }
        Assert.Equal(0, log.Count);
    }
}
