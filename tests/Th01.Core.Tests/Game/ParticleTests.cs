using Th01.Core.Audio;
using Th01.Core.Game;
using Th01.Core.Rng;

namespace Th01.Core.Tests.Game;

public class ParticleTests
{
    static (GameWorld w, ParticleField p) Make(uint seed = 11)
    {
        var w = new GameWorld(new GameOptions { FrameRand = seed }, NullSoundSink.Instance);
        w.Rng = new IRand(seed);
        w.Particles.Init();
        return (w, w.Particles);
    }

    [Fact]
    public void Init_sets_the_two_tunables_and_kills_everything()
    {
        var (_, p) = Make();
        Assert.Equal(2, p.SpawnInterval);
        Assert.Equal(10, p.VelocityBaseMax);
        for (int i = 0; i < ParticleField.Count; i++) Assert.False(p.Alive(i));
    }

    [Fact]
    public void First_lap_skips_slot_0_and_spawns_slot_1_on_the_second_frame()
    {
        var (w, p) = Make();
        p.Update(ParticleOrigin.TopRight);
        Assert.Equal(1, p.SpawnCycle);
        Assert.Equal(11u, w.Rng.Seed);                     // nothing drawn: cycle 1 is odd
        p.Update(ParticleOrigin.TopRight);
        Assert.False(p.Alive(0));
        Assert.True(p.Alive(1));
    }

    [Fact]
    public void Odd_slot_from_the_top_right_enters_on_the_right_edge_flying_left_and_down()
    {
        var (w, p) = Make();
        var probe = new IRand(11);
        int vb = probe.Next() % 10 + 1;
        int y = probe.Next() % 400;
        p.Update(ParticleOrigin.TopRight);
        p.Update(ParticleOrigin.TopRight);                 // spawns slot 1 and moves it once
        Assert.Equal(probe.Seed, w.Rng.Seed);              // two draws: speed, then the free coordinate
        Assert.Equal(vb, p.VelocityBase(1));
        Assert.Equal((-vb * 13, vb * 8), p.Velocity(1));
        Assert.Equal((639 * 16 - vb * 13, (y << 4) + vb * 8), p.RawPos(1));
    }

    [Fact]
    public void Even_slot_from_the_top_enters_at_a_random_column_on_the_top_edge()
    {
        var (w, p) = Make();
        var probe = new IRand(11);
        for (int k = 0; k < 4; k++) p.Update(ParticleOrigin.Top);   // slots 1 and 2 have spawned (cycles 2 and 4)
        // slot 1: top origin, odd -> column draw; slot 2: even -> column draw as well
        int vb1 = probe.Next() % 10 + 1; int x1 = probe.Next() % 640;
        int vb2 = probe.Next() % 10 + 1; int x2 = probe.Next() % 640;
        Assert.Equal(probe.Seed, w.Rng.Seed);
        Assert.Equal((x1 << 4, 0 + (vb1 << 4) * 3), p.RawPos(1));    // moved on cycles 2, 3, 4
        Assert.Equal((x2 << 4, vb2 << 4), p.RawPos(2));              // moved once, on cycle 4
        Assert.Equal((0, vb2 << 4), p.Velocity(2));
    }

    [Fact]
    public void Bottom_and_side_origins_set_their_edges_and_velocity_signs()
    {
        foreach (var (o, vxSign, vySign) in new[]
        {
            (ParticleOrigin.Bottom, 0, -1), (ParticleOrigin.BottomLeft, 1, -1), (ParticleOrigin.BottomRight, -1, -1),
            (ParticleOrigin.Left, 1, 0), (ParticleOrigin.Right, -1, 0), (ParticleOrigin.TopLeft, 1, 1),
        })
        {
            var (_, p) = Make();
            p.Update(o); p.Update(o);
            var (vx, vy) = p.Velocity(1);
            Assert.Equal(vxSign, Math.Sign(vx));
            Assert.Equal(vySign, Math.Sign(vy));
        }
    }

    [Fact]
    public void Particles_die_when_they_leave_the_screen_and_slots_are_reused()
    {
        var (_, p) = Make();
        p.Update(ParticleOrigin.Right); p.Update(ParticleOrigin.Right);
        Assert.True(p.Alive(1));
        int guard = 0;
        while (p.Alive(1) && guard++ < 1000) p.Update(ParticleOrigin.Right);
        Assert.False(p.Alive(1));
        Assert.True(guard < 700);                           // 640 px at >= 1 px per frame
    }

    [Fact]
    public void Spawning_draws_exactly_two_numbers_and_only_for_free_slots()
    {
        var (w, p) = Make();
        int spawned = 0;
        for (int k = 0; k < 60; k++)
        {
            uint before = w.Rng.Seed;
            int aliveBefore = Enumerable.Range(0, 40).Count(p.Alive);
            p.Update(ParticleOrigin.Top);
            int aliveAfter = Enumerable.Range(0, 40).Count(p.Alive);
            var r = new IRand(before);
            int draws = 0;
            while (r.Seed != w.Rng.Seed && draws < 10) { r.Next(); draws++; }
            Assert.True(draws is 0 or 2, $"frame {k}: {draws} draws");
            if (draws == 2) spawned++;
        }
        Assert.Equal(30, spawned);                          // cycles 2, 4, ... 60
    }

    [Fact]
    public void The_first_lap_reaches_slot_40_and_corrupts_the_neighbouring_arrays()
    {
        var (w, p) = Make();
        var probe = new IRand(11);
        for (int k = 0; k < 79; k++) p.Update(ParticleOrigin.Top);
        Assert.Equal(79, p.SpawnCycle);
        // advance the probe over the 39 spawns of slots 1..39 (2 draws each), then the 40th spawn is the odd one out
        for (int k = 0; k < 39 * 2; k++) probe.Next();
        int vb = probe.Next() % 10 + 1;
        int x = probe.Next() % 640;                          // slot 40 is even: top edge, random column
        p.Update(ParticleOrigin.Top);                        // cycle 80 -> slot 40
        Assert.Equal(probe.Seed, w.Rng.Seed);
        Assert.Equal(1, p.VelocityBase(0));                  // alive[40] is velocity_base[0]: written with 1
        Assert.Equal(vb, p.SpawnCycle);                      // velocity_base[40] is the spawn counter itself
        Assert.True(p.Alive(0));                             // velocity_y[40] (vb * 16) overwrote alive[0] / alive[1]
        Assert.False(p.Alive(1));
        Assert.Equal(x << 4, p.RawPos(0).y);                 // x[40] is y[0]
    }

    [Fact]
    public void The_stream_is_deterministic_and_depends_on_the_seed()
    {
        ulong Run(uint seed, ParticleOrigin o)
        {
            var (w, p) = Make(seed);
            for (int k = 0; k < 700; k++) p.Update(o);
            var h = new StateHasher();
            p.Hash(ref h);
            return h.Value ^ w.Rng.Seed;
        }
        Assert.Equal(Run(3, ParticleOrigin.TopRight), Run(3, ParticleOrigin.TopRight));
        Assert.NotEqual(Run(3, ParticleOrigin.TopRight), Run(4, ParticleOrigin.TopRight));
        Assert.NotEqual(Run(3, ParticleOrigin.TopRight), Run(3, ParticleOrigin.Left));
    }
}
