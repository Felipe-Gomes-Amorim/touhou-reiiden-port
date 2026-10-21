using System.Text;
using Th01.Assets.Formats;

namespace Th01.Assets.Tests;

/// <summary>Synthetic data only; every file is built by hand in the test.</summary>
public class FormatTests
{
    private static byte[] Pal() => Enumerable.Range(0, 48).Select(i => (byte)(i % 16)).ToArray();

    [Fact]
    public void Ptn_DecodesPlanesAndAlpha()
    {
        var f = new List<byte>(Encoding.ASCII.GetBytes("HPTN")) { 1, 2 };
        f.AddRange(Pal());
        f.Add(0);
        var planes = new byte[4][];
        for (int p = 0; p < 4; p++) planes[p] = new byte[128];
        // pixel (0,0): colour 0b0101 = 5 (B and G); pixel (1,0): colour 15 (transparent)
        planes[0][0] = 0b1100_0000; planes[2][0] = 0b1100_0000;
        planes[1][0] = 0b0100_0000; planes[3][0] = 0b0100_0000;
        foreach (var p in planes) f.AddRange(p);
        f.Add(0); foreach (var p in new byte[512]) f.Add(p); // second, empty image

        var sheet = PtnSprites.Load(f.ToArray());
        Assert.Equal(2, sheet.Frames.Count);
        var img = sheet.Frames[0];
        Assert.Equal(5, img.Pixels[0]);
        Assert.Equal(1, img.Alpha[0]);
        Assert.Equal(15, img.Pixels[1]);
        Assert.Equal(0, img.Alpha[1]);
        Assert.Equal(1, img.Alpha[2]); // colour 0 is opaque
        Assert.NotNull(img.Palette);
        Assert.Throws<InvalidDataException>(() => PtnSprites.Load(f.ToArray().AsSpan(0, 100)));
    }

    [Fact]
    public void Bos_AlphaBitSetMeansTransparent()
    {
        var f = new List<byte>(Encoding.ASCII.GetBytes("BOSS")) { 1, 0, 1, 0, 1 };
        f.AddRange(new byte[7]); f.AddRange(Pal());
        f.Add(0b0111_1111); // alpha: only pixel 0 opaque
        f.Add(0b1000_0000); f.Add(0); f.Add(0); f.Add(0b1000_0000); // B, R, G, E -> colour 9 at pixel 0
        var sheet = BosSprites.Load(f.ToArray());
        var img = sheet.Frames[0];
        Assert.Equal((8, 1), (img.Width, img.Height));
        Assert.Equal(9, img.Pixels[0]);
        Assert.Equal(1, img.Alpha[0]);
        Assert.Equal(0, img.Alpha[1]);
    }

    [Fact]
    public void Grc_IsMonochromeWithAlphaEqualToBits()
    {
        var f = new List<byte>(Encoding.ASCII.GetBytes("GRCG")) { 1, 0, 2, 0, 2 };
        f.AddRange(new byte[7]); f.AddRange(Pal());
        f.AddRange(new byte[] { 0xA0, 0, 0, 0xFF }); // two images of 1x2 bytes
        var sheet = GrcSprites.Load(f.ToArray());
        Assert.Equal(2, sheet.Frames.Count);
        Assert.Equal(new byte[] { 1, 0, 1, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0 }, sheet.Frames[0].Pixels);
        Assert.Equal(sheet.Frames[1].Pixels, sheet.Frames[1].Alpha);
        Assert.Equal(1, sheet.Frames[1].Pixels[8]);
        Assert.Null(sheet.Frames[0].Palette);
    }

    private static byte[] Grx(byte[] rle, params byte[][] planars)
    {
        var f = new List<byte>(Encoding.ASCII.GetBytes("HGRX")) { (byte)planars.Length, 0, 0, 0 };
        f.AddRange(BitConverter.GetBytes((ushort)rle.Length));
        f.AddRange(BitConverter.GetBytes((ushort)planars[0].Length));
        f.AddRange(new byte[4]); f.AddRange(Pal()); f.AddRange(rle);
        foreach (var p in planars) f.AddRange(p);
        return f.ToArray();
    }

    [Fact]
    public void Grx_RunsPutAndSkip()
    {
        // unit 0: put (cmd 0). unit 1: skip (cmd 2). units 2..4: run of 2 (+1) putting.
        // then run of 31995 not expressible in one byte, so the command stream just ends.
        byte[] rle = [0, 2, 1, 2, 0];
        byte[] planar = [0x80, 0, 0, 0, /*u2*/ 0, 0x80, 0, 0, /*u3*/ 0, 0, 0x80, 0, /*u4*/ 0, 0, 0, 0x80];
        var sheet = GrzImage.LoadGrx(Grx(rle, planar));
        var img = sheet.Frames[0];
        Assert.Equal((640, 400), (img.Width, img.Height));
        Assert.Equal(1, img.Pixels[0]); Assert.Equal(1, img.Alpha[0]);
        Assert.Equal(0, img.Alpha[8]);                       // skipped unit
        Assert.Equal(2, img.Pixels[16]); Assert.Equal(1, img.Alpha[16]);
        Assert.Equal(4, img.Pixels[24]);
        Assert.Equal(8, img.Pixels[32]);
        Assert.Equal(0, img.Alpha[40]);                      // stream ended: transparent
    }

    [Fact]
    public void Grz_PicksImagesByOffset()
    {
        byte[] g1 = Grx([0], [0x80, 0, 0, 0]);
        byte[] g2 = Grx([0], [0, 0, 0, 0x80], [0x80, 0, 0, 0]);
        var f = new List<byte>(Encoding.ASCII.GetBytes("HGRZ")) { 2, 0, 0, 0 };
        int start = 4 + 4 + 64 + 4 + 20;
        var offs = new int[16]; offs[0] = start; offs[1] = start + g1.Length;
        foreach (int o in offs) f.AddRange(BitConverter.GetBytes(o));
        f.AddRange(new byte[4 + 20]); f.AddRange(g1); f.AddRange(g2);
        var list = GrzImage.LoadGrz(f.ToArray());
        Assert.Equal(2, list.Count);
        Assert.Single(list[0].Frames);
        Assert.Equal(2, list[1].Frames.Count);
        Assert.Equal(8, list[1].Frames[0].Pixels[0]);
        Assert.Equal(1, list[1].Frames[1].Pixels[0]);
    }

    [Fact]
    public void Grf_DecodesCustomRunByte()
    {
        var f = new List<byte>(Encoding.ASCII.GetBytes("HGRF")) { 0, 0xFE, 0, 0 };
        byte[] rle = [0xFE, 3, 0xFF, 0x0F, 0xFE, 0, 0x00]; // run of 3+1 FF, then 0F, then literal FE (0 runs)
        f.AddRange(new byte[8]); f.AddRange(Pal());
        f.AddRange(rle);
        var img = GrfImage.LoadFirstPlane(f.ToArray(), widthBytes: 8, height: 1);
        Assert.Equal(1, img.Pixels[31]);
        Assert.Equal(0, img.Pixels[32]); Assert.Equal(1, img.Pixels[36]);
        Assert.Equal(1, img.Pixels[40]); Assert.Equal(0, img.Pixels[47]); // 0xFE
    }

    [Fact]
    public void StageDat_ReadsCardsAndRawObstacles()
    {
        var f = new byte[22 + 250 * 2];
        Encoding.ASCII.GetBytes("STAGE").CopyTo(f, 0);
        f[22] = 0b1010; // row 0, columns 0..3: cards at 0 and 2
        f[22 + 5 * 9 + 4] = 0b0001; // row 9, column 19
        f[22 + 50 + 7] = 15; // obstacle (0,7) kept raw
        f[22 + 250 + 50] = 1;
        var list = StageDat.Load(f);
        Assert.Equal(2, list.Count);
        var s = list[0];
        Assert.True(s.Cards[0, 0]); Assert.False(s.Cards[0, 1]); Assert.True(s.Cards[0, 2]); Assert.False(s.Cards[0, 3]);
        Assert.True(s.Cards[9, 19]);
        Assert.Equal(15, s.Obstacles[0, 7]);
        Assert.Equal(1, list[1].Obstacles[0, 0]);
        Assert.Throws<InvalidDataException>(() => StageDat.Load(new byte[10]));
    }

    [Fact]
    public void ScoreDat_DecodesNamesWithAddKey()
    {
        var f = new byte[ScoreDat.Size];
        Encoding.ASCII.GetBytes("HISCORE").CopyTo(f, 0);
        for (int i = 0; i < 16; i++) f[7 + i] = (byte)(0x40 + i + ScoreDat.NameKey); // wraps past 0xFF
        BitConverter.GetBytes(123456).CopyTo(f, 7 + 160);
        BitConverter.GetBytes((short)40).CopyTo(f, 7 + 160 + 40);
        f[7 + 160 + 60] = 0x81; f[7 + 160 + 61] = 0x40;
        var t = ScoreDat.Load(f);
        Assert.Equal(Enumerable.Range(0, 16).Select(i => (byte)(0x40 + i)).ToArray(), t.NameBytes[0]);
        Assert.Equal(123456, t.Scores[0]);
        Assert.Equal(40, t.Stages[0]);
        Assert.Equal(new byte[] { 0x81, 0x40 }, t.RouteBytes[0]);
    }

    [Fact]
    public void Cfg_ReadsOptions()
    {
        byte[] f = [.. "REIIDEN"u8, 2, 1, 1, 2];
        Assert.Equal(new GameOptions(2, 1, 1, 2), ReiidenCfg.Load(f));
        Assert.Throws<InvalidDataException>(() => ReiidenCfg.Load("REIIDEX\0\0\0\0"u8));
    }
}
