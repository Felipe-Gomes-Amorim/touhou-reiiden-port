using Th01.Core.Audio;
using Th01.Core.Game;
using Th01.Core.Input;
using Th01.Core.Rng;
using Th01.Core.Sim;

namespace Th01.Core.Tests.Game;

public class KikuriTests
{
    static StageSetup JigokuBoss(int id = Kikuri.Id, int route = 1) => new()
    {
        StageId = id,
        Route = route,
        Cards = new bool[StageSetup.Rows, StageSetup.Cols],
        Obstacles = new byte[StageSetup.Rows, StageSetup.Cols],
    };

    static GameWorld World(int phase, Rank rank = Rank.Normal, uint seed = 5)
    {
        var w = new GameWorld(new GameOptions { FrameRand = seed, Stage = JigokuBoss(), Rank = rank, Lives = 3, Bombs = 1 }, NullSoundSink.Instance);
        K(w).Phase = phase;
        K(w).PhaseFrame = 0;
        w.Orb.Left = 0; w.Orb.Top = 380; w.Orb.PrevLeft = 0; w.Orb.PrevTop = 380;   // far from the strip
        w.Player.Left = 304;
        w.Rng = new IRand(seed);
        return w;
    }

    static Kikuri K(GameWorld w) => (Kikuri)w.Boss!;

    static void Run(GameWorld w, int updates) { for (int i = 0; i < updates; i++) K(w).Update(); }

    static int PelletsOf(GameWorld w, PelletMotion m) =>
        Enumerable.Range(0, Pellets.Count).Count(i => w.Pellets.Moving[i] && w.Pellets.Motion[i] == m);

    static int PelletsOfGroups(GameWorld w) =>
        Enumerable.Range(0, Pellets.Count).Count(i => w.Pellets.Moving[i] && w.Pellets.FromGroup[i] != PelletGroup.None);

    static void OrbInStrip(GameWorld w) { w.Orb.Left = 300; w.Orb.Top = 100; }

    // ---- set-up ----

    [Fact]
    public void Only_stage_15_on_the_jigoku_route_gets_this_boss()
    {
        Assert.IsType<Kikuri>(World(2).Boss);
        Assert.Equal(14, K(World(2)).Hp);
        Assert.Null(new GameWorld(new GameOptions { Stage = JigokuBoss(route: 0) }, NullSoundSink.Instance).Boss);
        Assert.IsType<Mima>(new GameWorld(new GameOptions { Stage = JigokuBoss(id: 9) }, NullSoundSink.Instance).Boss);
    }

    [Fact]
    public void Entrance_is_the_barn_door_then_the_rings()
    {
        var g = new GameSession(new GameOptions { FrameRand = 3, Stage = JigokuBoss() });
        Assert.Equal(SessionPhase.BossEntrance, g.Phase);
        int ticks = 0;
        while (g.Phase == SessionPhase.BossEntrance && ticks < 5000) { g.Step(GameButtons.Shot); ticks++; }
        Assert.Equal(SessionPhase.Playing, g.Phase);
        // 225 lines x 3 frames, then the rings: the first tick runs 1 iteration, the others 2 (a delay only after odd ones)
        Assert.Equal(675 + 51, ticks);
        Assert.Equal(2, K(g.World).Phase);
        Assert.Equal(0, K(g.World).PhaseFrame);
    }

    // ---- being hit ----

    [Theory]
    [InlineData(257, 100, true)]
    [InlineData(256, 100, false)]
    [InlineData(351, 100, true)]
    [InlineData(352, 100, false)]
    [InlineData(300, 131, true)]
    [InlineData(300, 132, false)]
    [InlineData(300, 300, false)]
    public void The_orb_hits_only_inside_a_strip_at_the_top_of_the_field(int left, int top, bool hit)
    {
        var w = World(2);
        w.Orb.Left = left; w.Orb.Top = top;
        K(w).Update();
        Assert.Equal(hit ? 13 : 14, K(w).Hp);
        if (hit) Assert.Equal(7000u, w.Score);
    }

    [Fact]
    public void Hit_flash_whitens_on_frames_2_and_3_of_every_4()
    {
        var w = World(2);
        K(w).Invincible = true;
        var seen = new List<bool>();
        for (int f = 0; f < 8; f++) { K(w).InvincibilityFrame = f; seen.Add(K(w).FlashOn); }
        Assert.Equal(new[] { false, false, true, true, false, false, true, true }, seen);
    }

    // ---- phase 2 and 3 ----

    [Fact]
    public void Spiral_fires_two_opposite_pellets_from_the_disc_every_third_frame_from_frame_102()
    {
        var w = World(2);
        Run(w, 101);
        Assert.Equal(0, w.Pellets.Alive);
        Run(w, 1);                                         // 102 % 3 == 0
        Assert.Equal(2, w.Pellets.Alive);
        var idx = Enumerable.Range(0, Pellets.Count).Where(i => w.Pellets.Moving[i]).ToArray();
        Assert.Equal(410 << 4, w.Pellets.CurLeft[idx[0]]);
        Assert.Equal(230 << 4, w.Pellets.CurLeft[idx[1]]);
        Assert.Equal(180 << 4, w.Pellets.CurTop[idx[0]]);
    }

    [Fact]
    public void Phase_2_ends_when_hp_drops_to_10_and_phase_3_fades_for_100_frames()
    {
        var w = World(2);
        K(w).Hp = 10;
        K(w).Update();
        Assert.Equal(3, K(w).Phase);
        Run(w, 99);
        Assert.Equal(3, K(w).Phase);
        Run(w, 1);
        Assert.Equal(4, K(w).Phase);
        Assert.Equal(0, K(w).PhaseFrame);

        var v = World(2);
        K(v).Invincible = true;                       // the phase change waits for the invincibility to end
        K(v).Hp = 10;
        K(v).Update();
        Assert.Equal(2, K(v).Phase);
    }

    // ---- phase 4 ----

    [Fact]
    public void Spinning_rings_are_eight_spin_pellets_every_60_frames()
    {
        var w = World(4);
        Run(w, 59);
        Assert.Equal(0, w.Pellets.Alive);
        Run(w, 1);
        Assert.Equal(8, PelletsOf(w, PelletMotion.Spin));
        w.Pellets.Update();
        for (int i = 0; i < Pellets.Count; i++)
        {
            if (!w.Pellets.Moving[i]) continue;
            Assert.Equal((0, 0), (w.Pellets.VelX[i], w.Pellets.VelY[i]));
            Assert.InRange((w.Pellets.CurLeft[i] >> 4) - 312, -17, 17);   // on a circle of 16 px around the moving centre
        }
    }

    [Fact]
    public void Souls_come_out_of_the_light_ball_at_frame_200_and_rise_in_mirror_image()
    {
        var w = World(4);
        Run(w, 200);
        var k = K(w);
        Assert.Equal((304, 208), (k.Souls[0].Left, k.Souls[0].Top));
        Assert.Equal((304, 208), (k.Souls[1].Left, k.Souls[1].Top));
        int guard = 0;
        while (k.PatternCur == Kikuri.P4SoulActivation && guard++ < 1000) k.Update();
        Assert.Equal(Kikuri.P4Pattern, k.PatternCur);
        Assert.True(k.Souls[0].Top < 96);
        Assert.Equal(608, k.Souls[0].Left + k.Souls[1].Left);
        Assert.True(k.Souls[0].Left < k.Souls[1].Left);
    }

    [Theory]
    [InlineData(Rank.Easy, 2 * 2)]
    [InlineData(Rank.Normal, 2 * 3)]
    [InlineData(Rank.Hard, 2 * 5)]
    [InlineData(Rank.Lunatic, 2 * 5)]
    public void Each_soul_fires_a_spread_every_80_frames(Rank rank, int expected)
    {
        var w = World(4, rank);
        var k = K(w);
        int guard = 0;
        while (k.PatternCur == Kikuri.P4SoulActivation && guard++ < 1000) k.Update();
        while (k.PhaseFrame % 80 != 79) k.Update();
        w.Pellets.ResetNonClouds();
        int before = PelletsOfGroups(w);
        k.Update();
        Assert.Equal(expected, PelletsOfGroups(w) - before);
    }

    [Fact]
    public void Phase_4_ends_after_frame_600_and_leads_to_phase_5()
    {
        var w = World(4);
        var k = K(w);
        int guard = 0;
        while (k.Phase == 4 && guard++ < 2000) k.Update();
        Assert.Equal(5, k.Phase);
        Assert.Equal(601, guard);
        Assert.Equal(0, k.PhaseFrame);
    }

    // ---- phase 5 ----

    [Fact]
    public void A_tear_falls_8_px_every_other_tick_then_ripples_and_hurts_a_player_next_to_it()
    {
        var w = World(5);
        var k = K(w);
        k.Tears[0].PosSet(200, 380);
        k.TearAnimFrame[0] = 1;
        w.Player.Left = 190;
        k.Update();                                         // moves: 380 -> 388 (> 384): the ripple starts next tick
        Assert.Equal(388, k.Tears[0].Top);
        Assert.False(w.PlayerIsHit);
        k.Update();                                         // ripple frame 2: the hit test runs
        Assert.Equal(2, k.TearAnimFrame[0]);
        Assert.True(w.PlayerIsHit);
    }

    [Fact]
    public void A_ripple_lasts_75_frames_and_misses_a_far_or_invincible_player()
    {
        var w = World(5);
        var k = K(w);
        k.Tears[0].PosSet(200, 390);
        k.TearAnimFrame[0] = 1;
        w.Player.Left = 400;
        int ticks = 0;
        while (k.TearAnimFrame[0] != 0 && ticks++ < 200) k.Update();
        Assert.Equal(74, ticks);
        Assert.False(w.PlayerIsHit);
        var v = World(5);
        v.PlayerInvincible = true;
        K(v).Tears[0].PosSet(200, 390);
        K(v).TearAnimFrame[0] = 1;
        v.Player.Left = 190;
        Run(v, 10);
        Assert.False(v.PlayerIsHit);
    }

    [Fact]
    public void Two_eye_lasers_cross_towards_the_player_every_200_frames_from_frame_400()
    {
        var w = World(5);
        Run(w, 399);
        Assert.False(w.Lasers.Slots[0].Alive);
        Run(w, 1);
        Assert.True(w.Lasers.Slots[0].Alive && w.Lasers.Slots[1].Alive);
        Assert.Equal(50 / 8, w.Lasers.Slots[0].RayExtendSpeed);
        Assert.True(w.Lasers.Slots[0].StepX > 0);          // from the left eye towards player_left + 60
        Assert.True(w.Lasers.Slots[1].StepX < 0);
    }

    [Fact]
    public void Phase_5_ends_on_hp_6_or_after_frame_1600()
    {
        var w = World(5);
        K(w).Hp = 6;
        K(w).Update();
        Assert.Equal(6, K(w).Phase);
        var v = World(5);
        K(v).PhaseFrame = 1600;
        K(v).Update();
        Assert.Equal(6, K(v).Phase);
    }

    // ---- phase 6 ----

    [Fact]
    public void Phase_6_runs_its_four_patterns_in_a_loop()
    {
        var w = World(6);
        var k = K(w);
        Run(w, 103);
        Assert.Equal(0, w.Pellets.Alive);                            // nothing before frame 104
        Run(w, 1);                                                   // 104: 4 pellets x 2 spots
        Assert.Equal(8, w.Pellets.Alive);
        int guard = 0;
        while (k.PatternCur == 0 && guard++ < 2000) k.Update();
        Assert.Equal(1, k.PatternCur);                               // the spiral hands over to the eye lasers
        var seen = new List<int> { 1 };
        guard = 0;
        while (guard++ < 4000 && seen.Count < 4)
        {
            k.Update();
            if (k.PatternCur != seen[^1]) seen.Add(k.PatternCur);
        }
        Assert.Equal(new[] { 1, 2, 3, 0 }, seen);
    }

    [Fact]
    public void Single_eye_lasers_fire_one_every_30_frames_from_250()
    {
        var w = World(6);
        var k = K(w);
        int guard = 0;
        while (k.PatternCur == 0 && guard++ < 2000) k.Update();
        Assert.Equal(1, k.PatternCur);
        Run(w, 249);
        Assert.Equal(0, w.Lasers.Slots.Count(l => l.Alive));
        Run(w, 1);
        Assert.Equal(1, w.Lasers.Slots.Count(l => l.Alive));
        Run(w, 150);
        Assert.Equal(6, w.Lasers.Slots.Count(l => l.Alive));
    }

    [Fact]
    public void Vertical_lasers_come_down_from_the_top_ten_columns_apart()
    {
        var w = World(6);
        var k = K(w);
        int guard = 0;
        while (k.PatternCur != 3 && guard++ < 6000) k.Update();
        Assert.Equal(3, k.PatternCur);
        w.Lasers.ResetAll();                                     // the eye lasers of the previous pattern are still alive here
        Run(w, 100);                                                 // frame 100: the first laser
        Assert.True(w.Lasers.Slots[0].Alive);
        Assert.Equal(0, w.Lasers.Slots[0].StepX);                   // straight down
        Assert.Equal(256, w.Lasers.Slots[0].StepY);
        Run(w, 90);
        Assert.True(w.Lasers.Slots.All(l => l.Alive));
        Assert.Equal(0, k.PatternCur);                               // hands back to the disc spiral
    }

    [Fact]
    public void After_29_patterns_the_next_hit_ends_the_fight()
    {
        var w = World(6);
        var k = K(w);
        int guard = 0;
        while (k.Hp > 1 && guard++ < 300000) { k.Update(); w.Pellets.ResetNonClouds(); }
        Assert.Equal(1, k.Hp);                                       // the timeout set it
        Assert.True(guard > 29 * 300);
        OrbInStrip(w);
        k.Update();
        Assert.True(k.Defeated);
    }

    [Fact]
    public void Defeat_takes_the_first_four_lasers_with_it()
    {
        var w = World(6);
        var k = K(w);
        k.Hp = 1;
        w.Lasers.Slots[2].Spawn(320, 300, 320, 400, 48, 10, 20, 4);
        w.Lasers.Slots[6].Spawn(320, 300, 320, 400, 48, 10, 20, 4);
        OrbInStrip(w);
        k.Update();
        Assert.True(k.Defeated);
        Assert.False(w.Lasers.Slots[2].Alive);
        Assert.True(w.Lasers.Slots[6].Alive);                       // only the first 4 are reset, as in the original
    }

    [Fact]
    public void Defeat_leads_straight_to_the_stage_bonus()
    {
        var g = new GameSession(new GameOptions { FrameRand = 12, Stage = JigokuBoss(), Lives = 3, Bombs = 1, Rank = Rank.Hard });
        int ticks = 0;
        while (g.Phase == SessionPhase.BossEntrance && ticks++ < 5000) g.Step(GameButtons.None);
        Assert.Equal(SessionPhase.Playing, g.Phase);
        var b = K(g.World);
        b.Phase = 6; b.Hp = 1; b.Invincible = false;
        g.World.Orb.Left = 300; g.World.Orb.Top = 100; g.World.Orb.Y.Force = 0; g.World.Orb.ForceFrame = 0;
        g.World.Orb.PrevLeft = g.World.Orb.Left; g.World.Orb.PrevTop = g.World.Orb.Top;
        g.Step(GameButtons.None);
        Assert.True(b.Defeated);
        g.Step(GameButtons.None);
        Assert.Equal(SessionPhase.BossDefeat, g.Phase);
        ticks = 0;
        while (g.Phase == SessionPhase.BossDefeat && ticks++ < 2000) g.Step(GameButtons.None);
        Assert.Equal(SessionPhase.StageBonus, g.Phase);
        Assert.Equal(StageBonus.ComputeTotle(15, g.World.Timer.Value, g.World.CardComboMax, g.World.RemLives, g.World.RemBombs), g.Bonus!.Value);
        StageTests.Dismiss(g);
        Assert.Equal(SessionOutcome.Cleared, g.Outcome);
    }

    // ---- determinism ----

    static (ulong hash, int maxPhase, int maxAlive) Fight(uint seed, Rank rank)
    {
        var g = new GameSession(new GameOptions { FrameRand = seed, Stage = JigokuBoss(), Rank = rank, Lives = 1000, Bombs = 2 });
        uint x = 0xBADC0FFE;
        GameButtons cur = GameButtons.None;
        int maxPhase = 0, maxAlive = 0;
        for (int i = 0; i < 12000; i++)
        {
            x ^= x << 13; x ^= x >> 17; x ^= x << 5;
            if (i % 6 == 0)
            {
                cur = GameButtons.None;
                if ((x & 1) != 0) cur |= GameButtons.Left;
                if ((x & 2) != 0) cur |= GameButtons.Right;
                if ((x & 4) != 0) cur |= GameButtons.Shot;
            }
            if (i % 150 == 149 && g.Phase == SessionPhase.Playing && g.World.Boss is Kikuri { Phase: 2 or 4 or 5 or 6 })
            {
                g.World.Orb.Left = 300; g.World.Orb.Top = 100;
            }
            g.Step(cur);
            if (g.World.Boss is Kikuri kk) maxPhase = Math.Max(maxPhase, kk.Phase);
            maxAlive = Math.Max(maxAlive, g.World.Pellets.Alive);
        }
        return (g.StateHash(), maxPhase, maxAlive);
    }

    [Theory]
    [InlineData(Rank.Normal)]
    [InlineData(Rank.Lunatic)]
    public void A_full_fight_is_deterministic(Rank rank)
    {
        var a = Fight(31, rank);
        var b = Fight(31, rank);
        Assert.Equal(a, b);
        Assert.True(a.maxPhase >= 2);
        Assert.True(a.maxAlive > 5);
        Assert.NotEqual(a.hash, Fight(32, rank).hash);
    }
}
