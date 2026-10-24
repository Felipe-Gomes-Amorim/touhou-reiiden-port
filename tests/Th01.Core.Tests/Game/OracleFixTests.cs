using Th01.Core.Game;
using Th01.Core.Input;

namespace Th01.Core.Tests.Game;

/// <summary>Divergences found by comparing against the original (oracle traces).</summary>
public class OracleFixTests
{
    [Fact]
    public void Bomb_frame_is_200_after_a_hit_and_keeps_counting_from_there()
    {
        var g = new GameSession(new GameOptions { FrameRand = 3, Lives = 3 });
        for (int i = 0; i < 10; i++) g.Step(GameButtons.Shot);     // start playing
        g.Step(GameButtons.None);
        Assert.Equal(SessionPhase.Playing, g.Phase);
        Assert.NotEqual(200u, g.World.BombFrame);
        g.World.PlayerIsHit = true;
        g.Step(GameButtons.None);
        Assert.Equal(SessionPhase.MissAnimation, g.Phase);
        Assert.Equal(200u, g.World.BombFrame);
        int guard = 0;
        while (g.Phase == SessionPhase.MissAnimation && guard++ < 500) g.Step(GameButtons.None);
        Assert.Equal(SessionPhase.Playing, g.Phase);
        Assert.Equal(200u, g.World.BombFrame);                     // frozen during the animation, as the first trace row shows
        g.Step(GameButtons.None);
        Assert.Equal(201u, g.World.BombFrame);
    }

    [Fact]
    public void Bomb_frame_is_200_when_the_stage_is_cleared_too()
    {
        var g = new GameSession(new GameOptions { FrameRand = 3, Lives = 3 });
        for (int i = 0; i < 10; i++) g.Step(GameButtons.Shot);
        g.Step(GameButtons.None);
        g.World.StageCleared = true;
        g.Step(GameButtons.None);
        Assert.Equal(SessionPhase.StageBonus, g.Phase);
        Assert.Equal(200u, g.World.BombFrame);
    }
}
