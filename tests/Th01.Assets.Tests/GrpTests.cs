using Th01.Assets;
using Th01.Assets.Config;
using Th01.Assets.Formats;

namespace Th01.Assets.Tests;

public class GrpTests
{
    private static byte[] Build(int w, int h, byte[] stream, byte[]? palette = null)
    {
        var f = new List<byte> { (byte)'Z', (byte)'N', 0x1A, 0, 0, 0, 0, 4 };
        f.AddRange("PC98"u8.ToArray());
        f.AddRange([0, 0, (byte)(w >> 8), (byte)w, (byte)(h >> 8), (byte)h]);
        f.AddRange(palette ?? Enumerable.Range(0, 48).Select(i => (byte)(i % 16)).ToArray());
        f.AddRange(stream);
        return f.ToArray();
    }

    [Fact]
    public void DecodesTheWorkedExampleOfTheFormatDescription()
    {
        // Bytes 5D 29 80 26 75 from the description: deltas, a first repeat of 0 bytes from two rows up,
        // a "last 4 bytes" repeat and more deltas.
        var img = GrpImage.Load(Build(10, 1, [0x5D, 0x29, 0x80, 0x26, 0x75]));
        Assert.Equal(new byte[] { 9, 9, 9, 1, 9, 9, 1, 8, 8, 8 }, img.Pixels);
        Assert.Equal((10, 1), (img.Width, img.Height));
        Assert.NotNull(img.Palette);
    }

    [Fact]
    public void AllowsZeroPaddingButNotOtherTrailingBytes()
    {
        Assert.NotNull(GrpImage.Load(Build(10, 1, [0x5D, 0x29, 0x80, 0x26, 0x75, 0, 0, 0])));
        Assert.Throws<InvalidDataException>(() => GrpImage.Load(Build(10, 1, [0x5D, 0x29, 0x80, 0x26, 0x75, 0, 0, 1])));
        Assert.Throws<InvalidDataException>(() => GrpImage.Load(Build(10, 1, [0x5D, 0x29])));          // truncated
        Assert.Throws<InvalidDataException>(() => GrpImage.Load(new byte[] { 1, 2, 3 }));
    }

    [Fact]
    public void MasksPaletteToFourBits()
    {
        var pal = new byte[48]; pal[0] = 0xFF;
        Assert.Equal(15, GrpImage.Load(Build(10, 1, [0x5D, 0x29, 0x80, 0x26, 0x75], pal)).Palette![0].R);
    }

    [SkippableFact]
    public void RealGrpFiles_AreFull640x400WithNoLeftoverData()
    {
        string? path = GameDataConfig.Resolve();
        Skip.If(path is null || !Directory.Exists(path), $"Game data not configured. Set {GameDataConfig.EnvVar}.");
        var files = Directory.GetFiles(path!, "*.GRP");
        Skip.If(files.Length == 0, "No .GRP files in the game folder.");
        foreach (var f in files)
        {
            var img = GrpImage.Load(File.ReadAllBytes(f));
            Assert.Equal((640, 400), (img.Width, img.Height));
            Assert.NotNull(img.Palette);
            Assert.All(img.Pixels, p => Assert.True(p < 16));
        }
    }
}

public class GrpColorKeyTests
{
    [Fact]
    public void ColorKeyMakesOnlyThatIndexTransparent()
    {
        var img = new Th01.Assets.Model.IndexedImage(2, 2, [15, 0, 3, 15]);
        var a = GrpImage.WithColorKey(img);
        Assert.Equal(new byte[] { 0, 1, 1, 0 }, a.Alpha);
        Assert.Equal(img.Pixels, a.Pixels);
        Assert.Equal(new byte[] { 1, 1, 0, 1 }, GrpImage.WithColorKey(img, 3).Alpha);
        Assert.Equal(15, GrpImage.ColorKey);
    }

    [SkippableFact]
    public void TitleImagesLoadAndTheWindowHasTransparentAndOpaqueAreas()
    {
        string? folder = GameDataConfig.Resolve();
        Skip.If(folder is null || !File.Exists(Path.Combine(folder!, "OP_WIN.GRP")), $"Game data not configured. Set {GameDataConfig.EnvVar}.");
        var assets = new GameAssets(new FolderAssetSource(folder!));
        foreach (string n in new[] { "OP_WIN.GRP", "REIIDEN2.GRP", "REIIDEN3.GRP" })
        {
            var g = assets.GetGrp(n);
            Assert.Equal((640, 400), (g.Width, g.Height));
            Assert.NotNull(g.Palette);
        }
        var win = assets.GetGrpWithColorKey("OP_WIN.GRP");
        Assert.Contains((byte)0, win.Alpha);
        Assert.Contains((byte)1, win.Alpha);
        int transparent = win.Alpha.Count(a => a == 0);
        Assert.True(transparent > win.Alpha.Length / 2, "most of the window image is transparent (it only holds the window frame)");
        Assert.Same(win, assets.GetGrpWithColorKey("OP_WIN.GRP"));
    }
}
