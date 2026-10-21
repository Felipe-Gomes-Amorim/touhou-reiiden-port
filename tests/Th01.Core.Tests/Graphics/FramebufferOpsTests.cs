using Th01.Core.Graphics;

namespace Th01.Core.Tests.Graphics;

public class FramebufferOpsTests
{
    [Fact]
    public void ScrollY_ShiftsDisplayWithWraparound()
    {
        var fb = new Framebuffer();
        fb.SetPalette4(0, 0, 0, 0);
        fb.SetPalette4(1, 15, 0, 0);
        fb.SetPixel(0, 0, 1);
        fb.ScrollY = 10;
        var rgba = new uint[Framebuffer.Width * Framebuffer.Height];
        fb.ToRgba(rgba);
        // VRAM row 0 is shown on display row 390 (400 - 10).
        Assert.Equal(0xFFFF0000u, rgba[390 * Framebuffer.Width]);
        Assert.Equal(0xFF000000u, rgba[0]);
        fb.ScrollY = -10;
        Assert.Equal(390, fb.ScrollY);
    }

    [Fact]
    public void CopyRectEgc16_RoundsToSixteenDotWords()
    {
        var src = new Framebuffer();
        src.Clear(5);
        var dst = new Framebuffer();
        dst.CopyRectEgc16(src, 20, 3, 4, 2); // x=20..23 lies in word 16..31
        Assert.Equal(5, dst.GetPixel(16, 3));
        Assert.Equal(5, dst.GetPixel(31, 4));
        Assert.Equal(0, dst.GetPixel(15, 3));
        Assert.Equal(0, dst.GetPixel(32, 3));
        Assert.Equal(0, dst.GetPixel(20, 5));
    }

    [Fact]
    public void CopyRect_IsExactAndClipped()
    {
        var src = new Framebuffer();
        src.Clear(7);
        var dst = new Framebuffer();
        dst.CopyRect(src, -5, 398, 10, 10);
        Assert.Equal(7, dst.GetPixel(0, 399));
        Assert.Equal(7, dst.GetPixel(4, 398));
        Assert.Equal(0, dst.GetPixel(5, 398));
    }

    [Fact]
    public void BlitColorKey_SkipsTransparentIndex()
    {
        var fb = new Framebuffer();
        fb.Clear(3);
        fb.BlitColorKey([1, 15, 15, 2], 2, 2, 10, 10);
        Assert.Equal(1, fb.GetPixel(10, 10));
        Assert.Equal(3, fb.GetPixel(11, 10));
        Assert.Equal(3, fb.GetPixel(10, 11));
        Assert.Equal(2, fb.GetPixel(11, 11));
    }

    [Fact]
    public void Blit_WithAlpha_AndClipping()
    {
        var fb = new Framebuffer();
        fb.Blit([4, 5, 6, 7], 2, 2, 639, 399, [1, 1, 1, 0]);
        Assert.Equal(4, fb.GetPixel(639, 399));
        fb.Blit([9, 9], 2, 1, -1, 0, [0, 1]);
        Assert.Equal(9, fb.GetPixel(0, 0));
    }

    [Fact]
    public void FillRectPlanes_OnlyTouchesMaskedPlanes()
    {
        var fb = new Framebuffer();
        fb.Clear(0b0101);
        fb.FillRectPlanes(0, 0, 2, 1, 0b1111, 0b1010);
        Assert.Equal(0b1111, fb.GetPixel(0, 0));
        fb.FillRectPlanes(0, 1, 1, 1, 0b0000, 0b0001);
        Assert.Equal(0b0100, fb.GetPixel(0, 1));
        Assert.Equal(0b0101, fb.GetPixel(0, 2));
    }

    [Fact]
    public void TileRect_RepeatsEveryEightDots()
    {
        var fb = new Framebuffer();
        fb.TileRect(3, 0, 16, 2, [1, 2, 3, 4, 5, 6, 7, 8]);
        Assert.Equal(4, fb.GetPixel(3, 0));   // x=3 -> index 3
        Assert.Equal(1, fb.GetPixel(8, 1));   // x=8 -> index 0
        Assert.Equal(0, fb.GetPixel(2, 0));
        Assert.Equal(0, fb.GetPixel(19, 0));
    }

    [Fact]
    public void InvertRect_IsInvolution()
    {
        var fb = new Framebuffer();
        fb.SetPixel(5, 5, 3);
        fb.InvertRect(4, 4, 3, 3);
        Assert.Equal(12, fb.GetPixel(5, 5));
        Assert.Equal(15, fb.GetPixel(4, 4));
        fb.InvertRect(4, 4, 3, 3);
        Assert.Equal(3, fb.GetPixel(5, 5));
    }

    [Fact]
    public void VramPages_RestoreBackground()
    {
        var pages = new VramPages();
        pages.Page1.Clear(2);
        pages.Page0.FillRect(0, 0, 640, 400, 9);
        pages.Restore(16, 0, 16, 1);
        Assert.Equal(2, pages.Page0.GetPixel(20, 0));
        Assert.Equal(9, pages.Page0.GetPixel(40, 0));
        pages.RestoreAll();
        Assert.Equal(2, pages.Page0.GetPixel(40, 0));
    }
}
