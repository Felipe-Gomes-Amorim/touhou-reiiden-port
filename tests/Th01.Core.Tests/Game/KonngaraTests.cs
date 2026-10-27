using Th01.Core.Audio;
using Th01.Core.Game;
using Th01.Core.Input;
using Th01.Core.Rng;
using Th01.Core.Sim;

namespace Th01.Core.Tests.Game;

/// <summary>Rules: docs/GAMEPLAY-NOTES.md section 32 (ref: th01/main/boss/b20j.cpp). Hand-derived numbers come from that file.</summary>
public class KonngaraTests
{
    static StageSetup Stage(int route = 1, int id = Konngara.Id) => new()
    {
        StageId = id,
        Route = route,
        Cards = new bool[StageSetup.Rows, StageSetup.Cols],
        Obstacles = new byte[StageSetup.Rows, StageSetup.Cols],
    };

    static GameWorld World(int phase = 1, int pattern = 99, Rank rank = Rank.Normal, uint seed = 5, bool invincible = false)
    {
        var w = new GameWorld(new GameOptions { FrameRand = seed, Stage = Stage(), Rank = rank, Lives = 3, Bombs = 1 }, NullSoundSink.Instance);
        var k = K(w);
        k.Phase = phase;
        k.PhaseFrame = 0;
        k.PatternCur = pattern;
        w.Orb.Left = 0; w.Orb.Top = 380; w.Orb.PrevLeft = 0; w.Orb.PrevTop = 380;   // far from the head
        w.Player.Left = 304;
        w.PlayerInvincible = invincible;
        w.Rng = new IRand(seed);
        return w;
    }

    static Konngara K(GameWorld w) => (Konngara)w.Boss!;
    static void Run(GameWorld w, int n) { for (int i = 0; i < n; i++) K(w).Update(); }
    /// <summary>Updates until the boss PhaseFrame equals the given frame (the frame counter is incremented first).</summary>
    static void RunTo(GameWorld w, int frame)
    {
        // bounded: patterns reset the frame to 0 when they end
        for (int guard = 0; guard < 3000 && K(w).PhaseFrame < frame; guard++)
        {
            K(w).Update();
            if (K(w).PhaseFrame == 0) return;
        }
    }

    // ---- set-up ----

    [Fact]
    public void Only_stage_20_on_the_jigoku_route_gets_this_boss()
    {
        var w = World();
        Assert.Equal(18, K(w).Hp);
        Assert.Equal((10, 16), (K(w).HpFirstRedWhite, K(w).HpFirstWhite));
        Assert.Equal(new[] { 3, 4, 5 }, K(w).FlashColors);
        Assert.IsNotType<Konngara>(new GameWorld(new GameOptions { Stage = Stage(route: 0) }, NullSoundSink.Instance).Boss);
        Assert.IsNotType<Konngara>(new GameWorld(new GameOptions { Stage = Stage(id: Kikuri.Id) }, NullSoundSink.Instance).Boss);
    }

    [Fact]
    public void The_entrance_lasts_its_computed_length_and_phase_0_only_starts_phase_1()
    {
        var w = World(phase: 0);
        var k = K(w);
        int ticks = 1;
        while (!k.EntranceFrame()) ticks++;
        Assert.Equal(Konngara.EntranceTotal, ticks);
        Assert.InRange(ticks, 300, 1500);
        w.Rng = new IRand(99);
        w.Orb.Left = 300; w.Orb.Top = 130;                            // inside the head box: phase 0 must not apply hits
        k.Update();
        Assert.Equal((1, 18, 0), (k.Phase, k.Hp, k.PhaseFrame));
        Assert.Equal(w.FrameRand, w.Rng.Seed);                        // random_seed = frame_rand
    }

    // ---- pattern choice and phases ----

    [Fact]
    public void A_pattern_is_chosen_after_120_idle_frames()
    {
        var w = World();
        var k = K(w);
        Run(w, 120);
        Assert.Equal(99, k.PatternCur);
        Run(w, 1);
        Assert.InRange(k.PatternCur, 0, 2);
        Assert.Equal(1, k.PhaseFrame);
        Assert.Equal(1, k.PatternsDoneForTests);
    }

    [Fact]
    public void Phase_1_ends_below_16_hp_when_idle_and_the_seal_phases_last_120_frames()
    {
        var w = World();
        var k = K(w);
        k.Hp = 15;
        Run(w, 1);
        Assert.Equal(2, k.Phase);
        Assert.Equal(99, k.PatternCur);
        Run(w, 49);
        Assert.False(k.SiddhamFlash);
        Run(w, 1);                                                    // frame 50: the colour lights up
        Assert.True(k.SiddhamFlash);
        Run(w, 2);                                                    // 52: first fade step
        Assert.Equal(1, k.SiddhamSteps);
        RunTo(w, 120);
        Assert.Equal(2, k.Phase);
        Run(w, 1);
        Assert.Equal((3, 0), (k.Phase, k.PhaseFrame));
    }

    [Fact]
    public void Phase_thresholds_follow_hp_and_the_count_of_patterns()
    {
        foreach (var (phase, hp, done, next) in new[] { (3, 12, 0, 4), (3, 18, 9, 4), (5, 9, 0, 6), (5, 18, 6, 6), (1, 18, 7, 2) })
        {
            var w = World(phase);
            K(w).Hp = hp;
            K(w).PatternsDoneForTests = done;
            Run(w, 1);
            Assert.Equal(next, K(w).Phase);
        }
        var stay = World(3);
        K(stay).Hp = 13;
        K(stay).PatternsDoneForTests = 8;
        Run(stay, 1);
        Assert.Equal(3, K(stay).Phase);
    }

    [Fact]
    public void The_face_follows_the_player_unless_a_slash_locks_it()
    {
        var w = World();
        var k = K(w);
        w.Player.Left = 100; Run(w, 1);
        Assert.Equal(Konngara.FdLeft, k.FaceDirection);
        w.Player.Left = 500; Run(w, 1);
        Assert.Equal(Konngara.FdRight, k.FaceDirection);
        w.Player.Left = 300; Run(w, 1);
        Assert.Equal(Konngara.FdCenter, k.FaceDirection);
        var s = World(5, pattern: 0);
        K(s).PhaseFrame = 9;
        s.Player.Left = 100;
        Run(s, 1);                                                    // frame 10 of the slash rain: centre, closed, locked
        Assert.Equal((Konngara.FdCenter, Konngara.FeClosed, false), (K(s).FaceDirection, K(s).FaceExpression, K(s).FaceDirectionCanChange));
        Run(s, 5);
        Assert.Equal(Konngara.FdCenter, K(s).FaceDirection);
    }

    // ---- hits ----

    [Fact]
    public void The_orb_inside_the_head_box_scores_10000_and_outside_it_does_nothing()
    {
        var w = World();
        w.Orb.Left = 288 + 64; w.Orb.Top = 120 + 40;                  // the far corner of the box (current position)
        Run(w, 1);
        Assert.Equal(17, K(w).Hp);
        Assert.Equal(10000u, w.Score);
        var o = World();
        o.Orb.Left = 288 + 65; o.Orb.Top = 130;
        Run(o, 1);
        Assert.Equal(18, K(o).Hp);
    }

    [Fact]
    public void Hp_0_in_phase_7_means_defeated()
    {
        var w = World(7);
        Assert.False(K(w).Defeated);
        K(w).Hp = 0;
        Assert.True(K(w).Defeated);
        Assert.True(K(w).DefeatTicks() > Konngara.DefeatSequenceTicks);
    }

    // ---- patterns ----

    static GameWorld InPattern(int phase, int pattern, int frame, Rank rank = Rank.Normal, bool invincible = true)
    {
        var w = World(phase, pattern, rank, invincible: invincible);
        K(w).PhaseFrame = frame - 1;
        return w;
    }

    [Fact]
    public void Symmetrical_from_the_cup_fires_pairs_then_mirrored_pairs()
    {
        var w = InPattern(1, 1, 100);
        RunTo(w, 139);
        Assert.Equal(10, w.Pellets.Alive);                            // 104, 112, 120, 128, 136: five pairs
        Run(w, 5);                                                    // 144: angle 0x45 and its mirror, four pellets
        Assert.Equal(14, w.Pellets.Alive);
        var x = InPattern(1, 1, 100);
        RunTo(x, 299);
        Run(x, 1);                                                    // 300 resets the pattern
        Assert.Equal(0, K(x).PhaseFrame);
        Assert.Equal(99, K(x).PatternCur);
    }

    [Fact]
    public void Aimed_spray_aims_from_the_cup_with_a_sweeping_offset()
    {
        var w = InPattern(3, 1, 100, Rank.Normal);
        Run(w, 1);                                                    // frame 100: 100 % 4 == 0
        Assert.Equal(1, w.Pellets.Alive);
        int a = AngleMath.Iatan2(Player.Top - (188 - 4), 304 - (312 - 34)) + 0x20;
        var expect = World();
        expect.Pellets.AddSingle(312, 188, a & 0xFF, 48);
        int i = Array.FindIndex(w.Pellets.Moving, m => m), j = Array.FindIndex(expect.Pellets.Moving, m => m);
        Assert.Equal((expect.Pellets.VelX[j], expect.Pellets.VelY[j]), (w.Pellets.VelX[i], w.Pellets.VelY[i]));
        RunTo(w, 700);
        Assert.Equal(0, K(w).PhaseFrame);
    }

    [Fact]
    public void Two_snakes_spawn_at_100_and_150_and_hurt_by_the_head_point()
    {
        var w = InPattern(1, 2, 100);
        Run(w, 1);
        Assert.True(K(w).TwoSnakes.Alive(0));
        Assert.False(K(w).TwoSnakes.Alive(1));
        Assert.Equal(312, K(w).TwoSnakes.Left[0][4]);
        RunTo(w, 150);
        Assert.True(K(w).TwoSnakes.Alive(1));
        for (int j = 0; j < 5; j++) { K(w).TwoSnakes.Left[0][j] = 314; K(w).TwoSnakes.Top[0][j] = 378; }
        w.PlayerIsHit = false;
        w.PlayerInvincible = false;
        Run(w, 1);
        Assert.True(w.PlayerIsHit);
        var safe = InPattern(1, 2, 100, invincible: true);
        Run(safe, 1);
        for (int j = 0; j < 5; j++) { K(safe).TwoSnakes.Left[0][j] = 314; K(safe).TwoSnakes.Top[0][j] = 378; }
        Run(safe, 1);
        Assert.False(safe.PlayerIsHit);
    }

    [Fact]
    public void Four_snakes_spawn_every_50_frames_from_the_cup_and_the_sleeve()
    {
        var w = InPattern(3, 2, 100);
        RunTo(w, 250);
        var s = K(w).FourSnakes;
        Assert.All(new[] { 0, 1, 2, 3 }, i => Assert.True(s.Alive(i)));
        Assert.Equal(290, s.Left[3][4]);
        RunTo(w, 400);
        Assert.Equal(0, K(w).PhaseFrame);
    }

    [Fact]
    public void Aimed_rows_walk_back_and_forth_and_end_when_they_are_low_enough()
    {
        var w = InPattern(3, 0, 100);
        Run(w, 1);
        Assert.True(K(w).RowActive);
        Assert.Equal((290, 200), (K(w).RowLeft, K(w).RowTop));
        int guard = 0;
        while (K(w).PhaseFrame != 0 && guard++ < 4000) { Run(w, 1); w.Pellets.ResetNonClouds(); }
        Assert.Equal(0, K(w).PhaseFrame);
        Assert.False(K(w).RowActive);
        Assert.True(K(w).RowTop >= 64 + 192);
    }

    [Fact]
    public void The_sword_ray_follows_its_seven_keyframes_and_stops_at_360()
    {
        var w = InPattern(3, 3, 100);
        Run(w, 1);
        Assert.True(K(w).RayActive);
        Assert.Equal((0, 64 + 8 * 25 - 8), (K(w).RayEndX, K(w).RayEndY));      // moved up one step already
        RunTo(w, 125);
        Assert.Equal((8, 64), (K(w).RayEndX, K(w).RayEndY));
        RunTo(w, 205);
        Assert.Equal((Playfield.Right - 1, 64 + 8), (K(w).RayEndX, K(w).RayEndY));
        RunTo(w, 360);
        Run(w, 0);
        Assert.False(K(w).RayActive);
        Assert.Equal(0, K(w).PhaseFrame);
    }

    [Fact]
    public void Slash_rain_shows_seven_images_and_resets_after_200()
    {
        var w = InPattern(5, 0, 10);
        Run(w, 1);
        Assert.Equal(-1, K(w).SlashImage);
        RunTo(w, 50);
        Assert.Equal(0, K(w).SlashImage);
        RunTo(w, 170);
        Assert.Equal(6, K(w).SlashImage);
        RunTo(w, 201);
        Assert.Equal(0, K(w).PhaseFrame);
        Assert.True(K(w).FaceDirectionCanChange);
        Assert.Equal(-1, K(w).SlashImage);
    }

    [Fact]
    public void Slash_rain_fires_two_gravity_pellets_per_step_along_the_path()
    {
        var w = InPattern(5, 0, 10);
        RunTo(w, 139);
        // Normal interval 3: shots at 102 .. 138 (13 of them), two pellets each
        Assert.Equal(2 * 13, w.Pellets.Alive);
    }

    [Fact]
    public void Triangular_and_aimed_slashes_fire_on_the_path_from_the_sword_to_the_corners()
    {
        var t = InPattern(5, 2, 10);
        RunTo(t, 139);
        Assert.Equal(2 * 13, t.Pellets.Alive);                         // every 3rd frame from 102 to 138, two pellets each
        var a = InPattern(7, 11, 10);
        RunTo(a, 139);
        Assert.Equal(13, a.Pellets.Alive);
    }

    [Fact]
    public void Lasers_sweep_the_floor_and_each_one_comes_with_a_three_way_spread()
    {
        var w = InPattern(5, 1, 100);
        Run(w, 1);
        Assert.True(w.Lasers[10].Alive);
        Assert.Equal(3, w.Pellets.Alive);
        RunTo(w, 170);
        Run(w, 0);
        Assert.Equal(0, K(w).PhaseFrame);                              // 8 lasers: 0, 96 ... 672 >= 640
        Assert.Equal(8 * 3, w.Pellets.Alive);
    }

    [Fact]
    public void The_semicircle_alternates_rain_and_falling_rings()
    {
        var w = InPattern(7, 10, 100);
        Run(w, 1);                                                     // 100 % 20 == 0
        Assert.Equal(10, w.Pellets.Alive);
        RunTo(w, 110);
        Assert.Equal(20, w.Pellets.Alive);
        int fall = Enumerable.Range(0, Pellets.Count).Count(i => w.Pellets.Moving[i] && w.Pellets.Motion[i] == PelletMotion.FallStraightFromTopThenRegular);
        Assert.Equal(10, fall);
        RunTo(w, 300);
        Assert.Equal(0, K(w).PhaseFrame);
    }

    [Fact]
    public void Diamonds_cross_to_the_edges_then_fire_a_group_and_rain_of_five()
    {
        var w = InPattern(1, 0, 100, Rank.Hard);
        Run(w, 1);
        Assert.True(K(w).DiamondsActive);
        Assert.Equal(5, w.Pellets.Alive);                              // Hard: a wide aimed 5-way spread
        w.Pellets.ResetNonClouds();
        int guard = 0;
        while (K(w).DiamondLeft[0] > 0 && guard++ < 200) Run(w, 1);
        Assert.Equal((0, Playfield.Right - 8, 0, Playfield.Right - 8), (K(w).DiamondLeft[0], K(w).DiamondLeft[1], K(w).DiamondLeft[2], K(w).DiamondLeft[3]));
        guard = 0;
        while (K(w).DiamondTop[0] > Playfield.Top && guard++ < 400) Run(w, 1);
        Assert.Equal(Playfield.Top, K(w).DiamondTop[0]);
        w.Pellets.ResetNonClouds();
        Run(w, 14);                                                    // Hard interval 14: the first volley is on the 14th edge frame
        Assert.True(w.Pellets.Alive >= 5);
        guard = 0;
        while (K(w).PhaseFrame != 0 && guard++ < 400) { Run(w, 1); w.Pellets.ResetNonClouds(); }
        Assert.Equal(0, K(w).PhaseFrame);
        Assert.False(K(w).DiamondsActive);
    }

    [Fact]
    public void The_defeat_grants_50000_points_once_and_clears_the_game()
    {
        var g = new GameSession(new GameOptions { FrameRand = 12, Stage = Stage(), Lives = 3, Bombs = 1 });
        for (int i = 0; i < Konngara.EntranceTotal + 5; i++) g.Step(GameButtons.None);
        Assert.Equal(SessionPhase.Playing, g.Phase);
        var k = (Konngara)g.World.Boss!;
        k.Phase = 7; k.Hp = 0;
        g.Step(GameButtons.None);
        Assert.Equal(SessionPhase.BossDefeat, g.Phase);
        uint before = g.World.Score;
        Assert.False(g.World.GameCleared);
        int guard = 0;
        while (g.Phase == SessionPhase.BossDefeat && guard++ < 5000) { Assert.False(g.World.GameCleared && g.World.Score == before); g.Step(GameButtons.None); }
        Assert.Equal(SessionPhase.StageBonus, g.Phase);
        Assert.True(g.World.GameCleared);
        Assert.Equal(before + 50000u + g.Bonus!.Value.Total, g.World.Score);
    }

    [Fact]
    public void Without_art_the_head_is_a_box_and_snake_heads_are_white()
    {
        var g = new GameSession(new GameOptions { FrameRand = 12, Stage = Stage(), Lives = 3, Bombs = 1 });
        for (int i = 0; i < Konngara.EntranceTotal + 5; i++) g.Step(GameButtons.None);
        var k = (Konngara)g.World.Boss!;
        k.TwoSnakes.Left[0][0] = 100; k.TwoSnakes.Top[0][0] = 200;
        for (int j = 1; j < 5; j++) { k.TwoSnakes.Left[0][j] = 300; k.TwoSnakes.Top[0][j] = 300; }
        var fb = new Th01.Core.Graphics.Framebuffer();
        g.Render(fb);
        Assert.Equal(12, fb.GetPixel(300, 130));
        Assert.Equal(7, fb.GetPixel(103, 203));
        Assert.Equal(9, fb.GetPixel(303, 303));
    }

    // ---- determinism ----

    static (ulong hash, int maxPhase, int maxAlive) Fight(uint seed, Rank rank)
    {
        var g = new GameSession(new GameOptions { FrameRand = seed, Stage = Stage(), Rank = rank, Lives = 1000, Bombs = 2 });
        uint x = 0xBADC0FFE;
        GameButtons cur = GameButtons.None;
        int maxPhase = 0, maxAlive = 0;
        for (int i = 0; i < 15000; i++)
        {
            x ^= x << 13; x ^= x >> 17; x ^= x << 5;
            if (i % 6 == 0)
            {
                cur = GameButtons.None;
                if ((x & 1) != 0) cur |= GameButtons.Left;
                if ((x & 2) != 0) cur |= GameButtons.Right;
                if ((x & 4) != 0) cur |= GameButtons.Shot;
            }
            if (i % 200 == 199 && g.Phase == SessionPhase.Playing && g.World.Boss is Konngara)
            {
                g.World.Orb.Left = 300; g.World.Orb.Top = 130;
            }
            g.Step(cur);
            if (g.World.Boss is Konngara kk) maxPhase = Math.Max(maxPhase, kk.Phase);
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
    }
}
