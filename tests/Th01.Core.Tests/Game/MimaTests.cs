using Th01.Core.Audio;
using Th01.Core.Game;
using Th01.Core.Graphics;
using Th01.Core.Input;
using Th01.Core.Rng;
using Th01.Core.Sim;

namespace Th01.Core.Tests.Game;

public class MimaTests
{
    static StageSetup Stage(int route = 1, int id = 9) => new()
    {
        StageId = id, Route = route,
        Cards = new bool[StageSetup.Rows, StageSetup.Cols],
        Obstacles = new byte[StageSetup.Rows, StageSetup.Cols],
    };

    /// <summary>A world with Mima already past her entrance, in the given phase, with the Orb out of the way.</summary>
    static (GameWorld w, Mima m) Fight(int phase = 1, int pattern = 0, Rank rank = Rank.Normal, uint seed = 9)
    {
        var w = new GameWorld(new GameOptions { FrameRand = seed, Stage = Stage(), Rank = rank }, NullSoundSink.Instance);
        var m = (Mima)w.Boss!;
        m.Phase = phase;
        m.PatternCur = pattern;
        m.PhaseFrame = 0;
        m.Left = Mima.BaseLeft; m.Top = Mima.BaseTop;
        w.Orb.Left = 0; w.Orb.Top = 380; w.Orb.PrevLeft = 0; w.Orb.PrevTop = 380;
        w.Player.Left = 304;
        w.PlayerInvincible = true;                 // patterns are tested without being killed by them
        w.Rng = new IRand(seed);
        return (w, m);
    }

    static void Run(Mima m, int updates) { for (int i = 0; i < updates; i++) m.Update(); }

    // ---- set-up ----

    [Fact]
    public void Base_position_hp_and_who_gets_her()
    {
        Assert.Equal((256, 120), (Mima.BaseLeft, Mima.BaseTop));
        Assert.Equal(12, Fight().m.Hp);
        var jigoku = new GameWorld(new GameOptions { Stage = Stage(1) }, NullSoundSink.Instance);
        var makai = new GameWorld(new GameOptions { Stage = Stage(0) }, NullSoundSink.Instance);
        Assert.IsType<Mima>(jigoku.Boss);
        Assert.IsType<YuugenMagan>(makai.Boss);     // the Makai boss
        Assert.Null(new GameWorld(new GameOptions { Stage = Stage(1, 8) }, NullSoundSink.Instance).Boss);
    }

    [Fact]
    public void Entrance_lasts_101_ticks_updates_missiles_and_particles_once_and_draws_the_hop_direction()
    {
        var w = new GameWorld(new GameOptions { FrameRand = 4, Stage = Stage() }, NullSoundSink.Instance);
        var m = (Mima)w.Boss!;
        w.Rng = new IRand(4);
        var probe = new IRand(4);
        int direction = probe.Next() % 2;
        int ticks = 0;
        while (!m.EntranceFrame() && ticks < 500) ticks++;
        Assert.Equal(100, ticks);                   // the 101st call is the last one
        Assert.Equal(1, m.Phase);
        Assert.Equal(0, m.PhaseFrame);
        Assert.Equal((Mima.BaseLeft, Mima.BaseTop), (m.Left, m.Top));
        Assert.Equal(probe.Seed, w.Rng.Seed);        // exactly one draw (the particle stream drew nothing on its first frame)
        // the direction shows in where the first hop lands
        m.PatternCur = 3;
        Run(m, 52 + 4);
        Assert.Equal(direction == 0 ? 0 : 512, m.Left);
    }

    // ---- being hit ----

    [Theory]
    [InlineData(8, 8, true)]
    [InlineData(88, 120, true)]
    [InlineData(7, 8, false)]
    [InlineData(89, 8, false)]
    [InlineData(8, 7, false)]
    [InlineData(8, 121, false)]
    public void Orb_hitbox_and_response(int dx, int dy, bool hit)
    {
        var (w, m) = Fight();
        w.PlayerInvincible = false;
        w.Orb.PrevLeft = m.Left + dx; w.Orb.PrevTop = m.Top + dy;
        w.Orb.VelocityX = OrbVelocityX.Left4;
        m.Update();
        Assert.Equal(hit ? 11 : 12, m.Hp);
        Assert.Equal(hit ? 5000u : 0u, w.Score);
        Assert.Equal(hit ? OrbVelocityX.Right4 : OrbVelocityX.Left4, w.Orb.VelocityX);
    }

    [Fact]
    public void Shots_are_absorbed_in_the_right_hand_box_and_invincibility_lasts_40_frames()
    {
        var (w, m) = Fight();
        w.Shots.Add(m.Left + 16 + 96, m.Top + 48 + 48);   // far corner of the 96 x 48 test box
        m.Update();
        Assert.Equal(12, m.Hp);
        Assert.False(w.Shots.Moving[0]);
        var (v, n) = Fight();
        v.PlayerInvincible = false;
        v.Orb.PrevLeft = n.Left + 20; v.Orb.PrevTop = n.Top + 20;
        n.Update();
        for (int i = 0; i < 40; i++) { n.Update(); Assert.True(n.Invincible); }
        n.Update();
        Assert.False(n.Invincible);
        Assert.Equal(11, n.Hp);
    }

    [Fact]
    public void Missiles_and_particles_run_inside_every_boss_update()
    {
        var (w, m) = Fight();
        w.Missiles.Add(100, 100, 32, 0);
        m.Update();
        Assert.Equal(100 * 16 + 32, w.Missiles.CurLeft[0]);
        for (int i = 0; i < 6; i++) m.Update();
        Assert.Contains(true, Enumerable.Range(0, ParticleField.Count).Select(w.Particles.Alive));
    }

    // ---- phase 1 ----

    [Fact]
    public void Pattern_1_aims_at_the_player_first_then_fires_static_rings_and_ends_at_360()
    {
        var (w, m) = Fight();
        Run(m, 183);
        Assert.Equal(0, w.Pellets.Alive);              // the square is still growing: radius 32 -> 112 takes until frame 176
        Run(m, 1);                                     // frame 184: first aimed volley, previous corners at radius 112, angle 0x78
        Assert.Equal(4, w.Pellets.Alive);
        int cx = 320, cy = 200;
        var c0 = (AngleMath.PolarX(cx, 112, 0x78), AngleMath.PolarY(cy, 112, 0x78));
        int i0 = Array.FindIndex(w.Pellets.Moving, p => p);
        Assert.Equal((c0.Item1 << 4, c0.Item2 << 4), (w.Pellets.CurLeft[i0], w.Pellets.CurTop[i0]));
        int speed = 72 / 2; speed += 6; speed += -4 * speed / 40;     // half speed, tuned for Normal
        int a = AngleMath.Iatan2(384 - c0.Item2, 316 - c0.Item1);
        Assert.Equal((AngleMath.PolarX(0, speed, a), AngleMath.PolarY(0, speed, a)), (w.Pellets.VelX[i0], w.Pellets.VelY[i0]));
        Run(m, 280 - 184);                             // up to frame 280: 13 aimed volleys, 4 pellets each
        Assert.Equal(13 * 4, w.Pellets.Alive);
        Run(m, 360 - 280);                             // 288..360: 10 static volleys
        Assert.Equal(13 * 4 + 10 * 4, w.Pellets.Alive);
        Run(m, 1);                                     // frame 361 > 360
        Assert.Equal(0, m.PhaseFrame);
        Assert.Equal(0, m.SquareFlag);
        Assert.Equal(1, m.PatternCur);
    }

    [Fact]
    public void Static_volley_fires_along_the_centre_to_corner_direction_at_full_speed()
    {
        var (w, m) = Fight();
        Run(m, 288);
        int idx = Enumerable.Range(0, Pellets.Count).Where(i => w.Pellets.Moving[i]).Last();
        int px = w.Pellets.CurLeft[idx] >> 4, py = w.Pellets.CurTop[idx] >> 4;
        int a = AngleMath.Iatan2(py - 200, px - 320);
        int speed = 72 + 6 + (-4 * 78) / 40;           // 70
        Assert.Equal((AngleMath.PolarX(0, speed, a), AngleMath.PolarY(0, speed, a)), (w.Pellets.VelX[idx], w.Pellets.VelY[idx]));
    }

    [Fact]
    public void Pattern_2_missiles_aim_at_the_position_remembered_at_frame_224_with_whole_pixel_speeds()
    {
        var (w, m) = Fight(pattern: 1);
        w.Player.Left = 100;
        Run(m, 224);                                    // the target is taken during frame 224
        w.Player.Left = 500;                            // later movement is ignored
        Run(m, 247 - 224);
        Assert.Equal(0, w.Missiles.Alive);              // nothing before frame 248
        Run(m, 1);                                      // 248
        Assert.Equal(4, w.Missiles.Alive);
        int a = AngleMath.Iatan2(384 - 200, 100 + 16 - 8 - 320);
        int vx = AngleMath.PolarX(0, 104, a), vy = AngleMath.PolarY(0, 104, a);
        Assert.Equal(((vx >> 4) << 4, (vy >> 4) << 4), (w.Missiles.VelX[0], w.Missiles.VelY[0]));
        Assert.Equal(0, w.Missiles.VelX[0] % 16);
        Run(m, 8);                                      // second volley at 256
        Assert.Equal(8, w.Missiles.Alive);
        Run(m, 320 - 256 + 1);                          // frame 321 > 320
        Assert.Equal(0, m.PhaseFrame);
        Assert.Equal(2, m.PatternCur);
    }

    [Fact]
    public void Pattern_3_uses_two_squares_and_spawns_pellets_inside_clouds()
    {
        var (w, m) = Fight(pattern: 2);
        Run(m, 184);
        Assert.Equal(3, m.SquareFlag);
        Assert.Equal(8, w.Pellets.Alive);               // first volley: 4 corners of each square
        Assert.False(w.Pellets.SpawnWithCloud);         // switched off again at the end of the step
        Assert.Equal(8, Enumerable.Range(0, Pellets.Count).Count(i => w.Pellets.CloudFrame[i] != 0));
        Assert.Equal(112, m.SquareRadius);
        // the flipped square's corners use the mirrored angle
        Assert.Equal((AngleMath.PolarX(320, 112, (0 - m.SquareAngle) & 0xFF), AngleMath.PolarY(200, 112, (0 - m.SquareAngle) & 0xFF)), m.SquareCorner(1, 0));
        Run(m, 320 - 184);
        Assert.Equal(100, w.Pellets.Alive);             // the pool is full long before the pattern ends
        Run(m, 1);
        Assert.Equal(0, m.PhaseFrame);
        Assert.Equal(3, m.PatternCur);
        Assert.True(m.MeteorActive);
    }

    [Theory]
    [InlineData(0, 0)]
    [InlineData(1, 512)]
    public void Pattern_4_hops_four_times_and_leaves_her_where_the_last_hop_put_her(int direction, int firstLeft)
    {
        uint seed = Enumerable.Range(1, 200).Select(s => (uint)s).First(s => new IRand(s).Next() % 2 == direction);
        var w2 = new GameWorld(new GameOptions { FrameRand = seed, Stage = Stage(), Rank = Rank.Normal }, NullSoundSink.Instance);
        w2.Rng = new IRand(seed);
        var m2 = (Mima)w2.Boss!;
        w2.PlayerInvincible = true;
        w2.Orb.Left = 0; w2.Orb.Top = 380; w2.Orb.PrevLeft = 0; w2.Orb.PrevTop = 380;
        while (!m2.EntranceFrame()) { }
        m2.PatternCur = 3;
        Run(m2, 52);                                    // vanish: 52 frames
        Assert.Equal(1, m2.PhaseFrame);                 // hop 1 starts at frame 1
        Run(m2, 3);                                     // frame 4: teleport
        Assert.Equal(firstLeft, m2.Left);
        Assert.Equal(Mima.BaseTop - 24, m2.Top);
        Assert.True(m2.HitboxOrbInactive);              // frames < 12
        Run(m2, 8);                                     // frame 12
        Assert.False(m2.HitboxOrbInactive);
        Run(m2, 20);                                    // frame 32: spread-in done, ring of 8 chase pellets
        Assert.Equal(8, w2.Pellets.Alive);
        int i0 = Array.FindIndex(w2.Pellets.Moving, p => p);
        Assert.Equal(PelletMotion.Chase, w2.Pellets.Motion[i0]);
        Assert.Equal(35, w2.Pellets.Speed[i0]);
        Assert.Equal(((firstLeft + 64) << 4, (Mima.BaseTop - 24 + 80) << 4), (w2.Pellets.CurLeft[i0], w2.Pellets.CurTop[i0]));
        Run(m2, 3 * 32);                                // hops 2..4
        Assert.Equal(32, w2.Pellets.Alive);
        Assert.Equal(0, m2.PatternCur);                 // 3 -> 0: the pattern is over
        Assert.Equal(direction == 0 ? 384 : 128, m2.Left);
        Assert.False(m2.HitboxOrbInactive);
    }

    // ---- phase change ----

    [Fact]
    public void Phase_2_starts_after_the_pattern_ends_with_hp_at_6_and_gives_way_to_phase_3_after_52_frames()
    {
        var (w, m) = Fight();
        m.Hp = 6;
        m.Left = 40; m.Top = 70;                         // somewhere else, e.g. after a hop
        m.PhaseFrame = 360;
        m.SquareFlag = 1;
        m.Update();                                      // pattern 1 ends -> pattern 99 -> phase 2 at once
        Assert.Equal(2, m.Phase);
        Assert.Equal((Mima.BaseLeft, Mima.BaseTop), (m.Left, m.Top));
        Assert.Equal(0, m.PhaseFrame);
        Run(m, 51);
        Assert.Equal(2, m.Phase);
        Run(m, 1);
        Assert.Equal(3, m.Phase);
        Assert.Equal(0, m.PatternCur);
    }

    [Fact]
    public void Phase_2_waits_for_the_invincibility_to_end_and_deals_no_damage()
    {
        var (w, m) = Fight();
        m.Hp = 6; m.Invincible = true; m.InvincibilityFrame = 10;
        m.PhaseFrame = 360; m.SquareFlag = 1;
        m.Update();
        Assert.Equal(1, m.Phase);                        // pattern 99, but still invincible
        Assert.Equal(99, m.PatternCur);
        Run(m, 31);
        Assert.Equal(2, m.Phase);
        w.PlayerInvincible = false;
        w.Orb.PrevLeft = m.Left + 20; w.Orb.PrevTop = m.Top + 20;
        Run(m, 5);
        Assert.Equal(6, m.Hp);
    }

    // ---- phase 3 ----

    [Fact]
    public void Pillars_draw_eight_positions_in_the_original_order()
    {
        var (w, m) = Fight(phase: 3, pattern: 0);
        Run(m, 99);
        uint before = w.Rng.Seed;
        m.Update();                                      // frame 100
        // The ambient particles run first inside the same update and may draw two numbers; the pillars draw 8 after.
        bool matched = false;
        foreach (int skip in new[] { 0, 2 })
        {
            var p = new IRand(before);
            for (int k = 0; k < skip; k++) p.Next();
            int r(int n) => p.Next() % n;
            int[] expected =
            {
                16 + r(48), 640 - 32 - r(48) + 16, 64 + r(224), 336 + r(224), 128 + r(160), 336 + r(160), 64 + r(224), 336 + r(224),
            };
            if (p.Seed != w.Rng.Seed) continue;
            for (int i = 0; i < 8; i++) Assert.Equal((128, expected[i]), m.Pillar(i));
            matched = true;
        }
        Assert.True(matched);
    }

    [Theory]
    [InlineData(-39, true)]
    [InlineData(-40, false)]     // strict: player_left > centre - 40
    [InlineData(7, true)]
    [InlineData(8, false)]       // strict: player_left < centre + 8
    public void Pillar_hits_the_player_in_a_narrow_window_at_its_two_standing_visits(int dx, bool hit)
    {
        var (w, m) = Fight(phase: 3, pattern: 0);
        w.PlayerInvincible = false;
        Run(m, 100);
        int cx = m.Pillar(0).centerX;
        w.Player.Left = cx + dx;
        Run(m, 191 - 100);                               // up to frame 191: still circles
        Assert.False(w.PlayerIsHit);
        Run(m, 1);                                       // frame 192: the pillar stands for the first time (time 32)
        Assert.Equal(hit, w.PlayerIsHit);
        w.PlayerIsHit = false;
        Run(m, 8);                                       // frame 200: second standing visit (time 24)
        Assert.Equal(hit, w.PlayerIsHit);
        w.PlayerIsHit = false;
        Run(m, 8);                                       // frame 208: it vanishes, no more hits
        Assert.False(w.PlayerIsHit);
    }

    [Fact]
    public void A_pillar_ends_with_an_aimed_three_way_spread_from_her_centre_and_the_pattern_ends_with_the_last_pillar()
    {
        var (w, m) = Fight(phase: 3, pattern: 0);
        Run(m, 207);
        Assert.Equal(0, w.Pellets.Alive);
        Run(m, 1);                                       // frame 208: pillar 0 is done
        Assert.Equal(3, w.Pellets.Alive);
        Assert.Equal(-999, m.Pillar(0).time);
        int i0 = Array.FindIndex(w.Pellets.Moving, q => q);
        Assert.Equal((320 << 4, 200 << 4), (w.Pellets.CurLeft[i0], w.Pellets.CurTop[i0]));
        Run(m, 350 - 208);
        Assert.Equal(3 * 7, w.Pellets.Alive);
        Assert.NotEqual(0, m.PhaseFrame);
        Run(m, 1);                                       // frame 351: pillar 7 is done, all done
        Assert.Equal(0, m.PhaseFrame);
        Assert.Equal(24, w.Pellets.Alive);
        Assert.Equal(1, m.PatternCur);
    }

    [Fact]
    public void Half_circle_missiles_turn_by_0D_every_volley_and_use_whole_pixel_speeds()
    {
        var (w, m) = Fight(phase: 3, pattern: 1);
        Run(m, 183);
        Assert.Equal(0, w.Missiles.Alive);
        Run(m, 1);                                       // frame 184: (184 % 16 == 8) first volley
        Assert.Equal(4, w.Missiles.Alive);
        Assert.Equal((4 * 16, 0), (w.Missiles.VelX[0], w.Missiles.VelY[0]));   // 4.5 px chopped to 4, angle 0
        Run(m, 16);
        int second = Enumerable.Range(0, Missiles.Count).First(i => w.Missiles.Flag[i] != 0 && i >= 4);
        Assert.Equal((AngleMath.PolarX(0, 4, 0x0D) << 4, AngleMath.PolarY(0, 4, 0x0D) << 4), (w.Missiles.VelX[second], w.Missiles.VelY[second]));
        Run(m, 341 - 200);                               // frame 341 > 340
        Assert.Equal(0, m.PhaseFrame);
        Assert.Equal(2, m.PatternCur);
    }

    [Fact]
    public void Slow_spray_fires_a_ring_of_four_per_step_from_frame_184_and_ends_at_371()
    {
        var (w, m) = Fight(phase: 3, pattern: 2);
        Run(m, 183);
        Assert.Equal(0, w.Pellets.Alive);
        Run(m, 1);
        Assert.Equal(4, w.Pellets.Alive);
        int i0 = Array.FindIndex(w.Pellets.Moving, q => q);
        int speed = 40 + 6 + (-4 * 46) / 40;             // 42
        Assert.Equal((AngleMath.PolarX(0, speed, 0x74), AngleMath.PolarY(0, speed, 0x74)), (w.Pellets.VelX[i0], w.Pellets.VelY[i0]));
        Run(m, 368 - 184);
        Assert.Equal(24 * 4, w.Pellets.Alive);           // frames 184..368
        Run(m, 3);
        Assert.Equal(0, m.PhaseFrame);
        Assert.Equal(3, m.PatternCur);
    }

    [Fact]
    public void Aimed_lasers_spawn_one_per_frame_from_the_matching_corner_and_ignore_busy_slots()
    {
        var (w, m) = Fight(phase: 3, pattern: 3);
        Run(m, 180);
        Assert.DoesNotContain(true, w.Lasers.Slots.Select(l => l.Alive));
        Run(m, 1);                                       // frame 181: slot 1
        Assert.True(w.Lasers[1].Alive);
        Assert.False(w.Lasers[0].Alive);
        Assert.Equal(m.SquareCorner(0, 1).x << 8, w.Lasers[1].OriginLeft);
        Assert.Equal(54 / 8, w.Lasers[1].RayExtendSpeed);
        Assert.Equal(20, w.Lasers[1].MoveoutAtAge);
        Assert.Equal(3, w.Lasers[1].WidthCel);
        Run(m, 3);
        Assert.Equal(4, w.Lasers.Slots.Count(l => l.Alive));
        int age = w.Lasers[1].Age;
        Run(m, 1);                                       // slot 1 again: still alive, Spawn does nothing
        Assert.Equal(age, w.Lasers[1].Age);              // (UpdateAll is the session's job, not the boss's)
        Run(m, 300 - 185);
        Run(m, 1);
        Assert.Equal(0, m.PhaseFrame);
        Assert.Equal(0, m.PatternCur);                   // 3 -> 0
    }

    [Fact]
    public void Last_hp_clears_the_screen_of_bullets_and_flags_the_defeat()
    {
        var (w, m) = Fight(phase: 3, pattern: 1);
        w.PlayerInvincible = false;
        w.Pellets.AddGroup(200, 200, PelletGroup.One, 40);
        w.Missiles.Add(100, 100, 0, 0);
        w.Lasers[2].Spawn(10, 100, 10, 200, 40, 5, 20, 1);
        m.Hp = 1;
        w.Orb.PrevLeft = m.Left + 30; w.Orb.PrevTop = m.Top + 30;
        m.Update();
        Assert.Equal(0, m.Hp);
        Assert.True(m.Defeated);
        Assert.Equal(0, w.Pellets.Alive);
        Assert.Equal(0, w.Missiles.Alive);
        Assert.False(w.Lasers[2].Alive);
    }

    // ---- session flow ----

    static GameSession Started(int lives = 3)
    {
        var g = new GameSession(new GameOptions { FrameRand = 12, Stage = Stage(), Lives = lives, Bombs = 1, Rank = Rank.Hard });
        Assert.Equal(SessionPhase.BossEntrance, g.Phase);
        for (int i = 0; i < 101; i++) g.Step(GameButtons.None);
        Assert.Equal(SessionPhase.Playing, g.Phase);
        return g;
    }

    [Fact]
    public void Defeat_goes_straight_to_the_scene_bonus_without_a_route_menu()
    {
        var g = Started();
        var m = (Mima)g.World.Boss!;
        m.Phase = 3; m.Hp = 1; m.PatternCur = 1; m.Invincible = false;
        g.World.Orb.PrevLeft = m.Left + 30; g.World.Orb.PrevTop = m.Top + 30;
        g.Step(GameButtons.None);
        Assert.True(m.Defeated);
        g.Step(GameButtons.None);
        Assert.Equal(SessionPhase.BossDefeat, g.Phase);
        int guard = 0;
        while (g.Phase == SessionPhase.BossDefeat && guard++ < 2000) g.Step(GameButtons.None);
        Assert.Equal(SessionPhase.StageBonus, g.Phase);
        var b = g.Bonus!.Value;
        Assert.Equal(StageBonus.ComputeTotle(10, g.World.Timer.Value, g.World.CardComboMax, g.World.RemLives, g.World.RemBombs), b);
        StageTests.Dismiss(g);
        Assert.Equal(SessionOutcome.Cleared, g.Outcome);
        Assert.Equal(2000, StageTimer.InitialFor(9, 1));         // Jigoku stage 10 timer
    }

    static (ulong hash, int maxPhase, int maxPellets, int maxMissiles) Battle(uint seed, Rank rank)
    {
        var g = new GameSession(new GameOptions { FrameRand = seed, Stage = Stage(), Rank = rank, Lives = 1000, Bombs = 2 });
        uint x = 0xFACEB00C;
        GameButtons cur = GameButtons.None;
        int maxPhase = 0, maxP = 0, maxM = 0;
        for (int i = 0; i < 14000; i++)
        {
            x ^= x << 13; x ^= x >> 17; x ^= x << 5;
            if (i % 6 == 0)
            {
                cur = GameButtons.None;
                if ((x & 1) != 0) cur |= GameButtons.Left;
                if ((x & 2) != 0) cur |= GameButtons.Right;
                if ((x & 4) != 0) cur |= GameButtons.Shot;
            }
            if (i % 170 == 169 && g.Phase == SessionPhase.Playing && g.World.Boss is { } b && b.Phase is 1 or 3)
            {
                g.World.Orb.Left = b.Left + 40; g.World.Orb.Top = b.Top + 40;
            }
            g.Step(cur);
            if (g.World.Boss != null) maxPhase = Math.Max(maxPhase, g.World.Boss.Phase);
            maxP = Math.Max(maxP, g.World.Pellets.Alive);
            maxM = Math.Max(maxM, g.World.Missiles.Alive);
        }
        return (g.StateHash(), maxPhase, maxP, maxM);
    }

    [Theory]
    [InlineData(Rank.Normal)]
    [InlineData(Rank.Lunatic)]
    public void A_whole_fight_is_deterministic_and_reaches_the_third_phase(Rank rank)
    {
        var a = Battle(21, rank);
        Assert.Equal(a, Battle(21, rank));
        Assert.True(a.maxPhase >= 3);
        Assert.True(a.maxPellets > 10);
        Assert.NotEqual(a.hash, Battle(22, rank).hash);
    }

    // ---- rendering ----

    sealed class Art : IGameArt
    {
        public int FrameCount(string sheet) => 0;
        public bool TryGetFrame(string sheet, int frame, out ArtFrame art)
        {
            if (sheet == "BOSS3_1.BOS" && frame == 0)
            {
                art = new ArtFrame(128, 160, Enumerable.Repeat((byte)7, 128 * 160).ToArray(), Array.Empty<byte>(), Array.Empty<byte>());
                return true;
            }
            art = default;
            return false;
        }
        public bool TryGetBackground(out ArtFrame art) { art = default; return false; }
    }

    [Fact]
    public void Mima_is_drawn_from_her_sheet_or_as_a_box_and_art_does_not_change_the_hash()
    {
        var plain = new GameSession(new GameOptions { FrameRand = 3, Stage = Stage() });
        var arted = new GameSession(new GameOptions { FrameRand = 3, Stage = Stage() }, null, new Art());
        for (int i = 0; i < 130; i++) { plain.Step(GameButtons.None); arted.Step(GameButtons.None); }
        Assert.Equal(plain.StateHash(), arted.StateHash());
        var a = new Framebuffer(); var b = new Framebuffer();
        arted.Render(a); plain.Render(b);
        var m = arted.World.Boss!;
        Assert.Equal(7, a.GetPixel(m.Left + 20, m.Top + 20));
        Assert.Equal(12, b.GetPixel(m.Left + 20, m.Top + 20));
    }
}
