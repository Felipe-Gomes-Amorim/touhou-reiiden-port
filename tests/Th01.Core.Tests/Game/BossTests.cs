using Th01.Core.Audio;
using Th01.Core.Game;
using Th01.Core.Graphics;
using Th01.Core.Input;
using Th01.Core.Rng;
using Th01.Core.Sim;

namespace Th01.Core.Tests.Game;

public class BossTests
{
    static StageSetup BossStage(int id = 4) => new()
    {
        StageId = id,
        Cards = new bool[StageSetup.Rows, StageSetup.Cols],
        Obstacles = new byte[StageSetup.Rows, StageSetup.Cols],
    };

    static GameWorld World(Rank rank = Rank.Normal, uint seed = 5)
    {
        var w = new GameWorld(new GameOptions { FrameRand = seed, Stage = BossStage(), Rank = rank, Lives = 3, Bombs = 1 }, NullSoundSink.Instance);
        w.Boss!.Phase = 1;
        w.Boss.PhaseFrame = 0;
        w.Orb.Left = 0; w.Orb.Top = 380; w.Orb.PrevLeft = 0; w.Orb.PrevTop = 380;   // far from the boss
        w.Player.Left = 304;
        return w;
    }

    static void Run(GameWorld w, int updates) { for (int i = 0; i < updates; i++) w.Boss!.Update(); }

    static int PelletsOf(GameWorld w, PelletMotion m) =>
        Enumerable.Range(0, Pellets.Count).Count(i => w.Pellets.Moving[i] && w.Pellets.Motion[i] == m);

    // ---- constants and set-up ----

    [Fact]
    public void Base_position_and_hp()
    {
        Assert.Equal(272, SinGyoku.BaseLeft);
        Assert.Equal(96, SinGyoku.BaseTop);
        Assert.Equal(8, World().Boss!.Hp);
    }

    [Fact]
    public void Only_stage_5_gets_this_boss()
    {
        Assert.NotNull(World().Boss);
        Assert.Null(new GameWorld(new GameOptions { Stage = BossStage(9) }, NullSoundSink.Instance).Boss);
        Assert.Null(new GameWorld(new GameOptions { Stage = BossStage(3) }, NullSoundSink.Instance).Boss);
    }

    [Fact]
    public void Entrance_runs_200_frames_then_play_starts_without_waiting_for_a_shot()
    {
        var g = new GameSession(new GameOptions { FrameRand = 3, Stage = BossStage() });
        Assert.Equal(SessionPhase.BossEntrance, g.Phase);
        for (int i = 0; i < 199; i++) { g.Step(GameButtons.Shot); Assert.Equal(SessionPhase.BossEntrance, g.Phase); }
        g.Step(GameButtons.None);
        Assert.Equal(SessionPhase.Playing, g.Phase);
        var b = g.World.Boss!;
        Assert.Equal(1, b.Phase);
        Assert.Equal(0, b.PhaseFrame);
        Assert.Equal((272, 96), (b.Left, b.Top));
        // The sphere spins up: rotation every 18, 16, 14, ... frames (reference written independently)
        int image = 0, interval = 18;
        for (int f = 1; f <= 200; f++)
            if (f % interval == 0) { image = (image + 1) % 8; interval = Math.Max(1, interval - 2); }
        Assert.Equal(image, b.SphereImage);
    }

    // ---- being hit ----

    static void PlaceOrbOnBoss(GameWorld w)
    {
        w.Orb.PrevLeft = w.Boss!.Left + 32; w.Orb.PrevTop = w.Boss.Top + 32;
    }

    [Fact]
    public void Orb_hit_costs_one_hp_and_scores_3000()
    {
        var w = World();
        PlaceOrbOnBoss(w);
        w.Boss!.Update();
        Assert.Equal(7, w.Boss.Hp);
        Assert.True(w.Boss.Invincible);
        Assert.Equal(3000u, w.Score);
    }

    [Theory]
    [InlineData(OrbVelocityX.Left4, OrbVelocityX.Right4)]
    [InlineData(OrbVelocityX.Right4, OrbVelocityX.Left4)]
    [InlineData(OrbVelocityX.Left8, OrbVelocityX.Right4)]
    [InlineData(OrbVelocityX.Right8, OrbVelocityX.Left4)]
    public void Orb_bounces_away_after_hitting_the_boss(OrbVelocityX before, OrbVelocityX after)
    {
        // IsMovingLeft/Right cover the 4 and 8 px/tick velocities alike.
        var w = World();
        w.Orb.VelocityX = before;
        PlaceOrbOnBoss(w);
        w.Boss!.Update();
        Assert.Equal(after, w.Orb.VelocityX);
    }

    [Fact]
    public void Standing_orb_is_sent_away_from_the_nearest_side()
    {
        var w = World();
        PlaceOrbOnBoss(w);
        w.Orb.Left = 100; w.Orb.VelocityX = OrbVelocityX.Zero;
        w.Boss!.Update();
        Assert.Equal(OrbVelocityX.Right4, w.Orb.VelocityX);
        var v = World();
        PlaceOrbOnBoss(v);
        v.Orb.Left = 400; v.Orb.VelocityX = OrbVelocityX.Zero;
        v.Boss!.Update();
        Assert.Equal(OrbVelocityX.Left4, v.Orb.VelocityX);
    }

    [Fact]
    public void Invincibility_lasts_through_frame_40_and_ends_on_41()
    {
        var w = World();
        PlaceOrbOnBoss(w);
        w.Boss!.Update();                                  // the hit resets the counter to 0
        Assert.Equal(0, w.Boss.InvincibilityFrame);
        for (int i = 1; i <= 40; i++)
        {
            w.Boss.Update();
            Assert.True(w.Boss.Invincible, $"frame {i}");
            Assert.Equal(7, w.Boss.Hp);
        }
        w.Boss.Update();                                   // 41: reset
        Assert.False(w.Boss.Invincible);
        Assert.Equal(0, w.Boss.InvincibilityFrame);
        w.Boss.Update();                                   // can be hit again
        Assert.Equal(6, w.Boss.Hp);
    }

    [Theory]
    [InlineData(0, true)]
    [InlineData(64, true)]
    [InlineData(65, false)]
    [InlineData(-1, false)]
    public void Orb_hitbox_is_the_boss_square_of_64_px_using_the_previous_frame_position(int dx, bool hit)
    {
        var w = World();
        w.Orb.PrevLeft = w.Boss!.Left + dx; w.Orb.PrevTop = w.Boss.Top + 10;
        w.Boss.Update();
        Assert.Equal(hit ? 7 : 8, w.Boss.Hp);
    }

    [Fact]
    public void Shots_are_absorbed_without_damage()
    {
        var w = World();
        w.Shots.Add(w.Boss!.Left + 30, w.Boss.Top + 50);
        w.Boss.Update();
        Assert.Equal(8, w.Boss.Hp);
        Assert.False(w.Shots.Moving[0]);
        Assert.Equal(1, w.Shots.DecayFrame[0]);
    }

    [Fact]
    public void Bomb_damages_only_on_the_last_life_and_every_50th_frame()
    {
        var w = World();
        w.RemLives = 0; w.Bomb.Damaging = true; w.FrameRand = 100;
        w.Boss!.Update();
        Assert.Equal(7, w.Boss.Hp);
        var v = World();
        v.RemLives = 1; v.Bomb.Damaging = true; v.FrameRand = 100;
        v.Boss!.Update();
        Assert.Equal(8, v.Boss.Hp);
        var u = World();
        u.RemLives = 0; u.Bomb.Damaging = true; u.FrameRand = 101;
        u.Boss!.Update();
        Assert.Equal(8, u.Boss.Hp);
    }

    // ---- phase 1: half-circle spray ----

    [Theory]
    [InlineData(Rank.Easy, 10)]
    [InlineData(Rank.Normal, 15)]
    [InlineData(Rank.Hard, 20)]
    [InlineData(Rank.Lunatic, 30)]
    public void Spray_fires_the_ranked_number_of_pellets_between_frames_100_and_160(Rank rank, int count)
    {
        var w = World(rank);
        Run(w, 99);
        Assert.Equal(0, w.Pellets.Alive);
        Run(w, 60);                                       // frames 100..159
        Assert.Equal(count, w.Pellets.Alive);
        Assert.Equal(count, PelletsOf(w, PelletMotion.Regular));
        Run(w, 1);                                        // frame 160: the pattern ends
        Assert.Equal(0, w.Boss!.PhaseFrame);
        Assert.Equal(1, w.Boss.PatternCur);               // next is the slam
    }

    [Fact]
    public void Spray_direction_comes_from_one_random_draw_at_frame_10_and_sets_the_first_angle()
    {
        foreach (uint seed in new uint[] { 1, 2, 3, 4, 5, 6, 7, 8 })
        {
            var w = World(Rank.Normal, seed);
            w.Rng = new IRand(seed);
            var probe = new IRand(seed);
            int direction = probe.Next() % 2 == 1 ? 1 : -1;
            Run(w, 9);
            Assert.Equal(seed, w.Rng.Seed);                // nothing drawn before frame 10
            Run(w, 1);
            Assert.Equal(probe.Seed, w.Rng.Seed);          // exactly one draw
            Run(w, 90);                                    // frame 100: the first of 15 pellets
            // frame 100 fired the first pellet (15 pellets: interval 4): angle 0 for -1, 0x80 for +1; speed 50+6-5 = 51
            int first = Enumerable.Range(0, Pellets.Count).First(i => w.Pellets.Moving[i]);
            Assert.Equal(direction == -1 ? 51 : -51, w.Pellets.VelX[first]);
            Assert.Equal(0, w.Pellets.VelY[first]);
            Assert.Equal(1, w.Pellets.Alive);
        }
    }

    [Fact]
    public void Spray_angle_step_is_a_truncated_integer_division()
    {
        var w = World();
        // find a seed that gives direction -1 (angle starts at 0, steps by +128/15 = 8)
        uint seed = Enumerable.Range(1, 50).Select(s => (uint)s).First(s => new IRand(s).Next() % 2 == 0);
        w.Rng = new IRand(seed);
        Run(w, 105);                                       // fired at 100 and 104
        var idx = Enumerable.Range(0, Pellets.Count).Where(i => w.Pellets.Moving[i]).ToArray();
        Assert.Equal(2, idx.Length);
        // second pellet angle 8: vx = 51 * Cos8(8) >> 8, vy = 51 * Sin8(8) >> 8 (4 ticks of pellet movement not applied: no pellet update ran)
        Assert.Equal(AngleMath.PolarX(0, 51, 8), w.Pellets.VelX[idx[1]]);
        Assert.Equal(AngleMath.PolarY(0, 51, 8), w.Pellets.VelY[idx[1]]);
    }

    // ---- phase 1: slam ----

    [Fact]
    public void Slam_dives_at_the_player_rises_back_and_hands_over_to_the_spray()
    {
        var w = World();
        w.PlayerInvincible = true;
        w.Boss!.PatternCur = 1;
        Run(w, 99);
        Assert.Equal(96, w.Boss.Top);
        Run(w, 1);                                         // frame 100: aims (player straight below: 4 px/frame down) and moves
        Assert.Equal(100, w.Boss.PhaseFrame);
        Assert.Equal(100, w.Boss.Top);
        Assert.Equal(272, w.Boss.Left);
        Run(w, 52);                                        // frame 152: Top 96 + 4 * 53 = 308 > 304
        Assert.Equal(308, w.Boss.Top);
        Run(w, 54);                                        // 54 frames up at 4 px: 92 < 96
        Assert.Equal(92, w.Boss.Top);
        Assert.Equal(206, w.Boss.PhaseFrame);
        Run(w, 1);
        Assert.Equal(0, w.Boss.PhaseFrame);                // pattern over
        Assert.Equal(0, w.Boss.PatternCur);                // 1 -> 0
    }

    [Fact]
    public void Slam_speed_by_rank_and_the_hitbox_on_the_player()
    {
        var w = World(Rank.Lunatic);
        w.Boss!.PatternCur = 1;
        Run(w, 100);
        Assert.Equal(96 + 6, w.Boss.Top);                  // 6 px/frame on Lunatic
        // Player under the sphere gets hit once the sphere reaches its box; an invincible player does not.
        var h = World();
        h.Boss!.PatternCur = 1;
        Run(h, 100 + 43);                                  // Top 96 + 4 * 44 = 272 = player_top - 96
        Assert.True(h.PlayerIsHit);
        var i = World();
        i.PlayerInvincible = true;
        i.Boss!.PatternCur = 1;
        Run(i, 100 + 60);
        Assert.False(i.PlayerIsHit);
    }

    [Fact]
    public void Slam_toward_a_player_on_the_left_moves_sideways_and_is_clamped_to_the_field()
    {
        var w = World();
        w.Player.Left = 0;
        w.PlayerInvincible = true;
        w.Boss!.PatternCur = 1;
        Run(w, 100 + 20);
        Assert.True(w.Boss.Left < 272);
        Assert.True(w.Boss.Left >= 0);
        Run(w, 40);
        Assert.True(w.Boss.Left >= 0 && w.Boss.Left <= 544);
    }

    // ---- phase change ----

    [Fact]
    public void Phase_2_starts_after_two_hits_unless_a_slam_is_in_progress()
    {
        var w = World();
        w.Boss!.Hp = 6; w.Boss.Invincible = false; w.Boss.PatternCur = 0;
        w.Boss.Update();
        Assert.Equal(2, w.Boss.Phase);
        Assert.Equal(0, w.Boss.PatternCur);
        Assert.Equal(0, w.Boss.PhaseFrame);
        var v = World();
        v.Boss!.Hp = 6; v.Boss.PatternCur = 1;
        v.Boss.Update();
        Assert.Equal(1, v.Boss.Phase);
        var u = World();
        u.Boss!.Hp = 6; u.Boss.Invincible = true;
        u.Boss.Update();
        Assert.Equal(1, u.Boss.Phase);                     // wait for the invincibility to end
    }

    // ---- phase 2 patterns ----

    static GameWorld Phase2(int pattern, Rank rank = Rank.Normal, uint seed = 9)
    {
        var w = World(rank, seed);
        w.Boss!.Phase = 2; w.Boss.PatternCur = pattern; w.Boss.Hp = 5; w.Boss.PhaseFrame = 0;
        w.Rng = new IRand(seed);
        return w;
    }

    [Fact]
    public void Chasing_pairs_fire_every_8_frames_from_144_to_216()
    {
        var w = Phase2(0);
        Run(w, 143);
        Assert.Equal(0, w.Pellets.Alive);
        Run(w, 1);                                         // frame 144
        Assert.Equal(2, w.Pellets.Alive);
        Run(w, 116);                                       // frame 260
        Assert.Equal(20, w.Pellets.Alive);                 // 144, 152, ..., 216 = 10 pairs
        Assert.Equal(20, PelletsOf(w, PelletMotion.Chase));
        int i = Enumerable.Range(0, Pellets.Count).First(k => w.Pellets.Moving[k]);
        Assert.Equal(58, w.Pellets.Speed[i]);              // to_sp(3.625), the chase speed is not tuned
        Assert.Equal(0, w.Boss!.PhaseFrame);               // cycle over
        Assert.Equal(4, w.Boss.PatternCur);                // non-slam -> slam, no random draw
        Assert.Equal(9u, w.Rng.Seed);
    }

    [Fact]
    public void Crossing_pairs_come_from_the_hands_at_angles_30_and_50()
    {
        var w = Phase2(2);
        Run(w, 144);
        Assert.Equal(2, w.Pellets.Alive);
        var idx = Enumerable.Range(0, Pellets.Count).Where(k => w.Pellets.Moving[k]).ToArray();
        int speed = 70 + 6 + (-4 * 76) / 40;               // 69
        Assert.Equal(272 + 16 - 4 << 4, w.Pellets.CurLeft[idx[0]]);
        Assert.Equal(272 + 96 - 16 + 4 << 4, w.Pellets.CurLeft[idx[1]]);
        Assert.Equal(144 - 4 << 4, w.Pellets.CurTop[idx[0]]);
        Assert.Equal(AngleMath.PolarX(0, speed, 0x30), w.Pellets.VelX[idx[0]]);
        Assert.Equal(AngleMath.PolarY(0, speed, 0x50), w.Pellets.VelY[idx[1]]);
        Run(w, 116);
        Assert.Equal(30, w.Pellets.Alive);                 // 144..256 every 8 = 15 pairs
    }

    [Fact]
    public void Random_downwards_fires_10_pellets_at_135_160_and_185_in_draw_order()
    {
        var w = Phase2(1);
        var probe = new IRand(9);
        Run(w, 134);
        Assert.Equal(0, w.Pellets.Alive);
        Run(w, 1);
        Assert.Equal(10, w.Pellets.Alive);
        int speed = 54 + 6 + (-4 * 60) / 40;               // 54
        var idx = Enumerable.Range(0, Pellets.Count).Where(k => w.Pellets.Moving[k]).ToArray();
        for (int n = 0; n < 10; n++)
        {
            int angle = probe.Next() % 0x80;
            Assert.Equal(AngleMath.PolarX(0, speed, angle), w.Pellets.VelX[idx[n]]);
            Assert.Equal(AngleMath.PolarY(0, speed, angle), w.Pellets.VelY[idx[n]]);
        }
        Run(w, 125);                                       // frame 260
        Assert.Equal(30, w.Pellets.Alive);
        Assert.Equal(4, w.Boss!.PatternCur);
    }

    [Fact]
    public void Random_sling_spawns_in_the_sphere_with_two_draws_per_pellet_and_the_sling_motion()
    {
        var w = Phase2(3);
        var probe = new IRand(9);
        Run(w, 135);
        Assert.Equal(10, w.Pellets.Alive);
        Assert.Equal(10, PelletsOf(w, PelletMotion.SlingAimed));
        var idx = Enumerable.Range(0, Pellets.Count).Where(k => w.Pellets.Moving[k]).ToArray();
        for (int n = 0; n < 10; n++)
        {
            int ox = probe.Next() % 96, oy = probe.Next() % 96;
            Assert.Equal(272 + ox << 4, w.Pellets.CurLeft[idx[n]]);
            Assert.Equal(96 + oy << 4, w.Pellets.CurTop[idx[n]]);
            Assert.Equal(64, w.Pellets.Speed[idx[n]]);     // to_sp(4)
        }
        Assert.Equal(probe.Seed, w.Rng.Seed);
    }

    [Fact]
    public void After_a_slam_the_next_pattern_is_random_with_one_draw()
    {
        var w = Phase2(4);
        w.PlayerInvincible = true;
        var probe = new IRand(9);
        int expected = probe.Next() % 4;
        int guard = 0;
        do { w.Boss!.Update(); } while (w.Boss.PhaseFrame != 0 && guard++ < 1000);
        Assert.Equal(expected, w.Boss!.PatternCur);
        Assert.Equal(probe.Seed, w.Rng.Seed);
    }

    [Fact]
    public void Last_hp_ends_the_fight()
    {
        var w = Phase2(0);
        w.Boss!.Hp = 1;
        PlaceOrbOnBoss(w);
        w.Boss.Update();
        Assert.Equal(0, w.Boss.Hp);
        Assert.Equal(8, w.Boss.Phase);
        Assert.True(w.Boss.Defeated);
    }

    // ---- sling and chase pellet motion ----

    [Fact]
    public void Sling_pellet_draws_a_direction_once_circles_and_then_aims_at_the_player()
    {
        var w = World();
        w.Pellets.AddSingle(300, 150, 0, 0, PelletMotion.SlingAimed, 64);
        int i = Array.FindIndex(w.Pellets.Moving, m => m);
        w.Rng = new IRand(77);
        int bit = new IRand(77).Next() & 1;
        w.Pellets.Update();
        Assert.Equal(bit + 1, w.Pellets.SlingDirection[i]);
        Assert.Equal(8, w.Pellets.Angle[i]);
        // clockwise: velocity = (64 * Cos(0), 64 * Sin(0)); counter-clockwise swaps the components
        Assert.Equal(bit == 0 ? (64, 0) : (0, 64), (w.Pellets.VelX[i], w.Pellets.VelY[i]));
        int guard = 0;
        while (w.Pellets.FromGroup[i] == PelletGroup.None && w.Pellets.Moving[i] && guard++ < 100) w.Pellets.Update();
        Assert.Equal(PelletGroup.OneAimed, w.Pellets.FromGroup[i]);   // after a full turn it flies at the player
        Assert.Equal(0, w.Pellets.Angle[i]);
        Assert.Equal(32, guard);                           // angle 8 after the first tick, 0x108 after 32 more
    }

    [Fact]
    public void Chase_pellet_steers_one_subpixel_per_tick_towards_the_player_velocity_only_above_y_320()
    {
        var w = World();
        w.Pellets.AddSingle(100, 100, 0x00 - 0x10, 16, PelletMotion.Chase, 58);
        int i = Array.FindIndex(w.Pellets.Moving, m => m);
        int vx = w.Pellets.VelX[i], vy = w.Pellets.VelY[i];
        w.Pellets.Update();
        Assert.True(Math.Abs(w.Pellets.VelX[i] - vx) <= 1 && Math.Abs(w.Pellets.VelY[i] - vy) <= 1);
        Assert.NotEqual((vx, vy), (w.Pellets.VelX[i], w.Pellets.VelY[i]));
        // below the limit it just keeps its velocity
        var v = World();
        v.Pellets.AddSingle(100, 330, 0x00 - 0x10, 16, PelletMotion.Chase, 58);
        int j = Array.FindIndex(v.Pellets.Moving, m => m);
        int ovx = v.Pellets.VelX[j], ovy = v.Pellets.VelY[j];
        v.Pellets.Update();
        Assert.Equal((ovx, ovy), (v.Pellets.VelX[j], v.Pellets.VelY[j]));
    }

    [Fact]
    public void Deflected_motion_pellets_become_regular()
    {
        var w = World();
        w.PlayerDeflecting = true;
        w.Pellets.AddSingle(290, 350, 0x40, 16, PelletMotion.Chase, 58);
        w.Pellets.Update();
        int i = Array.FindIndex(w.Pellets.Moving, m => m);
        Assert.Equal(PelletMotion.Regular, w.Pellets.Motion[i]);
    }

    // ---- session-level flow ----

    static GameSession Started(int lives = 3)
    {
        var g = new GameSession(new GameOptions { FrameRand = 12, Stage = BossStage(), Lives = lives, Bombs = 1, Rank = Rank.Hard });
        for (int i = 0; i < SinGyoku.EntranceFrames; i++) g.Step(GameButtons.None);
        Assert.Equal(SessionPhase.Playing, g.Phase);
        return g;
    }

    static void Defeat(GameSession g)
    {
        var b = g.World.Boss!;
        b.Phase = 2; b.Hp = 1; b.Invincible = false;
        g.World.Orb.Left = b.Left + 32; g.World.Orb.Top = b.Top + 32; g.World.Orb.Y.Force = 0; g.World.Orb.ForceFrame = 0;
        g.World.Orb.PrevLeft = g.World.Orb.Left; g.World.Orb.PrevTop = g.World.Orb.Top;
        g.Step(GameButtons.None);
    }

    [Fact]
    public void Defeat_leads_through_the_defeat_animation_to_the_route_menu_and_the_scene_bonus()
    {
        var g = Started();
        Defeat(g);
        Assert.True(g.World.Boss!.Defeated);
        g.Step(GameButtons.None);                          // next tick: the defeat animation starts
        Assert.Equal(SessionPhase.BossDefeat, g.Phase);
        int ticks = 0;
        while (g.Phase == SessionPhase.BossDefeat && ticks++ < 2000) g.Step(GameButtons.None);
        Assert.Equal(SessionPhase.RouteSelect, g.Phase);
        Assert.Equal(SessionOutcome.Running, g.Outcome);
        for (int i = 0; i < SessionRouteDelay; i++) g.Step(GameButtons.Shot);   // pause before input counts
        Assert.Equal(SessionPhase.RouteSelect, g.Phase);
        g.Step(GameButtons.None);
        g.Step(GameButtons.Down);                          // Makai -> Jigoku
        g.Step(GameButtons.None);
        Assert.Equal(1, g.World.RouteChosen);
        g.Step(GameButtons.Up);                            // and back
        g.Step(GameButtons.None);
        Assert.Equal(0, g.World.RouteChosen);
        g.Step(GameButtons.Down); g.Step(GameButtons.None);
        g.Step(GameButtons.Shot);
        Assert.Equal(SessionPhase.StageBonus, g.Phase);
        var b = g.Bonus!.Value;
        Assert.Equal(StageBonus.ComputeTotle(5, g.World.Timer.Value, g.World.CardComboMax, g.World.RemLives, g.World.RemBombs), b);
        StageTests.Dismiss(g);
        Assert.Equal(SessionOutcome.Cleared, g.Outcome);
        Assert.Equal(1, g.CarryOver!.RouteChosen);
        Assert.Equal(Math.Max(0, g.World.PelletSpeed), g.CarryOver.PelletSpeed);
    }

    const int SessionRouteDelay = SessionConstants.RouteDelay;

    [Fact]
    public void Totle_bonus_uses_the_bigger_multipliers_and_its_own_caps()
    {
        var b = StageBonus.ComputeTotle(5, 2000, 10, 4, 2);
        Assert.Equal(6553, b.Time);                        // 2000 * 5 = 10000 -> clamped
        Assert.Equal(2000, b.CardCombo);
        Assert.Equal(2400, b.Resources);                   // 4*500 + 2*200
        Assert.Equal(5000, b.StageNumber);
        Assert.Equal((6553u + 2000 + 2400 + 5000) * 10, b.Total);
        Assert.Equal(65530, StageBonus.ComputeTotle(99, 0, 0, 0, 0).StageNumber);
    }

    [Fact]
    public void Defeat_takes_the_pending_pellets_with_it()
    {
        var g = Started();
        g.World.Pellets.AddGroup(300, 150, PelletGroup.One, 40);
        Defeat(g);
        g.Step(GameButtons.None);
        Assert.Equal(0, g.World.Pellets.Alive);
    }

    [Fact]
    public void Each_life_start_runs_the_boss_for_one_extra_frame()
    {
        var g = Started(lives: 3);
        var w = g.World;
        w.Boss!.PatternCur = 0;
        w.Orb.Left = w.Player.Left; w.Orb.Top = Playfield.PlayerTop - 8; w.Orb.Y.Force = 0; w.Orb.ForceFrame = 0;
        g.Step(GameButtons.None);
        Assert.Equal(SessionPhase.MissAnimation, g.Phase);
        int frame = w.Boss.PhaseFrame;
        int guard = 0;
        while (g.Phase == SessionPhase.MissAnimation && guard++ < 500) g.Step(GameButtons.None);
        Assert.Equal(frame + 1, w.Boss.PhaseFrame);        // frozen during the miss, +1 at the restart
    }

    // ---- determinism ----

    static (ulong hash, int maxPhase, int maxAlive) Fight(uint seed, Rank rank)
    {
        var g = new GameSession(new GameOptions { FrameRand = seed, Stage = BossStage(), Rank = rank, Lives = 1000, Bombs = 2 });
        uint x = 0xBADC0FFE;
        GameButtons cur = GameButtons.None;
        int maxPhase = 0, maxAlive = 0;
        for (int i = 0; i < 9000; i++)
        {
            x ^= x << 13; x ^= x >> 17; x ^= x << 5;
            if (i % 6 == 0)
            {
                cur = GameButtons.None;
                if ((x & 1) != 0) cur |= GameButtons.Left;
                if ((x & 2) != 0) cur |= GameButtons.Right;
                if ((x & 4) != 0) cur |= GameButtons.Shot;
                if ((x & 56) == 56) cur |= GameButtons.Up;
            }
            // the scripted "player" also drags the Orb into the boss every so often, like a good shot would
            if (i % 150 == 149 && g.Phase == SessionPhase.Playing && g.World.Boss is { Phase: 1 or 2 } b)
            {
                g.World.Orb.Left = b.Left + 32; g.World.Orb.Top = b.Top + 32;
            }
            g.Step(cur);
            if (g.World.Boss != null) maxPhase = Math.Max(maxPhase, g.World.Boss.Phase);
            maxAlive = Math.Max(maxAlive, g.World.Pellets.Alive);
        }
        return (g.StateHash(), maxPhase, maxAlive);
    }

    [Theory]
    [InlineData(Rank.Normal)]
    [InlineData(Rank.Lunatic)]
    public void A_full_fight_is_deterministic_and_reaches_the_second_phase(Rank rank)
    {
        var a = Fight(31, rank);
        var b = Fight(31, rank);
        Assert.Equal(a, b);
        Assert.True(a.maxPhase >= 2, "fight should have progressed");
        Assert.True(a.maxAlive > 5);
        Assert.NotEqual(a.hash, Fight(32, rank).hash);
    }

    // ---- rendering ----

    sealed class OneColorArt : IGameArt
    {
        public readonly Dictionary<(string, int), byte> Colors = new();
        public int FrameCount(string sheet) => 0;
        public bool TryGetFrame(string sheet, int frame, out ArtFrame art)
        {
            if (Colors.TryGetValue((sheet, frame), out var c))
            {
                int w = sheet.StartsWith("BOSS1") ? 96 : 32;
                art = new ArtFrame(w, w, Enumerable.Repeat(c, w * w).ToArray(), Array.Empty<byte>(), Array.Empty<byte>());
                return true;
            }
            art = default;
            return false;
        }
        public bool TryGetBackground(out ArtFrame art) { art = default; return false; }
    }

    [Fact]
    public void Boss_is_drawn_from_its_sheets_or_as_a_box_and_art_does_not_change_the_hash()
    {
        var art = new OneColorArt();
        for (int f = 0; f < 8; f++) art.Colors[("BOSS1.BOS", f)] = 7;
        var plain = new GameSession(new GameOptions { FrameRand = 3, Stage = BossStage() });
        var arted = new GameSession(new GameOptions { FrameRand = 3, Stage = BossStage() }, null, art);
        for (int i = 0; i < 230; i++) { plain.Step(GameButtons.None); arted.Step(GameButtons.None); }
        Assert.Equal(plain.StateHash(), arted.StateHash());
        var a = new Framebuffer(); var b = new Framebuffer();
        arted.Render(a); plain.Render(b);
        var boss = arted.World.Boss!;
        Assert.Equal(7, a.GetPixel(boss.Left + 40, boss.Top + 40));
        Assert.Equal(12, b.GetPixel(boss.Left + 40, boss.Top + 40));       // box fallback
        Assert.Equal(9, b.GetPixel(128 + 2, 42));                          // first HP box
    }
}

static class SessionConstants
{
    public const int RouteDelay = GameSession.RouteSelectDelayTicks;
}
