using Th01.Assets.Model;

namespace Th01.Assets.Tests.Model;

public class ModelTests
{
    [Fact]
    public void Palette_ExpandsTo888()
    {
        var p = new Palette4();
        p.Set(3, 15, 8, 0);
        Assert.Equal(((byte)255, (byte)136, (byte)0), p.ToRgb888(3));
        Assert.Throws<ArgumentOutOfRangeException>(() => p.Set(0, 16, 0, 0));
        Assert.Throws<ArgumentException>(() => new Palette4(new byte[10]));
    }

    [Fact]
    public void Image_ValidatesSizes()
    {
        Assert.Throws<ArgumentException>(() => new IndexedImage(2, 2, new byte[3]));
        Assert.Throws<ArgumentException>(() => new IndexedImageA(2, 2, new byte[4], new byte[3]));
        var img = new IndexedImageA(2, 2, new byte[4], new byte[4]);
        Assert.Equal(4, img.Pixels.Length);
    }

    [Fact]
    public void SpriteSheet_RequiresSameSizeFrames()
    {
        var a = new IndexedImageA(2, 2, new byte[4], new byte[4]);
        var b = new IndexedImageA(3, 2, new byte[6], new byte[6]);
        Assert.Throws<ArgumentException>(() => new SpriteSheet([a, b]));
        Assert.Equal(2, new SpriteSheet([a, a]).Frames.Count);
    }

    [Fact]
    public void StageLayout_IsTenByTwenty()
    {
        var s = new StageLayout();
        Assert.Equal(10, s.Cards.GetLength(0));
        Assert.Equal(20, s.Obstacles.GetLength(1));
        Assert.Throws<ArgumentException>(() => new StageLayout(new bool[2, 2], new byte[10, 20]));
    }
}
