using Th01.Core.Graphics;

namespace Th01.Core.Tests.Graphics;

public class PaletteFaderTests
{
    static Framebuffer WithPalette()
    {
        var fb = new Framebuffer();
        for (int i = 0; i < 16; i++) fb.SetPalette4(i, i, 15 - i, 8);
        return fb;
    }

    [Fact]
    public void FadeFromBlack_ReachesOriginalPaletteInAtMost16Steps()
    {
        var fb = WithPalette();
        var f = new PaletteFader();
        f.FadeFrom(fb, 0, 0, 0);
        fb.GetPalette4(15, out int r, out _, out _);
        Assert.Equal(0, r);
        int steps = 0;
        while (f.Step()) steps++;
        Assert.InRange(steps, 1, 16);
        for (int i = 0; i < 16; i++)
        {
            fb.GetPalette4(i, out r, out int g, out int b);
            Assert.Equal((i, 15 - i, 8), (r, g, b));
        }
    }

    [Fact]
    public void FadeToWhite_RespectsKeepMask()
    {
        var fb = WithPalette();
        var f = new PaletteFader();
        f.FadeTo(fb, 15, 15, 15, keepMask: 1 << 3);
        while (f.Step()) { }
        fb.GetPalette4(0, out int r, out int g, out int b);
        Assert.Equal((15, 15, 15), (r, g, b));
        fb.GetPalette4(3, out r, out g, out b);
        Assert.Equal((3, 12, 8), (r, g, b));
    }

    [Fact]
    public void Finish_JumpsToTarget()
    {
        var fb = WithPalette();
        var f = new PaletteFader();
        f.FadeTo(fb, 0, 0, 0);
        f.Finish();
        Assert.False(f.Active);
        fb.GetPalette4(7, out int r, out int g, out int b);
        Assert.Equal((0, 0, 0), (r, g, b));
    }
}
