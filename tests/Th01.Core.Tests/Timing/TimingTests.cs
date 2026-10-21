using Th01.Core.Timing;

namespace Th01.Core.Tests.Timing;

public class TimingTests
{
    [Fact]
    public void DefaultRate_IsExact_NoDrift()
    {
        var t = new FixedTimestep(maxTicksPerUpdate: 100);
        long total = 0;
        for (int i = 0; i < 1000; i++) total += t.Advance(1000);   // 1 s in 1 ms steps
        Assert.Equal(56, total);
        for (int i = 0; i < 999_000; i++) total += t.Advance(1000); // 1000 s in total
        Assert.Equal(56_423, total);
    }

    [Fact]
    public void LongStall_DropsBacklog()
    {
        var t = new FixedTimestep();
        Assert.Equal(5, t.Advance(10_000_000));
        Assert.Equal(0, t.Advance(1));
    }

    [Fact]
    public void Pause_StopsTicksAndDiscardsTime_StepGivesOne()
    {
        var t = new FixedTimestep();
        t.Paused = true;
        Assert.Equal(0, t.Advance(1_000_000));
        t.RequestStep();
        Assert.Equal(1, t.Advance(0));
        Assert.Equal(0, t.Advance(0));
        t.Paused = false;
        Assert.Equal(0, t.Advance(1000)); // paused time was not banked
    }

    [Fact]
    public void RateCounter_ReportsLastFullSecond()
    {
        var c = new RateCounter();
        for (int i = 0; i < 100; i++) c.Add(1, 10_000);
        Assert.Equal(100, c.PerSecond);
        Assert.Equal(100, c.Total);
    }
}
