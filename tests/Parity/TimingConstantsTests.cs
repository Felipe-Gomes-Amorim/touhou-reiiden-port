using Th01.Core.Game;

namespace Th01.Parity.Tests;

/// <summary>
/// Pins the GameSession timing constants whose value has evidence (see STATUS, T5 round 3).
/// Oracle: every miss shows a 83-vsync gap between two polled ticks (82 blank vsyncs) in the original.
/// Source (ReC98 th01/main/stage/timer.cpp, boss/defeat.cpp): HARRY UP = 2 x frame_delay(27), route menu frame_delay(50).
/// Not pinned (no evidence yet): EntranceTicks, BossDefeatFadeTicks (proposal 239, see STATUS).
/// </summary>
public class TimingConstantsTests
{
    [Fact] public void MissAnimation_Is82Ticks_AsMeasuredInTheOriginal() => Assert.Equal(82, GameSession.MissTotalTicks);
    [Fact] public void HarryUpBanner_Is2xFrameDelay27() => Assert.Equal(2 * 27, GameSession.HarryUpBannerTicks);
    [Fact] public void RouteSelectPause_IsFrameDelay50() => Assert.Equal(50, GameSession.RouteSelectDelayTicks);
}
