using Th01.Core.Diagnostics;
using Th01.Core.Game;
using Th01.Core.Input;

namespace Th01.Core.Tests.Game;

public class TraceTests
{
    [Fact]
    public void Session_exposes_the_oracle_columns_and_does_not_mutate_state()
    {
        var s = new GameSession(new GameOptions { FrameRand = 5 });
        var t = Assert.IsAssignableFrom<ITraceSource>(s);
        s.Step(GameButtons.Left);
        ulong h = s.StateHash();
        var v = t.TraceValues();
        foreach (var k in new[] { "player_x", "player_y", "orb_x", "orb_y", "orb_vx", "orb_vy", "score", "rng_seed" })
            Assert.True(v.ContainsKey(k), k);
        Assert.Equal(Playfield.PlayerLeftStart - 4, v["player_x"]);
        Assert.Equal(Playfield.PlayerTop, v["player_y"]);
        Assert.Equal(Playfield.OrbLeftStart, v["orb_x"]);
        Assert.Equal(-4, v["orb_vx"]);           // starts moving left
        Assert.Equal(h, s.StateHash());
    }

    [Fact]
    public void Fixed_point_columns_truncate_toward_zero()
    {
        var y = new OrbYAxis { Force = -7.8, VelocityY = -7.8 };
        Assert.Equal(-1996, y.VelocityYQ8);   // -7.8*256 = -1996.8
        Assert.Equal(-1996, y.ForceQ8);
    }
}
