using System.Text;
using Th01.Assets;
using Th01.Assets.Formats;
using Th01.Assets.Model;
using Th01.Platform.MonoGame;

namespace Th01.Platform.Tests;

/// <summary>The boss pictures registered by name for the Core (Mima, Konngara and the not yet wired Sariel).</summary>
public class BossPicturesTests : IDisposable
{
    readonly string _dir = Directory.CreateTempSubdirectory("th01-boss-").FullName;

    /// <summary>The worked example of the .GRP description: a 10x1 picture of indices 9 9 9 1 9 9 1 8 8 8 with a palette.</summary>
    static byte[] TinyGrp()
    {
        var f = new List<byte> { (byte)'Z', (byte)'N', 0x1A, 0, 0, 0, 0, 4 };
        f.AddRange("PC98"u8.ToArray());
        f.AddRange([0, 0, 0, 10, 0, 1]);
        f.AddRange(Enumerable.Range(0, 48).Select(i => (byte)(i % 16)));
        f.AddRange([0x5D, 0x29, 0x80, 0x26, 0x75]);
        return f.ToArray();
    }

    static byte[] Grx(byte[] rle, params byte[][] planars)
    {
        var f = new List<byte>(Encoding.ASCII.GetBytes("HGRX")) { (byte)planars.Length, 0, 0, 0 };
        f.AddRange(BitConverter.GetBytes((ushort)rle.Length));
        f.AddRange(BitConverter.GetBytes((ushort)planars[0].Length));
        f.AddRange(new byte[4]); f.AddRange(Enumerable.Range(0, 48).Select(i => (byte)(i % 16))); f.AddRange(rle);
        foreach (var p in planars) f.AddRange(p);
        return f.ToArray();
    }

    /// <summary>A .GRZ of three images with 1, 1 and 2 frames.</summary>
    static byte[] TinyGrz()
    {
        byte[] g1 = Grx([0], [0x80, 0, 0, 0]);
        byte[] g2 = Grx([0], [0, 0, 0, 0x80]);
        byte[] g3 = Grx([0], [0, 0, 0, 0x80], [0x80, 0, 0, 0]);
        var f = new List<byte>(Encoding.ASCII.GetBytes("HGRZ")) { 3, 0, 0, 0 };
        int start = 4 + 4 + 64 + 4 + 20;
        var offs = new int[16]; offs[0] = start; offs[1] = start + g1.Length; offs[2] = start + g1.Length + g2.Length;
        foreach (int o in offs) f.AddRange(BitConverter.GetBytes(o));
        f.AddRange(new byte[4 + 20]); f.AddRange(g1); f.AddRange(g2); f.AddRange(g3);
        return f.ToArray();
    }

    GameAssetArt Register(Action<string>? extra = null)
    {
        foreach (string n in SessionFactory.OpaquePictureNames.Concat(SessionFactory.KeyedPictureNames)) File.WriteAllBytes(Path.Combine(_dir, n), TinyGrp());
        File.WriteAllBytes(Path.Combine(_dir, SessionFactory.BossGrz), TinyGrz());
        extra?.Invoke(_dir);
        var art = new GameAssetArt();
        new SessionFactory(new FolderAssetSource(_dir)).RegisterBossPictures(art);
        return art;
    }

    [Fact]
    public void TheRequestedNamesAreRegistered()
    {
        Assert.Equal(["BOSS7_D1.GRP", "BOSS8_A1.GRP", "BOSS6_A.GRP", "BOSS6_A1.GRP", "BOSS6_A2.GRP", "BOSS6_A3.GRP", "BOSS6_A4.GRP", "BOSS6_A5.GRP", "BOSS6_A6.GRP", "BOSS6_H.GRP", "BOSS6_L.GRP"], SessionFactory.OpaquePictureNames);
        Assert.Equal(["BOSS8_D1.GRP", "BOSS8_D2.GRP", "BOSS8_D3.GRP", "BOSS8_D4.GRP"], SessionFactory.KeyedPictureNames);
        Assert.Equal("BOSS8.GRZ", SessionFactory.BossGrz);
    }

    [Fact]
    public void OpaquePictures_AreOneFrameWithPaletteAndEveryPixelDrawn()
    {
        var art = Register();
        foreach (string n in SessionFactory.OpaquePictureNames)
        {
            Assert.Equal(1, art.FrameCount(n));
            Assert.True(art.TryGetFrame(n, 0, out var f));
            Assert.Equal((10, 1), (f.Width, f.Height));
            Assert.Equal(48, f.Palette.Length);
            Assert.All(f.Alpha.ToArray(), a => Assert.Equal(1, a));
            Assert.Equal(new byte[] { 9, 9, 9, 1, 9, 9, 1, 8, 8, 8 }, f.Pixels.ToArray());
        }
        Assert.False(art.TryGetFrame("BOSS8_A1.GRP", 1, out _));        // there is only frame 0
    }

    [Fact]
    public void KeyedPictures_AreOneFrame_TransparentOnTheColourKey()
    {
        var art = Register();
        foreach (string n in SessionFactory.KeyedPictureNames) Assert.Equal(1, art.FrameCount(n));
        var img = new IndexedImage(4, 1, [15, 9, 0, 15], new Palette4());
        var f = SessionFactory.KeyedSheet(img).Frames[0];
        Assert.Equal(new byte[] { 0, 1, 1, 0 }, f.Alpha);               // 15 is the key; 0 is drawn (the real overlays have no index 0)
        var o = SessionFactory.OpaqueSheet(img).Frames[0];
        Assert.Equal(new byte[] { 1, 1, 1, 1 }, o.Alpha);
    }

    [Fact]
    public void Grz_IsOneSheetOfAllItsFrames()
    {
        var art = Register();
        Assert.Equal(4, art.FrameCount("BOSS8.GRZ"));                    // 1 + 1 + 2
        for (int i = 0; i < 4; i++) Assert.True(art.TryGetFrame("BOSS8.GRZ", i, out var f) && f.Width == 640 && f.Height == 400);
        Assert.False(art.TryGetFrame("BOSS8.GRZ", 4, out _));
        var sheets = new[] { new SpriteSheet([new IndexedImageA(2, 1, [1, 2], [1, 1])]), new SpriteSheet([new IndexedImageA(2, 1, [3, 4], [1, 1]), new IndexedImageA(2, 1, [5, 6], [1, 1])]) };
        var flat = SessionFactory.GrzFrames(sheets);
        Assert.Equal([new byte[] { 1, 2 }, [3, 4], [5, 6]], flat.Frames.Select(f => f.Pixels));
    }

    [Fact]
    public void MissingFiles_JustReturnFalse_NoRegistrationNoCrash()
    {
        var art = new GameAssetArt();
        new SessionFactory(new FolderAssetSource(_dir)).RegisterBossPictures(art);   // empty folder
        foreach (string n in SessionFactory.OpaquePictureNames.Concat(SessionFactory.KeyedPictureNames).Append("BOSS8.GRZ"))
        {
            Assert.Equal(0, art.FrameCount(n));
            Assert.False(art.TryGetFrame(n, 0, out _));
        }
    }

    [Fact]
    public void UnreadableFile_IsNotACrashAtRegistration_ItFailsOnlyWhenAsked()
    {
        var art = Register(d => File.WriteAllBytes(Path.Combine(d, "BOSS8_D2.GRP"), [1, 2, 3]));
        Assert.Equal(1, art.FrameCount("BOSS8_D1.GRP"));
        Assert.False(art.TryGetFrame("BOSS8_D2.GRP", 0, out _));          // damaged: no frame, the others still work
        Assert.True(art.TryGetFrame("BOSS8_D3.GRP", 0, out _));
    }

    [Fact]
    public void Stage20_Jigoku_UsesTheKonngaraBackground_MakaiKeepsItsStBackground()
    {
        int scene = SessionFactory.SceneId(19, 1);
        Assert.Equal("BOSS8_A1.GRP", SessionFactory.BackgroundFile(19, 1, scene));
        Assert.Equal("ST5.GRP", SessionFactory.BackgroundFile(19, 0, SessionFactory.SceneId(19, 0)));
        Assert.Equal("BOSS3.GRP", SessionFactory.BackgroundFile(9, 1, SessionFactory.SceneId(9, 1)));   // the others are unchanged
    }

    // ---- the player's real files ------------------------------------------------------------------------------------------

    static string? Data()
    {
        string? d = Environment.GetEnvironmentVariable("TH01_DATA_PATH");
        return d is not null && File.Exists(Path.Combine(d, "BOSS8_A1.GRP")) && File.Exists(Path.Combine(d, "BOSS8.GRZ")) ? d : null;
    }

    [SkippableFact]
    public void RealFiles_HaveTheExpectedFramesAndSizes()
    {
        string? d = Data();
        Skip.If(d is null, "no real boss files");
        var art = new GameAssetArt();
        var factory = new SessionFactory(new FolderAssetSource(d!));
        factory.RegisterBossPictures(art);
        foreach (string n in SessionFactory.OpaquePictureNames)
        {
            Assert.Equal(1, art.FrameCount(n));
            Assert.True(art.TryGetFrame(n, 0, out var f), n);
            Assert.Equal((640, 400), (f.Width, f.Height));
            Assert.Equal(48, f.Palette.Length);
            Assert.DoesNotContain((byte)0, f.Alpha.ToArray());
        }
        foreach (string n in SessionFactory.KeyedPictureNames)
        {
            Assert.True(art.TryGetFrame(n, 0, out var f), n);
            Assert.Equal((640, 400), (f.Width, f.Height));
            int clear = f.Alpha.ToArray().Count(a => a == 0), drawn = f.Alpha.Length - clear;
            Assert.InRange(clear, 200_000, 255_000);                    // most of the overlay is the key colour
            Assert.InRange(drawn, 1_000, 30_000);                       // and a real drawing is left
            Assert.All(f.Pixels.ToArray().Zip(f.Alpha.ToArray()).Where(p => p.Second == 0), p => Assert.Equal(15, p.First));
        }
        Assert.Equal(16, art.FrameCount("BOSS8.GRZ"));                  // the real file has 16 images of one frame each (not 7)
        for (int i = 0; i < 16; i++)
        {
            Assert.True(art.TryGetFrame("BOSS8.GRZ", i, out var f));
            Assert.Equal((640, 400), (f.Width, f.Height));
            Assert.Contains((byte)1, f.Alpha.ToArray());                // every image has strokes
        }
    }

    [SkippableFact]
    public void RealGame_Stage20Jigoku_ShowsTheKonngaraPictureAsBackground()
    {
        string? d = Data();
        Skip.If(d is null || !File.Exists(Path.Combine(d!, "STAGE6.DAT")) && !File.Exists(Path.Combine(d!, ExpectedFiles.ArchiveName)), "no real game files");
        var start = new SessionFactory(new FolderAssetSource(d!), stageId: 19, route: 1).Create();
        Assert.True(start.Art.TryGetBackground(out var bg));
        var expected = GrpImage.Load(File.ReadAllBytes(Path.Combine(d!, "BOSS8_A1.GRP")));
        Assert.Equal(expected.Pixels, bg.Pixels.ToArray());
    }

    [SkippableFact]
    public void RealPack_HasTheSarielGrcSheets_AlreadyRegistered()
    {
        string? d = Data();
        Skip.If(d is null || !File.Exists(Path.Combine(d!, ExpectedFiles.ArchiveName)), "no archive");
        var start = new SessionFactory(new FolderAssetSource(d!), stageId: 0).Create();
        foreach (string n in new[] { "BOSS6GR1.GRC", "BOSS6GR2.GRC", "BOSS6GR3.GRC", "BOSS6GR4.GRC" })
            Assert.True(start.Art.FrameCount(n) > 0, n);
    }

    public void Dispose()
    {
        try { Directory.Delete(_dir, true); } catch (IOException) { }
    }
}
