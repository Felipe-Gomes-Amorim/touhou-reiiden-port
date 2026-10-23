using Th01.Core.Game;
using Th01.Assets.Config;
using Th01.Assets.Model;
using Th01.Core.Graphics;
using Th01.Core.Input;
using Th01.Core.Sim;
using Th01.Platform.MonoGame;

namespace Th01.Platform.Tests;

public class WaveTwoTests
{
    [Theory]
    [InlineData(0, 0, 0), InlineData(4, 1, 0), InlineData(5, 0, 1), InlineData(5, 1, 2), InlineData(10, 0, 3), InlineData(19, 1, 6)]
    public void SceneId_FollowsTheOriginalTable(int stage, int route, int scene) =>
        Assert.Equal(scene, SessionFactory.SceneId(stage, route));

    [Fact]
    public void ToStageSetup_CopiesGridsWithoutNormalising()
    {
        var layout = new StageLayout();
        layout.Cards[3, 7] = true;
        layout.Obstacles[3, 7] = 15; // card with 3 HP: raw byte kept
        layout.Obstacles[9, 19] = 21;
        var setup = SessionFactory.ToStageSetup(layout, stageId: 6, route: 1);
        Assert.True(setup.Cards[3, 7]);
        Assert.Equal(15, setup.Obstacles[3, 7]);
        Assert.Equal(21, setup.Obstacles[9, 19]);
        Assert.Equal((6, 1), (setup.StageId, setup.Route));
        setup.Validate();
    }

    [Fact]
    public void GameAssetArt_ExposesFramesBackgroundAndPalette()
    {
        var pal = new Palette4();
        pal.Set(1, 15, 8, 0);
        var frame = new IndexedImageA(2, 1, [1, 15], [1, 0], pal);
        var art = new GameAssetArt();
        art.AddSheet("X.PTN", new SpriteSheet([frame]));
        art.SetBackground(new IndexedImage(2, 2, [1, 1, 1, 1], pal));

        Assert.Equal(1, art.FrameCount("x.ptn"));
        Assert.Equal(0, art.FrameCount("none"));
        Assert.True(art.TryGetFrame("X.PTN", 0, out var f));
        Assert.Equal([1, 0], f.Alpha.ToArray());
        Assert.False(art.TryGetFrame("X.PTN", 1, out _));
        Assert.True(art.TryGetBackground(out var bg));
        var fb = new Framebuffer();
        fb.ApplyPalette(bg);
        fb.GetPalette4(1, out int r, out int g, out int b);
        Assert.Equal((15, 8, 0), (r, g, b));
        fb.Draw(f, 5, 5);
        Assert.Equal(1, fb.GetPixel(5, 5));
        Assert.Equal(0, fb.GetPixel(6, 5)); // transparent pixel not drawn
    }

    [Fact]
    public void Compose_OverlaysNonZeroPixelsRemappedToBackgroundPalette()
    {
        var bg = new Framebuffer();
        for (int i = 0; i < 16; i++) bg.SetPalette4(i, 0, 0, 0);
        bg.SetPalette4(5, 15, 0, 0); // red in the background palette
        bg.Clear(2);
        var overlay = new Framebuffer();
        overlay.SetPalette4(9, 14, 1, 1); // the session's "red" lives at index 9
        overlay.SetPixel(10, 10, 9);
        var target = new Framebuffer();
        SceneCompositor.Compose(target, bg, overlay);
        Assert.Equal(5, target.GetPixel(10, 10)); // nearest red
        Assert.Equal(2, target.GetPixel(11, 10)); // background untouched
    }

    [Fact]
    public void DrawMessage_DrawsPanelWithText()
    {
        var fb = new Framebuffer();
        fb.SetPalette4(0, 0, 0, 0);
        fb.SetPalette4(7, 15, 15, 15);
        SceneCompositor.DrawMessage(fb, BdfFont.LoadSpleen8x16(), "GAME OVER", "SCORE 0");
        int lit = 0;
        for (int y = 150; y < 250; y++) for (int x = 200; x < 440; x++) if (fb.GetPixel(x, y) == 7) lit++;
        Assert.True(lit > 100);
    }

    [Fact]
    public void InputScript_RoundTripsAndReplaysPerTick()
    {
        var s = new InputScript();
        for (int i = 0; i < 5; i++) s.Append(GameButtons.Left);
        s.Append(GameButtons.None);
        for (int i = 0; i < 3; i++) s.Append(GameButtons.Shot | GameButtons.Right);
        Assert.Equal(9, s.TickCount);

        string path = Path.Combine(Path.GetTempPath(), "th01-script-" + Guid.NewGuid().ToString("N") + ".json");
        try
        {
            s.Save(path);
            var l = InputScript.TryLoad(path, out string? err);
            Assert.Null(err);
            Assert.NotNull(l);
            Assert.Equal(9, l!.TickCount);
            for (int t = 0; t < 9; t++) Assert.Equal(s.At(t), l.At(t));
            Assert.Equal(GameButtons.Left, l.At(4));
            Assert.Equal(GameButtons.None, l.At(5));
            Assert.Equal(GameButtons.Shot | GameButtons.Right, l.At(8));
            Assert.Equal(GameButtons.None, l.At(9));
        }
        finally { File.Delete(path); }
    }

    [Fact]
    public void InputScript_RejectsMissingAndMalformedFiles()
    {
        Assert.Null(InputScript.TryLoad("does-not-exist.json", out string? e1));
        Assert.NotNull(e1);
        string path = Path.Combine(Path.GetTempPath(), "th01-bad-" + Guid.NewGuid().ToString("N") + ".json");
        File.WriteAllText(path, "{ not json");
        try { Assert.Null(InputScript.TryLoad(path, out string? e2)); Assert.NotNull(e2); }
        finally { File.Delete(path); }
    }

    static string DataFolder()
    {
        string? path = GameDataConfig.Resolve();
        Skip.If(path is null || !Directory.Exists(path), $"Game data not configured. Set {GameDataConfig.EnvVar} or create {GameDataConfig.FileName}.");
        return path!;
    }

    [SkippableTheory]
    [InlineData(0, 0), InlineData(3, 0), InlineData(5, 0), InlineData(5, 1), InlineData(12, 1)]
    public void RealData_FactoryBuildsARunningSessionAndFrames(int stage, int route)
    {
        var start = new SessionFactory(new Th01.Assets.FolderAssetSource(DataFolder()), stage, route).Create();
        Assert.Equal(SessionOutcome.Running, start.Session.Outcome);
        Assert.True(start.Art.TryGetBackground(out var bg));
        Assert.Equal((640, 400), (bg.Width, bg.Height));
        for (int i = 0; i < 120; i++) start.Session.Step(i % 7 == 0 ? GameButtons.Shot : GameButtons.Left);
        start.Session.Render(new Framebuffer());
    }

    [SkippableTheory]
    [InlineData(0, 0, "STG.PTN"), InlineData(0, 0, "MIKO.PTN"), InlineData(9, 1, "BOSS3_1.BOS"), InlineData(4, 0, "BOSS1.BOS")]
    public void RealData_SpriteSheetsLoadFromTheArchive(int stage, int route, string sheet)
    {
        var start = new SessionFactory(new Th01.Assets.FolderAssetSource(DataFolder()), stage, route).Create();
        Assert.True(start.Art.FrameCount(sheet) > 0, $"{sheet} has no frames: the game would draw flat boxes");
        Assert.True(start.Art.TryGetFrame(sheet, 0, out var f));
        Assert.True(f.Width > 0 && f.Height > 0);
    }

    [SkippableFact]
    public void RealData_RenderedStageUsesSprites_NotFlatBoxes()
    {
        var factory = new SessionFactory(new Th01.Assets.FolderAssetSource(DataFolder()), 0, 0);
        StageSetup? setup = null;
        var start = factory.Create(s => { setup = s; return new GameOptions { Stage = s, FrameRand = 0 }; });
        var boxesOnly = new GameSession(new GameOptions { Stage = setup!, FrameRand = 0 });
        for (int i = 0; i < 60; i++) { start.Session.Step(GameButtons.None); boxesOnly.Step(GameButtons.None); }
        var a = new Framebuffer();
        var b = new Framebuffer();
        start.Session.Render(a);
        boxesOnly.Render(b);
        Assert.False(a.Pixels.SequenceEqual(b.Pixels), "the session was rendered without sprites (flat boxes only)");
    }

    [SkippableFact]
    public void RealData_SameInputsSameHash()
    {
        string folder = DataFolder();
        ulong Run()
        {
            var s = new SessionFactory(new Th01.Assets.FolderAssetSource(folder), 0, 0).Create().Session;
            for (int i = 0; i < 300; i++) s.Step(i % 5 == 0 ? GameButtons.Shot | GameButtons.Right : GameButtons.Right);
            return s.StateHash();
        }
        Assert.Equal(Run(), Run());
    }

    [Fact]
    public void MissingFiles_GiveAReadableMessage()
    {
        string dir = Path.Combine(Path.GetTempPath(), "th01-empty-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        try
        {
            var ex = Assert.Throws<FileNotFoundException>(() => new SessionFactory(new Th01.Assets.FolderAssetSource(dir)).Create());
            Assert.Contains("STAGE0.DAT", ex.Message);
        }
        finally { Directory.Delete(dir, true); }
    }
}
