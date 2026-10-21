using Th01.Core.Graphics;

namespace Th01.Core.Tests.Graphics;

public class ViewportLayoutTests
{
    [Fact]
    public void Integer_UsesWholeScaleAndCentres()
    {
        Assert.Equal(new PixelRect(160, 100, 1280, 800), ViewportLayout.Compute(1600, 1000, ScaleMode.Integer));
        Assert.Equal(new PixelRect(0, 0, 640, 400), ViewportLayout.Compute(640, 400, ScaleMode.Integer));
    }

    [Fact]
    public void Integer_FallsBackToFitWhenWindowIsSmaller()
    {
        var r = ViewportLayout.Compute(320, 300, ScaleMode.Integer);
        Assert.Equal(320, r.Width);
        Assert.Equal(200, r.Height);
        Assert.Equal(50, r.Y);
    }

    [Fact]
    public void Fit_KeepsAspectRatio_Stretch_FillsWindow()
    {
        Assert.Equal(new PixelRect(0, 20, 800, 500), ViewportLayout.Compute(800, 540, ScaleMode.Fit));
        Assert.Equal(new PixelRect(0, 0, 900, 300), ViewportLayout.Compute(900, 300, ScaleMode.Stretch));
        Assert.Equal(new PixelRect(0, 0, 0, 0), ViewportLayout.Compute(0, 0, ScaleMode.Fit));
    }
}
