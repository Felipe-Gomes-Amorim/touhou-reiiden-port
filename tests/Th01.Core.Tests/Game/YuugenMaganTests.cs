using Th01.Core.Audio;
using Th01.Core.Game;
using Th01.Core.Graphics;
using Th01.Core.Input;
using Th01.Core.Rng;
using Th01.Core.Sim;

namespace Th01.Core.Tests.Game;

public class YuugenMaganTests
{
    static StageSetup Stage(int route = 0) => new()
    {
        StageId = 9, Route = route,
        Cards = new bool[StageSetup.Rows, StageSetup.Cols],
        Obstacles = new byte[StageSetup.Rows, StageSetup.Cols],
    };

    static (GameWorld w, YuugenMagan y) Fight(int phase = 0, Rank rank = Rank.Normal, uint seed = 9, bool invincible = true)
    {
        var w = new GameWorld(new GameOptions { FrameRand = seed, Stage = Stage(), Rank = rank }, NullSoundSink.Instance);
        var y = (YuugenMagan)w.Boss!;
        y.Phase = phase;
        w.Orb.Left = 0; w.Orb.Top = 380; w.Orb.PrevLeft = 0; w.Orb.PrevTop = 380;
        w.Player.Left = 304;
        w.PlayerInvincible = invincible;
        w.Rng = new IRand(seed);
        return (w, y);
    }

    static void Run(YuugenMagan y, int n) { for (int i = 0; i < n; i++) y.Update(); }

    static int[] Images(YuugenMagan y) => y.Eyes.Select(e => e.Image).ToArray();   // W, E, SW, SE, N

    // ---- set-up ----

    [Fact]
    public void Eyes_hp_and_the_points_derived_from_them()
    {
        var (w, y) = Fight();
        Assert.Equal(16, y.Hp);
        Assert.Equal((64, 128), (y.West.Left, y.West.Top));
        Assert.Equal((512, 128), (y.East.Left, y.East.Top));
        Assert.Equal((192, 176), (y.SouthWest.Left, y.SouthWest.Top));
        Assert.Equal((384, 176), (y.SouthEast.Left, y.SouthEast.Top));
        Assert.Equal((288, 64), (y.North.Left, y.North.Top));
        Assert.Equal((92, 156, 160, 164), (y.West.OffcenterX, y.West.IrisTop, y.West.IrisCenterY, y.West.PupilBottom));
        Assert.All(y.Eyes, e => Assert.Equal(YuugenMagan.CHidden, e.Image));
        Assert.All(y.Eyes, e => Assert.True(e.HitboxInactive));
    }

    [Fact]
    public void The_fight_starts_at_once_without_an_entrance_or_a_wait_for_the_first_shot()
    {
        var g = new GameSession(new GameOptions { FrameRand = 3, Stage = Stage() });
        Assert.Equal(SessionPhase.Playing, g.Phase);
        g.Step(GameButtons.None);
        Assert.Equal(1, g.World.Boss!.PhaseFrame);
        Assert.Equal(3000, StageTimer.InitialFor(9, 0));                // Makai stage 10 timer
    }

    // ---- phase 0 ----

    [Fact]
    public void Eyes_open_in_the_original_staggered_order_and_close_again()
    {
        var (_, y) = Fight();
        Run(y, 100);
        Assert.Equal(new[] { 1, 1, 0, 0, 0 }, Images(y));                 // lateral eyes closed-open start
        Assert.False(y.West.HitboxInactive);
        Assert.True(y.SouthWest.HitboxInactive);
        Run(y, 20);                                                       // 120: lateral half open, south starts
        Assert.Equal(new[] { 2, 2, 1, 1, 0 }, Images(y));
        Run(y, 20);                                                       // 140
        Assert.Equal(new[] { 6, 6, 2, 2, 1 }, Images(y));
        Run(y, 20);                                                       // 160: lateral fully open and looking down
        Assert.Equal(new[] { 3, 3, 6, 6, 2 }, Images(y));
        Run(y, 20);                                                       // 180: lateral window is over
        Assert.Equal(new[] { 3, 3, 3, 3, 6 }, Images(y));
        Run(y, 20);                                                       // 200
        Assert.Equal(new[] { 3, 3, 3, 3, 3 }, Images(y));
        Run(y, 40);                                                       // 240: closing
        Assert.Equal(new[] { 2, 2, 2, 2, 2 }, Images(y));
        Assert.All(y.Eyes, e => Assert.True(e.HitboxInactive));
        Run(y, 20);
        Assert.Equal(new[] { 1, 1, 1, 1, 1 }, Images(y));
        Run(y, 20);
        Assert.Equal(new[] { 0, 0, 0, 0, 0 }, Images(y));
        Run(y, 20);                                                       // 300
        Assert.Equal(new[] { 1, 1, 0, 0, 0 }, Images(y));
        Run(y, 20);                                                       // 320
        Assert.Equal(new[] { 2, 2, 0, 0, 0 }, Images(y));
        Assert.False(y.West.HitboxInactive);
        Run(y, 10);                                                       // 330: nothing yet
        Assert.Equal(0, y.Phase);
        Run(y, 1);                                                        // 331
        Assert.Equal(1, y.Phase);
        Assert.Equal(0, y.PhaseFrame);
        Assert.Equal(YuugenMagan.CLeft, y.West.Image);
        Assert.Equal(YuugenMagan.CDown, y.East.Image);
    }

    [Theory]
    [InlineData(68, false)]
    [InlineData(69, true)]       // (ox - 24, ox - 8) = (68, 84) exclusive for a 32-wide player at the west eye
    [InlineData(83, true)]
    [InlineData(84, false)]
    public void West_laser_window(int playerLeft, bool hit)
    {
        var (w, y) = Fight(invincible: false);
        w.Player.Left = playerLeft;
        Run(y, 160);
        Assert.False(w.PlayerIsHit);                  // lasers only start after the lateral eyes are open
        Run(y, 1);
        Assert.Equal(hit, w.PlayerIsHit);
    }

    [Theory]
    [InlineData(516, false)]
    [InlineData(517, true)]
    [InlineData(555, true)]      // the east eye tests an 8 px wide player: (ox - 24, ox + 16) = (516, 556)
    [InlineData(556, false)]
    public void East_laser_window_is_much_wider_than_the_others(int playerLeft, bool hit)
    {
        var (w, y) = Fight(invincible: false);
        w.Player.Left = playerLeft;
        Run(y, 161);
        Assert.Equal(hit, w.PlayerIsHit);
    }

    [Fact]
    public void Invincibility_clears_a_hit_flagged_in_the_same_frame_and_lasers_stop_at_300()
    {
        var (w, y) = Fight(invincible: true);
        w.Player.Left = 80;
        Run(y, 170);
        w.PlayerIsHit = true;
        Run(y, 1);
        Assert.False(w.PlayerIsHit);
        var (v, z) = Fight(invincible: false);
        v.Player.Left = 80;
        Run(z, 299);
        Assert.True(v.PlayerIsHit);
        v.PlayerIsHit = false;
        Run(z, 1);                                    // frame 300: the lateral lasers are gone
        Assert.False(v.PlayerIsHit);
    }

    [Theory]
    [InlineData(Rank.Easy, 0, 0)]
    [InlineData(Rank.Normal, 0, 0)]
    [InlineData(Rank.Hard, 15, 0)]       // three eyes, 5-way spread each, at frame 240
    [InlineData(Rank.Lunatic, 0, 25)]    // the Lunatic volley is the one at 260 (the 240 one is unreachable)
    public void Closing_volleys_depend_on_the_rank(Rank rank, int at240, int at260)
    {
        var (w, y) = Fight(rank: rank);
        Run(y, 240);
        Assert.Equal(at240, w.Pellets.Alive);
        Run(y, 20);
        Assert.Equal(at240 + at260, w.Pellets.Alive);
    }

    [Fact]
    public void Phase_0_never_hurts_the_boss_and_the_intervals_follow_the_rank()
    {
        foreach (var (rank, interval) in new[] { (Rank.Easy, 350), (Rank.Normal, 300), (Rank.Hard, 200), (Rank.Lunatic, 130) })
        {
            var (w, y) = Fight(rank: rank);
            w.PlayerInvincible = true;
            w.Orb.PrevLeft = y.West.Left + 20; w.Orb.PrevTop = y.West.Top + 10;
            Run(y, 330);
            Assert.Equal(16, y.Hp);
            w.Orb.PrevLeft = 0; w.Orb.PrevTop = 380;
            Run(y, interval);                         // the pattern clock uses frame % interval == 10 ... check the first volley
            Assert.True(w.Pellets.Alive >= 4, $"{rank}");
        }
    }

    // ---- phase 1 ----

    [Fact]
    public void Phase_1_fires_wide_pairs_then_an_aimed_shot_and_ends_after_one_hit_or_1100_frames()
    {
        var (w, y) = Fight(phase: 1);
        y.West.Image = YuugenMagan.CHalfOpen; y.East.Image = YuugenMagan.CHalfOpen;
        Run(y, 9);
        Assert.Equal(0, w.Pellets.Alive);
        Run(y, 1);                                    // frame 10
        Assert.Equal(4, w.Pellets.Alive);             // two eyes x a 2-way spread
        int i0 = Array.FindIndex(w.Pellets.Moving, p => p);
        Assert.Equal(((96 - 4) << 4, (164 - 4) << 4), (w.Pellets.CurLeft[i0], w.Pellets.CurTop[i0]));
        Run(y, 100);                                  // frame 110: the single aimed pellets
        Assert.Equal(4 + 4 * 4 + 2, w.Pellets.Alive); // 25, 40, 60, 80 add four each, 110 adds two
        Assert.Equal(1, y.Phase);
        Run(y, 1100 - 110);
        Assert.Equal(1, y.Phase);
        Run(y, 1);
        Assert.Equal(2, y.Phase);
    }

    [Fact]
    public void A_visible_open_eye_is_hit_by_the_previous_orb_position_and_one_hit_ends_phase_1()
    {
        var (w, y) = Fight(phase: 1, invincible: false);
        y.West.Image = YuugenMagan.CHalfOpen;
        y.West.HitboxInactive = false;
        w.Orb.PrevLeft = y.West.Left - 12; w.Orb.PrevTop = y.West.Top + 24;     // the corner of the hitbox
        w.Orb.VelocityX = OrbVelocityX.Left4;
        y.Update();
        Assert.Equal(15, y.Hp);
        Assert.Equal(5000u, w.Score);
        Assert.Equal(OrbVelocityX.Right4, w.Orb.VelocityX);
        Assert.Equal(2, y.Phase);
        // just outside, hidden or switched off: nothing
        // phase 1 only drives the lateral eyes, so the south-west eye shows the hidden / switched-off cases
        foreach (var setup in new Action<YuugenMagan>[]
        {
            e => e.SouthWest.Image = YuugenMagan.CHidden, e => e.SouthWest.HitboxInactive = true,
        })
        {
            var (v, z) = Fight(phase: 1, invincible: false);
            z.SouthWest.Image = YuugenMagan.CHalfOpen; z.SouthWest.HitboxInactive = false;
            setup(z);
            v.Orb.PrevLeft = z.SouthWest.Left + 10; v.Orb.PrevTop = z.SouthWest.Top + 10;
            z.Update();
            Assert.Equal(16, z.Hp);
        }
        var (u, t) = Fight(phase: 1, invincible: false);
        t.West.Image = YuugenMagan.CHalfOpen; t.West.HitboxInactive = false;
        u.Orb.PrevLeft = t.West.Left - 13; u.Orb.PrevTop = t.West.Top + 10;
        t.Update();
        Assert.Equal(16, t.Hp);
    }

    // ---- phases 2, 4, 6, 8, 10: eye toggles ----

    [Fact]
    public void Eye_toggle_swaps_which_eyes_are_open_in_70_frames_and_moves_to_the_next_phase()
    {
        var (_, y) = Fight(phase: 2);
        y.West.Image = YuugenMagan.CHalfOpen; y.East.Image = YuugenMagan.CHalfOpen;
        y.West.HitboxInactive = false; y.East.HitboxInactive = false;
        Run(y, 19);
        Assert.Equal(new[] { 2, 2, 0, 0, 0 }, Images(y));
        Run(y, 1);                                                       // 20
        Assert.Equal(new[] { 2, 2, 1, 1, 0 }, Images(y));
        Assert.False(y.SouthWest.HitboxInactive);
        Run(y, 20);                                                      // 40
        Assert.Equal(new[] { 1, 1, 2, 2, 0 }, Images(y));
        Assert.True(y.West.HitboxInactive);
        Run(y, 20);                                                      // 60
        Assert.Equal(new[] { 0, 0, 6, 6, 0 }, Images(y));
        Run(y, 9);
        Assert.Equal(2, y.Phase);
        Run(y, 1);
        Assert.Equal(3, y.Phase);
        Assert.Equal(0, y.PhaseFrame);
    }

    // ---- phases 3 and 7: missile pairs ----

    static GameWorld MissileWorld(int phase, uint seed, out YuugenMagan y)
    {
        var (w, boss) = Fight(phase: phase, seed: seed);
        y = boss;
        y.SouthWest.Image = 6; y.SouthEast.Image = 6;
        return w;
    }

    [Fact]
    public void Missile_pairs_draw_a_variation_at_frame_100_aim_at_the_player_and_fire_every_8_frames()
    {
        var w = MissileWorld(3, 5, out var y);
        var probe = new IRand(5);
        int variation = probe.Next() % 3;
        Run(y, 99);
        Assert.Equal(0, y.Subphase);
        Assert.Equal(YuugenMagan.CClosed, y.SouthWest.Image);                // closed from frame 90
        Run(y, 1);                                                           // 100
        Assert.Equal(variation * 4 + 1, y.Subphase);
        Assert.Equal(probe.Seed, w.Rng.Seed);
        Assert.Equal(0, w.Missiles.Alive);
        Run(y, 4);                                                           // 104: first pair
        Assert.Equal(2, w.Missiles.Alive);
        int a(int eyeLeft, int eyeTop) => AngleMath.Iatan2(400 - eyeTop + 28, (304 - 8) - eyeLeft + 28);
        int angleSw = (a(192, 176) - 0x10) & 0xFF, angleSe = (a(384, 176) + 0x10) & 0xFF;
        Assert.Equal((AngleMath.PolarX(0, 8, angleSw) << 4, AngleMath.PolarY(0, 8, angleSw) << 4), (w.Missiles.VelX[0], w.Missiles.VelY[0]));
        Assert.Equal((AngleMath.PolarX(0, 8, angleSe) << 4, AngleMath.PolarY(0, 8, angleSe) << 4), (w.Missiles.VelX[1], w.Missiles.VelY[1]));
        Assert.Equal(((192 + 28) << 4, (176 + 28) << 4), (w.Missiles.CurLeft[0], w.Missiles.CurTop[0]));
    }

    [Fact]
    public void Missile_pair_subphases_advance_after_200_frames_and_11_pairs_each_and_a_lap_ends_at_frame_0()
    {
        var w = MissileWorld(3, 5, out var y);
        int variation = new IRand(5).Next() % 3;
        Run(y, 200);
        Assert.Equal(variation * 4 + 2, y.Subphase);                         // angle-shift 1
        Run(y, 80);                                                          // 280: ten pairs at 201, 209 ... 273
        Assert.Equal(variation * 4 + 2, y.Subphase);
        Run(y, 1);                                                           // 281: the eleventh
        Assert.Equal(variation * 4 + 3, y.Subphase);
        Run(y, 87);                                                          // 368
        Assert.Equal(variation * 4 + 3, y.Subphase);
        Run(y, 1);                                                           // 369: eleventh pair of shift 2
        Assert.Equal(0, y.Subphase);
        Assert.Equal(0, y.PhaseFrame);
        int total = w.Missiles.Alive;
        Assert.True(total > 20);
    }

    [Fact]
    public void Phase_3_ends_after_five_laps_and_phase_7_after_more_than_four_with_two_variations()
    {
        var (w, y) = Fight(phase: 3, seed: 7);
        w.Orb.VelocityX = OrbVelocityX.Zero;
        int guard = 0;
        while (y.Phase == 3 && guard++ < 6000) { y.Update(); w.Missiles.Reset(); }
        Assert.Equal(4, y.Phase);
        Assert.InRange(guard, 5 * 369, 5 * 369 + 1);                         // 5 laps of 369 frames
        var (v, z) = Fight(phase: 7, seed: 7);
        guard = 0;
        int maxVariationSeen = 0;
        while (z.Phase == 7 && guard++ < 6000) { z.Update(); v.Missiles.Reset(); maxVariationSeen = Math.Max(maxVariationSeen, z.Subphase / 4); }
        Assert.Equal(8, z.Phase);
        Assert.True(maxVariationSeen <= 1);                                  // 2 variations
        Assert.InRange(guard, 5 * 369 - 1, 5 * 369 + 1);
    }

    // ---- phase 5: spirals ----

    [Fact]
    public void Spirals_fire_from_both_lateral_eyes_with_a_mirrored_angle_and_the_ranked_interval()
    {
        var (w4, y4) = Fight(phase: 4);
        Run(y4, 70);
        Assert.Equal(5, y4.Phase);
        Run(y4, 99);
        Assert.Equal(0, w4.Pellets.Alive);
        Run(y4, 1);                                                          // 100: first volley, spray 1 starts
        Assert.Equal(2, w4.Pellets.Alive);
        int angleEast = (0 - 100) & 0xFF;                                    // decremented every frame since phase 5 began
        int speed = 64 + 6 + (-4 * 70) / 40;                                 // 63
        int west = (0x80 - angleEast) & 0xFF;
        Assert.Equal((AngleMath.PolarX(0, speed, west), AngleMath.PolarY(0, speed, west)), (w4.Pellets.VelX[0], w4.Pellets.VelY[0]));
        Assert.Equal((AngleMath.PolarX(0, speed, angleEast), AngleMath.PolarY(0, speed, angleEast)), (w4.Pellets.VelX[1], w4.Pellets.VelY[1]));
        Run(y4, 180);                                                        // up to frame 280
        Assert.Equal(2 * 23, w4.Pellets.Alive);                              // every 8: 0, 8, ... 176
        Run(y4, 1);
        Assert.Equal(2 * 23, w4.Pellets.Alive);
    }

    [Fact]
    public void Second_spiral_pellets_fall_straight_once_they_reach_the_top()
    {
        var (w, _) = Fight();
        w.Pellets.AddSingle(300, 80, 0xC0, 64, PelletMotion.FallStraightFromTopThenRegular, 64);   // flies up
        int i = Array.FindIndex(w.Pellets.Moving, m => m);
        for (int k = 0; k < 200 && w.Pellets.Motion[i] != PelletMotion.Regular; k++) w.Pellets.Update();
        Assert.Equal(PelletMotion.Regular, w.Pellets.Motion[i]);
        Assert.Equal(0, w.Pellets.VelX[i]);
        Assert.Equal(64, w.Pellets.VelY[i]);
        Assert.InRange(w.Pellets.CurTop[i] >> 4, 64, 70 + 4);   // switched at <= 70, then fell one step
    }

    // ---- phase 9: lasers across the floor ----

    [Fact]
    public void The_floor_laser_sweeps_16_px_every_4_frames_and_hits_through_its_end_point()
    {
        var (w, y) = Fight(phase: 9, invincible: false);
        w.Player.Left = 304;
        Run(y, 120);
        Assert.False(w.PlayerIsHit);
        Assert.Equal(1, y.Subphase);                                          // active since frame 40
        Run(y, 1);                                                            // 121: offset 320 is inside (304, 336)
        Assert.True(w.PlayerIsHit);
    }

    [Theory]
    [InlineData(303, false)]
    [InlineData(304, false)]     // strict: x must be > player_left
    [InlineData(305, true)]
    [InlineData(335, true)]
    [InlineData(336, false)]
    public void Line_bullet_hit_window_and_the_bottom_condition(int endX, bool hit)
    {
        var (w, y) = Fight(phase: 11, invincible: false);
        // phase 11 draws the ring between the eyes; put a known end point by moving the north eye
        y.SouthWest.Left = endX + 4 - 32;                  // off-center x = left + 32 - 4 = endX ... (ring end point of SW)
        y.SouthWest.Top = 400;                              // iris top far below the player's centre
        y.PhaseFrame = 99;
        y.Update();                                         // frame 100: the ring is "put" and tested
        Assert.Equal(hit, w.PlayerIsHit);
    }

    [Fact]
    public void Line_bullets_need_the_end_point_below_the_players_centre_and_no_invincibility()
    {
        var (w, y) = Fight(phase: 11, invincible: false);
        y.SouthWest.Left = 304 + 16 - 32 + 4; y.SouthWest.Top = 300;      // x inside, but y = 300 + 28 < 384
        y.PhaseFrame = 99; y.Update();
        Assert.False(w.PlayerIsHit);
        var (v, z) = Fight(phase: 11, invincible: true);
        z.SouthWest.Left = 304 + 16 - 32 + 4; z.SouthWest.Top = 400;
        z.PhaseFrame = 99; z.Update();
        Assert.False(v.PlayerIsHit);
    }

    [Fact]
    public void Floor_laser_passes_end_with_a_narrow_spread_above_normal_and_the_dual_pass_stops_at_the_centre()
    {
        var (w, y) = Fight(phase: 9, rank: Rank.Hard);
        Run(y, 41 + 4 * 37);                          // first pass: offset 592 at frame 189
        Assert.Equal(2, y.Subphase);                  // iteration 1, prepare
        Assert.Equal(3, w.Pellets.Alive);             // narrow 3-way from the north eye
        Run(y, 40);                                   // frame 40 of the second pass activates it, offset restarts at 0 (right to left)
        Assert.Equal(3, y.Subphase);
        Run(y, 4 * 37 + 1);                           // second pass done
        Assert.Equal(4, y.Subphase);
        Assert.Equal(6, w.Pellets.Alive);
        Run(y, 40);                                   // third pass activated
        Assert.Equal(5, y.Subphase);
        Run(y, 4 * 18 + 1);                           // offset 288 reached: the dual pass ends
        Assert.Equal(10, y.Phase);
        Assert.Equal(9, w.Pellets.Alive);             // Hard: another 3-way
    }

    [Fact]
    public void Lunatic_floor_lasers_end_every_pass_with_narrow_spreads_ending_with_a_five_way()
    {
        var (w, y) = Fight(phase: 9, rank: Rank.Lunatic);
        for (int i = 0; i < 5000 && y.Phase == 9; i++) y.Update();
        Assert.Equal(10, y.Phase);
        Assert.Equal(3 + 3 + 5, w.Pellets.Alive);
    }

    // ---- phase 11: pentagram between the eyes and its shrink ----

    [Fact]
    public void The_pentagram_shrinks_8_px_per_step_with_one_step_too_many_then_moves_on_to_phase_12()
    {
        var (w, y) = Fight(phase: 11);
        Run(y, 140);
        Assert.Equal(2, y.Subphase);                                          // moving from frame 140
        Run(y, 80);                                                           // frame 220: step 20 (distance 160), not over yet
        Assert.Equal(2, y.Subphase);
        Assert.Equal(11, y.Phase);
        Run(y, 1);                                                            // frame 221: 168 > 160: done, and phase 12 starts at once
        Assert.Equal(12, y.Phase);
        Assert.Equal(0, y.PhaseFrame);
        Assert.Equal(YuugenMagan.EfWest, y.EyesOpen);
        // the corner stays 168 / 2 below the north iris: y = 92 + 84 = 176, then the centre is 64 below it
        Assert.Equal(316, y.PentCenterX);
        Assert.Equal(176 + 64, y.PentCenterY);
        Assert.Equal(64, y.PentRadius);
        Assert.Equal(y.PentY[4], y.PentY[1]);                                 // lateral corners lined up
        Assert.Equal(316 + 64, y.PentX[0]);                                   // corners are recomputed around the centre (quirk)
    }

    [Fact]
    public void Phase_11_waits_for_the_invincibility_to_end_before_moving_on()
    {
        var (w, y) = Fight(phase: 11);
        Run(y, 221 - 1);
        y.Invincible = true; y.InvincibilityFrame = 10;
        Run(y, 1);
        Assert.Equal(11, y.Phase);
        Run(y, 40);
        Assert.Equal(12, y.Phase);
    }

    [Fact]
    public void Phase_12_lifts_the_pentagram_3_px_every_4_frames_to_the_kimono_centre_and_hands_over_to_phase_13()
    {
        var (w, y) = Fight(phase: 11);
        Run(y, 221);
        Assert.Equal(12, y.Phase);
        Run(y, 69);
        Assert.Equal(12, y.Phase);
        Assert.Equal(240 - 17 * 3, y.PentCenterY);                            // 189 after 17 steps
        Run(y, 1);
        Assert.Equal(13, y.Phase);
        Assert.Equal(0, y.PentAngle);
    }

    // ---- phase 13 ----

    static (GameWorld w, YuugenMagan y) Phase13(Rank rank = Rank.Normal)
    {
        var (w, y) = Fight(phase: 11, rank: rank);
        Run(y, 221 + 70);
        Assert.Equal(13, y.Phase);
        return (w, y);
    }

    [Fact]
    public void The_pentagram_cycle_visits_its_attack_phases_in_order_and_ring_pellets_stop_during_slams()
    {
        var (w, y) = Phase13();
        Assert.Equal(0, y.PentPhase);
        Run(y, 99);
        Assert.Equal(0, y.PentPhase);
        Run(y, 1);                                                            // frame 100: prepare is over, first clockwise spin
        Assert.Equal(1, y.PentPhase);
        var seen = new List<int> { 0, 1 };
        int guard = 0;
        while (y.PentPhase < 6 && guard++ < 3000)
        {
            y.Update();
            if (seen[^1] != y.PentPhase) seen.Add(y.PentPhase);
            w.Pellets.ResetNonClouds();
        }
        Assert.Equal(new[] { 0, 1, 2, 3, 4, 5, 6 }, seen);
        Assert.Equal(13, y.Phase);
    }

    [Fact]
    public void Ring_pellets_fly_outwards_from_every_corner_at_the_ranked_interval()
    {
        var (w, y) = Phase13(Rank.Normal);
        Run(y, 13);
        Assert.Equal(0, w.Pellets.Alive);
        Run(y, 1);                                                            // frame 14 (interval 14 on Normal)
        Assert.Equal(5, w.Pellets.Alive);
        int a = AngleMath.Iatan2(y.PentY[0] - y.PentCenterY, y.PentX[0] - y.PentCenterX);
        int speed = 48 + 6 + (-4 * 54) / 40;
        Assert.Equal((y.PentX[0] << 4, y.PentY[0] << 4), (w.Pellets.CurLeft[0], w.Pellets.CurTop[0]));
        Assert.Equal((AngleMath.PolarX(0, speed, a), AngleMath.PolarY(0, speed, a)), (w.Pellets.VelX[0], w.Pellets.VelY[0]));
    }

    [Fact]
    public void Spin_speed_grows_with_the_phase_frame()
    {
        var (w, y) = Phase13();
        Run(y, 100);
        int prev = y.PentAngle, steps = 0;
        for (int k = 0; k < 190 && y.PentPhase == 1; k++)
        {
            y.Update();
            if (y.PentAngle != prev)
            {
                Assert.Equal(y.PhaseFrame / 64 + 2, y.PentAngle - prev);
                steps++;
                prev = y.PentAngle;
            }
        }
        Assert.True(steps > 20);
    }

    [Fact]
    public void A_hit_rotates_which_eye_is_open_and_toggles_them_over_70_frames()
    {
        var (w, y) = Phase13();
        Assert.Equal(YuugenMagan.EfWest, y.EyesOpen);
        y.West.Image = YuugenMagan.CAhead; y.West.HitboxInactive = false;
        w.Orb.PrevLeft = y.West.Left + 10; w.Orb.PrevTop = y.West.Top + 10;
        y.Update();                                                           // tick 1: the hit
        Assert.Equal(15, y.Hp);
        Assert.Equal(YuugenMagan.EfWest, y.EyesOpen);
        y.Update();                                                           // tick 2: the rotation starts
        Assert.Equal(YuugenMagan.EfEast, y.EyesOpen);
        Run(y, 19);                                                           // tick 21: toggle frame 20
        Assert.Equal((YuugenMagan.CHalfOpen, YuugenMagan.CClosed), (y.West.Image, y.East.Image));
        Assert.False(y.East.HitboxInactive);
        Run(y, 20);                                                           // tick 41: toggle frame 40
        Assert.Equal((YuugenMagan.CClosed, YuugenMagan.CHalfOpen), (y.West.Image, y.East.Image));
        Assert.True(y.West.HitboxInactive);
        Run(y, 20);                                                           // tick 61
        Assert.Equal((YuugenMagan.CHidden, YuugenMagan.CAhead), (y.West.Image, y.East.Image));
        Assert.Equal(15, y.Hp);                                               // the old eye was switched off before it could be hit again
    }

    [Fact]
    public void Slam_hits_the_player_through_the_pentagram_lines_and_the_cycle_loops_back_to_phase_10()
    {
        var (w, y) = Phase13();
        w.PlayerInvincible = false;
        int guard = 0;
        while (y.PentPhase < 6 && guard++ < 3000) { y.Update(); w.PlayerIsHit = false; w.Pellets.ResetNonClouds(); }
        Assert.Equal(6, y.PentPhase);
        bool hit = false;
        guard = 0;
        while (y.Phase == 13 && guard++ < 1000)
        {
            y.Update();
            if (w.PlayerIsHit) hit = true;
            w.PlayerIsHit = false;
        }
        Assert.True(hit);                                                     // aimed at the player, so it must connect
        Assert.Equal(10, y.Phase);
        Assert.Equal(7, y.PentPhase);                                         // PAP_PREPARE_2
        Assert.Equal(0, y.PhaseFrame);
    }

    [Fact]
    public void Second_round_grows_the_pentagram_and_the_second_slam_returns_to_the_first_round()
    {
        var (w, y) = Phase13();
        w.PlayerInvincible = true;
        int guard = 0;
        while (!(y.Phase == 13 && y.PentPhase == 8) && guard++ < 20000) { y.Update(); w.Pellets.ResetNonClouds(); }
        Assert.Equal(8, y.PentPhase);
        int radius = y.PentRadius;
        Run(y, 200);
        Assert.Equal(radius + 50, y.PentRadius);                              // +1 every 4 frames for 200 frames
        Assert.Equal(9, y.PentPhase);
        guard = 0;
        while (y.Phase == 13 && guard++ < 1000) y.Update();
        Assert.Equal(10, y.Phase);
        Assert.Equal(0, y.PentPhase);                                         // back to PAP_PREPARE_1
    }

    [Fact]
    public void Hp_zero_in_phase_13_clears_missiles_and_flags_the_defeat_but_phase_11_keeps_them()
    {
        var (w, y) = Phase13();
        w.Missiles.Add(100, 100, 0, 0);
        y.West.Image = YuugenMagan.CAhead; y.West.HitboxInactive = false;
        y.Hp = 1;
        w.Orb.PrevLeft = y.West.Left + 10; w.Orb.PrevTop = y.West.Top + 10;
        y.Update();
        Assert.True(y.Defeated);
        Assert.Equal(0, w.Missiles.Alive);

        var (v, z) = Fight(phase: 11);
        v.Missiles.Add(100, 100, 0, 0);
        z.Eyes[0].Image = YuugenMagan.CAhead; z.Eyes[0].HitboxInactive = false;
        z.Hp = 1;
        v.Orb.PrevLeft = z.West.Left + 10; v.Orb.PrevTop = z.West.Top + 10;
        z.Update();
        Assert.True(z.Defeated);
        Assert.Equal(1, v.Missiles.Alive);
    }

    // ---- session flow ----

    [Fact]
    public void Defeat_goes_to_the_scene_bonus_without_a_route_menu()
    {
        var g = new GameSession(new GameOptions { FrameRand = 12, Stage = Stage(), Lives = 3, Bombs = 1, Rank = Rank.Hard });
        var y = (YuugenMagan)g.World.Boss!;
        y.Phase = 11;
        y.Eyes[0].Image = YuugenMagan.CAhead; y.Eyes[0].HitboxInactive = false;
        y.Hp = 1;
        g.World.Orb.PrevLeft = y.West.Left + 10; g.World.Orb.PrevTop = y.West.Top + 10;
        g.Step(GameButtons.None);
        Assert.True(y.Defeated);
        g.Step(GameButtons.None);
        Assert.Equal(SessionPhase.BossDefeat, g.Phase);
        int guard = 0;
        while (g.Phase == SessionPhase.BossDefeat && guard++ < 1000) g.Step(GameButtons.None);
        Assert.Equal(SessionPhase.StageBonus, g.Phase);
        StageTests.Dismiss(g);
        Assert.Equal(SessionOutcome.Cleared, g.Outcome);
    }

    static (ulong hash, int maxPhase, int maxPellets) Battle(uint seed, Rank rank)
    {
        var g = new GameSession(new GameOptions { FrameRand = seed, Stage = Stage(), Rank = rank, Lives = 1000, Bombs = 2 });
        uint x = 0xDEADBEEF;
        GameButtons cur = GameButtons.None;
        int maxPhase = 0, maxP = 0;
        for (int i = 0; i < 20000; i++)
        {
            x ^= x << 13; x ^= x >> 17; x ^= x << 5;
            if (i % 6 == 0)
            {
                cur = GameButtons.None;
                if ((x & 1) != 0) cur |= GameButtons.Left;
                if ((x & 2) != 0) cur |= GameButtons.Right;
                if ((x & 4) != 0) cur |= GameButtons.Shot;
            }
            if (i % 130 == 129 && g.Phase == SessionPhase.Playing && g.World.Boss is YuugenMagan b)
            {
                var eye = b.Eyes.FirstOrDefault(e => e.Image != 0 && !e.HitboxInactive);
                if (eye != null) { g.World.Orb.Left = eye.Left + 20; g.World.Orb.Top = eye.Top + 10; }
            }
            g.Step(cur);
            if (g.World.Boss != null) maxPhase = Math.Max(maxPhase, g.World.Boss.Phase);
            maxP = Math.Max(maxP, g.World.Pellets.Alive);
        }
        return (g.StateHash(), maxPhase, maxP);
    }

    [Theory]
    [InlineData(Rank.Normal)]
    [InlineData(Rank.Lunatic)]
    public void A_whole_fight_is_deterministic_and_gets_far(Rank rank)
    {
        var a = Battle(33, rank);
        Assert.Equal(a, Battle(33, rank));
        Assert.True(a.maxPhase >= 5);
        Assert.True(a.maxPellets > 10);
        Assert.NotEqual(a.hash, Battle(34, rank).hash);
    }

    // ---- rendering ----

    sealed class Art : IGameArt
    {
        public int FrameCount(string sheet) => 0;
        public bool TryGetFrame(string sheet, int frame, out ArtFrame art)
        {
            if (sheet == "BOSS2.BOS" && frame == 6)
            {
                art = new ArtFrame(64, 48, Enumerable.Repeat((byte)7, 64 * 48).ToArray(), Array.Empty<byte>(), Array.Empty<byte>());
                return true;
            }
            art = default;
            return false;
        }
        public bool TryGetBackground(out ArtFrame art) { art = default; return false; }
    }

    [Fact]
    public void Eyes_are_drawn_from_their_sheet_or_as_boxes_lasers_as_lines_and_art_does_not_change_the_hash()
    {
        var plain = new GameSession(new GameOptions { FrameRand = 3, Stage = Stage() });
        var arted = new GameSession(new GameOptions { FrameRand = 3, Stage = Stage() }, null, new Art());
        for (int i = 0; i < 210; i++) { plain.Step(GameButtons.None); arted.Step(GameButtons.None); }
        Assert.Equal(plain.StateHash(), arted.StateHash());
        var a = new Framebuffer(); var b = new Framebuffer();
        arted.Render(a); plain.Render(b);
        var y = (YuugenMagan)arted.World.Boss!;
        Assert.NotEqual(0, y.Eyes[0].Image);
        Assert.Equal(0, b.GetPixel(y.West.Left + 1, y.West.Top + 1));          // box fallback leaves a 4 px margin
        Assert.NotEqual(0, b.GetPixel(y.West.Left + 10, y.West.Top + 10));
        Assert.Equal(7, b.GetPixel(y.North.OffcenterX, 300));                  // the downward laser is a white line
    }
}
