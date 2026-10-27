using Th01.Platform.MonoGame;
using Th01.Platform.MonoGame.Launcher;

namespace Th01.Platform.Tests;

public class ImageFilterTests
{
    [Fact]
    public void FilterNumbers_MatchTheSettingsAndTheRows()
    {
        Assert.Equal(0, (int)ImageFilter.Sharp);
        Assert.Equal(1, (int)ImageFilter.Smooth);
        Assert.Equal(2, (int)ImageFilter.Scanlines);
        var m = new LauncherModel(new LauncherSettings(), new LauncherGameInfo("C:/g", true, GameVersion.Japanese), "0.4.0");
        m.Press(LauncherKey.Down); m.Press(LauncherKey.Select);
        m.Press(LauncherKey.TabNext);
        m.Press(LauncherKey.Down); m.Press(LauncherKey.Down); m.Press(LauncherKey.Down);
        Assert.Equal(RowId.Filter, m.FocusedRow.Id);
        var seen = new List<ImageFilter>();
        for (int i = 0; i < 3; i++) { seen.Add((ImageFilter)m.Settings.Filter); m.Press(LauncherKey.Select); }
        Assert.Equal([ImageFilter.Sharp, ImageFilter.Smooth, ImageFilter.Scanlines], seen);
        Assert.Equal(ImageFilter.Sharp, (ImageFilter)m.Settings.Filter);   // wrapped
    }

    [Theory]
    [InlineData(0.75)] [InlineData(1.0)] [InlineData(1.5)] [InlineData(2.0)] [InlineData(3.0)] [InlineData(4.0)]
    public void Sharp_IsNearestNeighbourAtEveryScale(double scale)
    {
        var p = FilterPlan.Choose(ImageFilter.Sharp, scale);
        Assert.False(p.Linear);
        Assert.False(p.TwoTap);
        Assert.False(p.Scanlines);
    }

    [Theory]
    [InlineData(0.75)] [InlineData(1.0)] [InlineData(1.5)] [InlineData(2.0)] [InlineData(3.0)] [InlineData(4.0)]
    public void Smooth_IsVisiblySoftAtEveryScale_EvenWholeNumbers(double scale)
    {
        var p = FilterPlan.Choose(ImageFilter.Smooth, scale);
        Assert.True(p.Linear);
        Assert.True(p.TwoTap, "plain bilinear barely changes whole-number scales: the blend must always be there");
        Assert.True(p.TapShift >= 1);
        Assert.False(p.Scanlines);
    }

    [Fact]
    public void Smooth_ShiftGrowsWithTheScale_ButStaysSmall()
    {
        int last = 0;
        foreach (double s in new[] { 1.0, 1.5, 2.0, 3.0, 4.0, 5.0 })
        {
            int shift = FilterPlan.Choose(ImageFilter.Smooth, s).TapShift;
            Assert.True(shift >= last);
            Assert.True(shift <= Math.Ceiling(s * 0.5) + 1, "about half a picture pixel: soft, not a blur");
            last = shift;
        }
    }

    [Theory]
    [InlineData(1.0)] [InlineData(2.0)] [InlineData(2.7)] [InlineData(4.0)]
    public void Scanlines_AreHardPixelsPlusTheOverlay(double scale)
    {
        var p = FilterPlan.Choose(ImageFilter.Scanlines, scale);
        Assert.False(p.Linear);
        Assert.False(p.TwoTap);
        Assert.True(p.Scanlines);
    }

    [Theory]
    [InlineData(400, 2)] [InlineData(400, 3)] [InlineData(400, 4)] [InlineData(400, 6)] [InlineData(720, 2)] [InlineData(720, 3)]
    public void OnWholeNumberScales_EachPictureLineGetsOneBandAtItsBottom(int lines, int scale)
    {
        var rows = Scanlines.Rows(lines, lines * scale);
        Assert.Equal(lines, rows.Count);
        int band = Math.Max(1, (int)Math.Round(scale * 0.34));
        for (int line = 0; line < lines; line++)
        {
            Assert.Equal(band, rows[line].Height);
            Assert.Equal(line * scale + scale - band, rows[line].Offset);        // the last rows of the line
            Assert.True(rows[line].Height < scale);                              // a clear row is always left in the line
        }
    }

    [Theory]
    [InlineData(400, 1000)]   // 2.5x
    [InlineData(400, 1100)]   // 2.75x
    [InlineData(720, 1500)]   // launcher at 2.08x
    [InlineData(400, 1234)]
    public void OnOtherScales_StillOneBandPerLine_InsideTheBox_NeverTouching(int lines, int height)
    {
        var rows = Scanlines.Rows(lines, height);
        Assert.Equal(lines, rows.Count);
        int prevEnd = -1;
        for (int i = 0; i < rows.Count; i++)
        {
            var (offset, h) = rows[i];
            Assert.InRange(offset, 0, height - 1);
            Assert.InRange(offset + h, 1, height);
            Assert.True(offset >= prevEnd, "bands do not overlap");
            Assert.Equal(i, (int)Math.Floor(offset * lines / (double)height));      // band i belongs to picture line i
            prevEnd = offset + h;
        }
    }

    [Theory]
    [InlineData(400, 400)]    // 1x game window
    [InlineData(400, 600)]    // 1.5x
    [InlineData(720, 720)]    // launcher at 1x
    [InlineData(720, 1080)]   // launcher full screen on 1080p (1.5x)
    public void BelowTwoRowsPerLine_EverySecondWindowRowIsDarkened(int lines, int height)
    {
        var rows = Scanlines.Rows(lines, height);
        Assert.Equal(height / 2, rows.Count);
        Assert.All(rows, r => Assert.Equal(1, r.Height));
        Assert.Equal(1, rows[0].Offset);
        Assert.Equal(3, rows[1].Offset);
        Assert.True(rows[^1].Offset < height);
    }

    [Fact]
    public void ScanlineRows_AreEmptyForEmptyInput_AndTheAlphaIsNoticeableButNotABlackBar()
    {
        Assert.Empty(Scanlines.Rows(0, 100));
        Assert.Empty(Scanlines.Rows(400, 0));
        Assert.InRange(Scanlines.Alpha, 60, 160);
    }
}
