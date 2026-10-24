using Th01.Core.Audio;
using Th01.Core.Diagnostics;
using Th01.Core.Game;
using Th01.Core.Input;
using Th01.Core.Rng;

namespace Th01.Core.Tests.Game;

public class SparkleTraceTests
{
    static GameWorld World(uint seed = 462)
    {
        var w = new GameWorld(new GameOptions { FrameRand = seed }, NullSoundSink.Instance);
        w.Rng = new IRand(seed);
        w.Player.Left = 304;
        return w;
    }

    static int Draws(uint from, uint to)
    {
        var r = new IRand(from);
        for (int k = 0; k <= 1000; k++) { if (r.Seed == to) return k; r.Next(); }
        return -1;
    }

    [Fact]
    public void Sparkles_do_nothing_and_draw_nothing_while_not_invincible()
    {
        var w = World();
        w.Sparkles.Update(false);
        w.Sparkles.Update(false);
        Assert.Equal(462u, w.Rng.Seed);
        Assert.False(w.Sparkles.Active);
    }

    [Fact]
    public void First_invincible_frame_rolls_all_eight_sparkles_with_three_draws_each()
    {
        var w = World();
        w.Sparkles.Update(true);
        Assert.Equal(24, Draws(462, w.Rng.Seed));          // the 24 the original shows after a miss restart
        var probe = new IRand(462);
        for (int i = 0; i < 8; i++)
        {
            int left = probe.Next() % 48 + (304 - 8), top = probe.Next() % 48 + (368 - 16), frame = probe.Next() % 7 + 1;
            Assert.Equal(left, w.Sparkles.Left[i]);
            Assert.Equal(top, w.Sparkles.Top[i]);
            Assert.Equal(frame + 1, w.Sparkles.Frame[i]);  // born with a frame in 1..7, advanced once
        }
        Assert.True(w.Sparkles.Active);
    }

    [Fact]
    public void A_sparkle_is_rerolled_after_it_reaches_frame_9_so_draws_come_in_multiples_of_three()
    {
        var w = World();
        w.Sparkles.Update(true);
        for (int t = 0; t < 40; t++)
        {
            uint before = w.Rng.Seed;
            w.Sparkles.Update(true);
            int d = Draws(before, w.Rng.Seed);
            Assert.True(d >= 0 && d % 3 == 0, $"tick {t}: {d} draws");
        }
    }

    [Fact]
    public void Turning_invincibility_off_parks_the_sparkles_and_turning_it_on_rerolls_them()
    {
        var w = World();
        w.Sparkles.Update(true);
        w.Sparkles.Update(false);
        Assert.False(w.Sparkles.Active);
        uint before = w.Rng.Seed;
        w.Sparkles.Update(false);
        Assert.Equal(before, w.Rng.Seed);
        w.Sparkles.Update(true);
        Assert.Equal(24, Draws(before, w.Rng.Seed));
    }

    [Fact]
    public void A_session_draws_for_the_sparkles_after_a_miss_restart()
    {
        var g = new GameSession(new GameOptions { FrameRand = 462, Lives = 3 });
        g.Step(GameButtons.Shot); g.Step(GameButtons.None);
        var w = g.World;
        w.Orb.Left = w.Player.Left; w.Orb.Top = Playfield.PlayerTop - 8; w.Orb.Y.Force = 0; w.Orb.ForceFrame = 0;
        g.Step(GameButtons.None);
        while (g.Phase == SessionPhase.MissAnimation) g.Step(GameButtons.None);
        Assert.Equal(w.FrameRand, w.Rng.Seed);             // irand_init(frame_rand) at the restart
        uint start = w.Rng.Seed;
        g.Step(GameButtons.None);                          // first iteration: the sparkles roll 8 x 3 numbers
        Assert.Equal(24, Draws(start, w.Rng.Seed));
    }

    // ---- trace sampling phase ----

    [Fact]
    public void Trace_reports_frame_rand_and_seed_as_the_originals_loop_leaves_them_at_its_sample_point()
    {
        var g = new GameSession(new GameOptions { FrameRand = 7 });
        var t = (ITraceSource)g;
        Assert.Equal(7L, t.TraceValues()["frame_rand"]);
        Assert.Equal(7L, t.TraceValues()["rng_seed"]);          // the pinned seed while waiting
        g.Step(GameButtons.Shot);                               // Shot sensed: the next step starts the first iteration
        Assert.Equal(8L, t.TraceValues()["frame_rand"]);        // irand_init + frame_rand++ already visible
        Assert.Equal(7L, t.TraceValues()["rng_seed"]);
        g.Step(GameButtons.None);                               // first iteration done
        Assert.Equal(9L, t.TraceValues()["frame_rand"]);        // the second iteration's increment is the visible one
        Assert.Equal(8u, g.World.FrameRand);                    // the simulation's own counter is unchanged
    }

    [Fact]
    public void Trace_exposes_the_double_columns_as_raw_bits()
    {
        var g = new GameSession(new GameOptions { FrameRand = 1 });
        var v = ((ITraceSource)g).TraceValues();
        Assert.Equal(BitConverter.DoubleToInt64Bits(-8.0), v["orb_force_bits"]);
        Assert.Equal(BitConverter.DoubleToInt64Bits(0.0), v["orb_vy_bits"]);
    }
}
