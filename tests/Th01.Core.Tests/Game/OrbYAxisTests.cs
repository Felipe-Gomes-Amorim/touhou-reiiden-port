using Th01.Core.Game;

namespace Th01.Core.Tests.Game;

/// <summary>Input/output vectors for the isolated floating-point helper, including truncation boundaries.</summary>
public class OrbYAxisTests
{
    // (force, forceFrame, expected pixel delta, expected stored (clamped) velocity)
    [Theory]
    [InlineData(-8.0, 0, -8, -8.0)]
    [InlineData(-8.0, 4, -8, -8.0)]       // 4/5 == 0
    [InlineData(-8.0, 5, -7, -7.0)]       // integer division steps at 5
    [InlineData(-7.8, 0, -7, -7.8)]       // truncates toward zero, not floor
    [InlineData(-0.5, 0, 0, -0.5)]
    [InlineData(0.99, 4, 0, 0.99)]
    [InlineData(0.99, 5, 1, 1.99)]
    [InlineData(-0.99, 5, 0, 0.01)]       // 1 + -0.99 -> 0.01 -> 0
    [InlineData(-1.0, 5, 0, 0.0)]         // exact integer result
    [InlineData(-1.5, 5, 0, -0.5)]
    [InlineData(-1.5, 0, -1, -1.5)]
    [InlineData(15.999999999999998, 0, 15, 15.999999999999998)] // largest double below 16
    [InlineData(16.0, 0, 16, 16.0)]
    [InlineData(16.5, 0, 16, 16.0)]       // clamped copy, movement keeps the raw value
    [InlineData(30.0, 0, 30, 16.0)]
    [InlineData(-30.9, 0, -30, -16.0)]
    [InlineData(-16.0, 0, -16, -16.0)]
    [InlineData(-10.0, 100, 10, 10.0)]    // 100/5 == 20
    public void Update_vectors(double force, int forceFrame, int expectedDelta, double expectedStored)
    {
        var y = new OrbYAxis { Force = force };
        Assert.Equal(expectedDelta, y.Update(forceFrame));
        Assert.Equal(expectedStored, y.VelocityY, 12);
    }

    [Fact]
    public void Surface_bounce_vectors()
    {
        var y = new OrbYAxis { VelocityY = 10.0 };
        y.BounceFromSurface();
        Assert.Equal(-7.8, y.Force, 12);
        Assert.Equal(-7, (int)y.Force);
        y.VelocityY = -16.0;
        y.BounceFromSurface();
        Assert.Equal(12.48, y.Force, 12);
        y.VelocityY = 0.0;
        y.BounceFromSurface();
        Assert.Equal(0.0, y.Force);
    }

    [Theory]
    [InlineData(-8.0, 0, 8.0)]
    [InlineData(-8.0, 3, 8.0)]   // 3/4 == 0
    [InlineData(-8.0, 4, 7.0)]
    [InlineData(-3.5, 9, 1.5)]
    public void Top_bounce_vectors(double velocity, int forceFrame, double expected)
    {
        var y = new OrbYAxis { VelocityY = velocity };
        y.BounceFromTop(forceFrame);
        Assert.Equal(expected, y.Force);
    }

    [Fact]
    public void Shot_and_immediate_and_reflect_vectors()
    {
        var y = new OrbYAxis { VelocityY = 5.0 };
        y.Shot();
        Assert.Equal(-7.5, y.Force);
        y.VelocityY = -16.0;
        y.Shot();
        Assert.Equal(-18.0, y.Force);   // can exceed the clamp; only VelocityY is clamped
        y.Immediate(-13);
        Assert.Equal(-13.0, y.Force);
        y.VelocityY = 6.25;
        y.Reflect();
        Assert.Equal(-6.25, y.Force);
    }

    [Fact]
    public void Chained_bounces_are_reproducible()
    {
        // Same sequence twice must be bit-identical; first values checked by hand: 12 * 0.78 = 9.36.
        double Run()
        {
            var y = new OrbYAxis { VelocityY = 12.0 };
            for (int i = 0; i < 20; i++)
            {
                y.BounceFromSurface();
                y.Update(i % 7);
            }
            return y.Force;
        }
        Assert.Equal(BitConverter.DoubleToInt64Bits(Run()), BitConverter.DoubleToInt64Bits(Run()));
        var z = new OrbYAxis { VelocityY = 12.0 };
        z.BounceFromSurface();
        Assert.Equal(-9.36, z.Force, 12);
    }
}
