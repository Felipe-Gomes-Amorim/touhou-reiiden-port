using Th01.Core.Input;
using Th01.Core.Rng;
using Th01.Core.Sim;
using Th01.Core.Timing;

namespace Th01.Core.Tests;

public class DeterminismTests
{
    static readonly GameButtons[] Script =
    [
        GameButtons.None, GameButtons.Left, GameButtons.Left | GameButtons.Shot,
        GameButtons.Up, GameButtons.Shot, GameButtons.Right | GameButtons.Down, GameButtons.Shot,
    ];

    static ulong Run(uint seed, int ticks)
    {
        var s = new DummyState(seed);
        for (int i = 0; i < ticks; i++) s.Step(Script[i % Script.Length]);
        return s.Hash();
    }

    [Fact]
    public void SameInputsSameSeed_SameHash() => Assert.Equal(Run(1234, 5000), Run(1234, 5000));

    [Fact]
    public void DifferentSeed_DifferentHash() => Assert.NotEqual(Run(1, 500), Run(2, 500));

    [Fact]
    public void DifferentInputs_DifferentHash()
    {
        var a = new DummyState(7);
        var b = new DummyState(7);
        a.Step(GameButtons.Left);
        b.Step(GameButtons.Right);
        Assert.NotEqual(a.Hash(), b.Hash());
    }

    [Fact]
    public void IRand_FirstValuesFromZeroSeed()
    {
        // seed' = seed * 0x015A4E35 + 1; result = (seed' >> 16) & 0x7FFF
        var r = new IRand(0);
        Assert.Equal(0, r.Next());       // seed 1 -> 0x0000
        Assert.Equal(0x015A, r.Next());  // seed 0x015A4E36 -> 0x015A
    }

    [Fact]
    public void IRand_StaysInRange()
    {
        var r = new IRand(0xDEADBEEF);
        for (int i = 0; i < 100_000; i++) Assert.InRange(r.Next(), 0, 32767);
    }

    [Fact]
    public void FixedTimestep_DoesNotDrift()
    {
        var t = new FixedTimestep(tickMicroseconds: 17_723);
        int total = 0;
        for (int i = 0; i < 1000; i++) total += t.Advance(1000);   // 1 s in 1 ms steps
        Assert.Equal(1_000_000 / 17_723, total);
    }
}
