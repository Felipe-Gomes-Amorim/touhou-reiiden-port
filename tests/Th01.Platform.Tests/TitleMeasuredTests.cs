using Th01.Core.Game;
using Th01.Core.Graphics;
using Th01.Core.Input;
using Th01.Platform.MonoGame;
using Xunit.Abstractions;

namespace Th01.Platform.Tests;

/// <summary>
/// Positions and colours measured on the original OP.EXE (docs/TITLE-MEASURED.md, private): ink bounding boxes of the menu rows.
/// Needs the Japanese OP.EXE texts and pictures, so it only runs with game data. Our font is not the PC-98 ROM font, so the boxes
/// are compared with a few pixels of tolerance.
/// </summary>
public class TitleMeasuredTests(ITestOutputHelper output)
{
    const int Tol = 4;

    static (TitleScene Scene, Framebuffer Fb) OpenMenu()
    {
        string? path = Th01.Assets.Config.GameDataConfig.Resolve();
        Skip.If(path is null || !Directory.Exists(path), "Game data not configured.");
        var assets = new Th01.Assets.FolderAssetSource(path!);
        Skip.IfNot(assets.Exists("OP.EXE"), "OP.EXE is not in the game folder.");
        var texts = Th01.Assets.Text.TitleTextLoader.Load(assets);
        Skip.IfNot(texts.FullwidthText, "This needs the Japanese OP.EXE (the texts are full-width there).");
        var t = new TitleScene(new AssetScreenImages(assets), Th01.Assets.Text.JapaneseFont.CreateDefault(), new GameSettings(), texts);
        for (int i = 0; i < TitleScene.FirstHitKeyTick - 1; i++) t.Tick(GameButtons.None);
        t.Tick(GameButtons.Shot); t.Tick(GameButtons.None);
        return (t, new Framebuffer());
    }

    /// <summary>Bounding box (x0, y0, x1, y1) of pixels of one colour inside the menu area, or null.</summary>
    static (int X0, int Y0, int X1, int Y1)? Ink(Framebuffer fb, byte colour, int yFrom, int yTo)
    {
        int x0 = int.MaxValue, y0 = int.MaxValue, x1 = -1, y1 = -1;
        for (int y = yFrom; y < yTo; y++)
            for (int x = 214; x < 414; x++)
                if (fb.GetPixel(x, y) == colour) { x0 = Math.Min(x0, x); x1 = Math.Max(x1, x); y0 = Math.Min(y0, y); y1 = Math.Max(y1, y); }
        return x1 < 0 ? null : (x0, y0, x1, y1);
    }

    void Check(string what, Framebuffer fb, byte colour, int rowTop, int x0, int x1, int top)
    {
        var box = Ink(fb, colour, rowTop - 2, rowTop + 18);
        output.WriteLine($"{what}: ours {(box is { } b ? $"x {b.X0}-{b.X1} top {b.Y0}" : "none")}; measured x {x0}-{x1} top {top}");
        Assert.NotNull(box);
        Assert.InRange(box!.Value.X0, x0 - Tol, x0 + Tol);
        Assert.InRange(box.Value.X1, x1 - Tol, x1 + Tol);
        Assert.InRange(box.Value.Y0, top - 3, top + 3);
    }

    [SkippableFact]
    public void MainMenu_InkMatchesTheMeasuredBoxes()
    {
        var (t, fb) = OpenMenu();
        t.Render(fb);
        Check("START (active)", fb, 15, 276, 269, 345, 277);
        Check("CONTINUE", fb, 5, 296, 245, 368, 297);
        Check("OPTION", fb, 5, 316, 261, 352, 317);
        Check("QUIT", fb, 5, 336, 277, 337, 337);
    }

    [SkippableFact]
    public void Options_InkMatchesTheMeasuredBoxes()
    {
        var (t, fb) = OpenMenu();
        t.Tick(GameButtons.Down); t.Tick(GameButtons.None);
        t.Tick(GameButtons.Down); t.Tick(GameButtons.None);
        t.Tick(GameButtons.Ok); t.Tick(GameButtons.None);       // OPTION
        t.Tick(GameButtons.Down); t.Tick(GameButtons.None);     // MUSIC active, as in the measurement
        t.Render(fb);
        Check("RANK row (label + NORMAL)", fb, 5, 266, 246, 378, 267);
        Check("MUSIC row (active: label + FM)", fb, 15, 286, 237, 362, 287);
        Check("PLAYER row (label + 5)", fb, 5, 306, 231, 362, 307);
        Check("M.TEST", fb, 5, 326, 229, 321, 327);
        Check("QUIT", fb, 5, 346, 245, 305, 347);
    }

    [SkippableFact]
    public void MusicTest_InkMatchesTheMeasuredBoxes()
    {
        var (t, fb) = OpenMenu();
        t.Tick(GameButtons.Down); t.Tick(GameButtons.None);
        t.Tick(GameButtons.Down); t.Tick(GameButtons.None);
        t.Tick(GameButtons.Ok); t.Tick(GameButtons.None);       // OPTION
        for (int i = 0; i < 3; i++) { t.Tick(GameButtons.Down); t.Tick(GameButtons.None); }   // M.TEST
        t.Tick(GameButtons.Ok); t.Tick(GameButtons.None);
        t.Render(fb);
        Check("MUSIC No. 00", fb, 15, 286, 229, 386, 287);
        var title = Ink(fb, 15, 308, 326);
        output.WriteLine($"track title: ours {title}; measured x 260-353 top 310");
        Assert.NotNull(title);
        Assert.InRange(title!.Value.X0, 260 - Tol, 260 + Tol);
        Assert.InRange(title.Value.X1, 353 - Tol, 353 + Tol);
        Check("Quit", fb, 5, 326, 261, 318, 327);
    }
}
