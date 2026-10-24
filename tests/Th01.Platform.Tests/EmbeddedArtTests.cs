using Th01.Assets.Model;
using Th01.Core.Graphics;
using Th01.Platform.MonoGame;

namespace Th01.Platform.Tests;

public class EmbeddedArtTests
{
    static SpriteSheet FakePelletTable()
    {
        // 3 cells x 8 shifts of 16x8: the cell's picture (pixel marker = cell + 1 on the diagonal) in the left 8 columns at shift 0,
        // moved right by the shift number for the other shifts.
        var frames = new List<IndexedImageA>();
        for (int cell = 0; cell < 3; cell++)
            for (int shift = 0; shift < 8; shift++)
            {
                var px = new byte[16 * 8];
                for (int y = 0; y < 8; y++) px[y * 16 + shift + y] = 1;
                if (cell == 1) px[0 * 16 + shift + 7] = 1;   // makes cell 1 differ from cell 0
                if (cell == 2) px[7 * 16 + shift] = 1;       // and cell 2
                frames.Add(new IndexedImageA(16, 8, px, (byte[])px.Clone()));
            }
        return new SpriteSheet(frames);
    }

    [Fact]
    public void PelletCells_KeepsShiftZeroOfEachCellAsAnEightByEightFrame()
    {
        var cells = SessionFactory.PelletCells(FakePelletTable());
        Assert.Equal(3, cells.Frames.Count);
        Assert.Equal((8, 8), (cells.FrameWidth, cells.FrameHeight));
        var raw = FakePelletTable();
        for (int c = 0; c < 3; c++)
            for (int y = 0; y < 8; y++)
                for (int x = 0; x < 8; x++)
                    Assert.Equal(raw.Frames[c * 8].Pixels[y * 16 + x], cells.Frames[c].Pixels[y * 8 + x]);
        Assert.Equal(cells.Frames[0].Pixels, cells.Frames[0].Alpha); // monochrome: alpha == bit
        Assert.NotEqual(cells.Frames[0].Pixels, cells.Frames[1].Pixels);
    }

    [Fact]
    public void PelletCells_RejectsAnUnexpectedTable()
    {
        var one = new SpriteSheet([new IndexedImageA(16, 8, new byte[128], new byte[128])]);
        Assert.Throws<InvalidDataException>(() => SessionFactory.PelletCells(one));
    }

    [Fact]
    public void DrawMono_PaintsOnlySetBitsInTheChosenColour()
    {
        var art = new GameAssetArt();
        art.AddSheet("m", new SpriteSheet([new IndexedImageA(2, 2, [1, 0, 0, 1], [1, 0, 0, 1])]));
        Assert.True(art.TryGetFrame("m", 0, out var f));
        var fb = new Framebuffer();
        fb.Clear(3);
        fb.DrawMono(f, 10, 10, 9);
        Assert.Equal(9, fb.GetPixel(10, 10));
        Assert.Equal(3, fb.GetPixel(11, 10));
        Assert.Equal(9, fb.GetPixel(11, 11));
        fb.DrawMono(f, 639, 399, 5);     // partly outside: must not throw
    }

    [SkippableFact]
    public void RealData_EmbeddedSheetsLoad_PelletHasThreeEightByEightFrames()
    {
        string? path = Th01.Assets.Config.GameDataConfig.Resolve();
        Skip.If(path is null || !Directory.Exists(path), "Game data not configured.");
        var assets = new Th01.Assets.FolderAssetSource(path!);
        Skip.IfNot(assets.Exists("REIIDEN.EXE"), "REIIDEN.EXE is not in the game folder.");
        var art = new SessionFactory(assets, 0, 0).Create().Art;

        Assert.Equal(3, art.FrameCount("pellet"));
        var counts = new Dictionary<string, int>
        {
            ["pellet_c"] = 2, ["laser_s"] = 8, ["bonusbox"] = 1, ["pillar"] = 2, ["shape8x8"] = 7,
            ["leaf_s"] = 1, ["leaf_l"] = 1, ["leaf_r"] = 1, ["ileave_m"] = 10, ["mousecur"] = 2,
        };
        foreach (var (name, n) in counts) Assert.Equal(n, art.FrameCount(name));

        var ink = new int[3];
        for (int c = 0; c < 3; c++)
        {
            Assert.True(art.TryGetFrame("pellet", c, out var f));
            Assert.Equal((8, 8), (f.Width, f.Height));
            Assert.True(f.Palette.IsEmpty);
            Assert.Equal(f.Pixels.ToArray(), f.Alpha.ToArray());
            ink[c] = f.Alpha.ToArray().Count(b => b != 0);
            Assert.True(ink[c] > 0, $"pellet cell {c} is empty");
        }
        Assert.True(ink[0] > ink[1], "the solid disc should have more ink than the ring");   // solid, ring, dotted ring
        Assert.True(ink[1] > ink[2], "the ring should have more ink than the dotted ring");

        var fb = new Framebuffer();
        art.TryGetFrame("pellet", 0, out var solid);
        fb.DrawMono(solid, 100, 100, 15);
        Assert.Equal(ink[0], Enumerable.Range(0, 64).Count(i => fb.GetPixel(100 + i % 8, 100 + i / 8) == 15));
    }
}
