using Th01.Core.Audio;
using Th01.Core.Game;
using Th01.Core.Input;
using Th01.Core.Rng;
using Th01.Core.Sim;

namespace Th01.Core.Tests.Game;

public class MimaTests
{
    static StageSetup JigokuBoss(int id = StageSetup.MimaStage, int route = StageSetup.RouteJigoku) => new()
    {
        StageId = id,
        Route = route,
        Cards = new bool[StageSetup.Rows, StageSetup.Cols],
        Obstacles = new byte[StageSetup.Rows, StageSetup.Cols],
    };

    /// <summary>A world with Mima already in phase 1 (entrance skipped), the Orb far away.</summary>
    static GameWorld World(Rank rank = Rank.Normal, uint seed = 5)
    {
        var w = new GameWorld(new GameOptions { FrameRand = seed, Stage = JigokuBoss(), Rank = rank, Lives = 3, Bombs = 1 }, NullSoundSink.Instance);
        w.Mima!.Phase = 1;
        w.Mima.PhaseFrame = 0;
        w.Mima.Left = Mima.BaseLeft;
        w.Mima.Top = Mima.BaseTop;
        w.Orb.Left = 0; w.Orb.Top = 380; w.Orb.PrevLeft = 0; w.Orb.PrevTop = 380;
        w.Player.Left = 304;
        return w;
    }

    static GameWorld InPhase3(int pattern, Rank rank = Rank.Normal, uint seed = 9)
    {
        var w = World(rank, seed);
        w.Mima!.Phase = 3; w.Mima.PatternCur = pattern; w.Mima.Hp = 5;
        w.Rng = new IRand(seed);
        return w;
    }

    static void Run(GameWorld w, int updates) { for (int i = 0; i < updates; i++) w.Mima!.Update(); }

    static int PelletsOf(GameWorld w, PelletMotion m) =>
        Enumerable.Range(0, Pellets.Count).Count(i => w.Pellets.Moving[i] && w.Pellets.Motion[i] == m);

    static void PlaceOrbOnBoss(GameWorld w)
    {
        w.Orb.PrevLeft = w.Mima!.Left + 32; w.Orb.PrevTop = w.Mima.Top + 32;
    }

    // ---- constants and set-up ----

    [Fact]
    public void Base_position_and_hp()
    {
        Assert.Equal(256, Mima.BaseLeft);
        Assert.Equal(120, Mima.BaseTop);
        Assert.Equal(12, World().Mima!.Hp);
    }

    [Fact]
    public void Only_stage_10_on_the_jigoku_route_gets_this_boss()
    {
        Assert.NotNull(World().Mima);
        Assert.Null(World().Boss);
        Assert.Null(new GameWorld(new GameOptions { Stage = JigokuBoss(route: StageSetup.RouteMakai) }, NullSoundSink.Instance).Mima);
        Assert.Null(new GameWorld(new GameOptions { Stage = JigokuBoss(id: 14) }, NullSoundSink.Instance).Mima);
        Assert.Null(new GameWorld(new GameOptions { Stage = JigokuBoss(id: 4) }, NullSoundSink.Instance).Mima);
    }

    [Fact]
    public void Entrance_spreads_in_for_51_frames_then_play_starts()
    {
        var g = new GameSession(new GameOptions { FrameRand = 3, Stage = JigokuBoss() });
        Assert.Equal(SessionPhase.BossEntrance, g.Phase);
        for (int i = 0; i < 50; i++) { g.Step(GameButtons.Shot); Assert.Equal(SessionPhase.BossEntrance, g.Phase); }
        g.Step(GameButtons.None);
        Assert.Equal(SessionPhase.Playing, g.Phase);
        var b = g.World.Mima!;
        Assert.Equal(1, b.Phase);
        Assert.Equal(0, b.PhaseFrame);
        Assert.Equal((Mima.BaseLeft, Mima.BaseTop), (b.Left, b.Top));
    }

    // ---- being hit ----

    [Fact]
    public void Orb_hit_costs_one_hp_and_scores_5000()
    {
        var w = World();
        PlaceOrbOnBoss(w);
        w.Mima!.Update();
        Assert.Equal(11, w.Mima.Hp);
        Assert.True(w.Mima.Invincible);
        Assert.Equal(5000u, w.Score);
    }

    [Theory]
    [InlineData(8, true)]
    [InlineData(88, true)]
    [InlineData(7, false)]
    [InlineData(89, false)]
    public void Orb_hitbox_uses_the_previous_frame_position(int dx, bool hit)
    {
        var w = World();
        w.Orb.PrevLeft = w.Mima!.Left + dx; w.Orb.PrevTop = w.Mima.Top + 60;
        w.Mima.Update();
        Assert.Equal(hit ? 11 : 12, w.Mima.Hp);
    }

    [Fact]
    public void Shots_are_absorbed_without_damage()
    {
        var w = World();
        w.Shots.Add(w.Mima!.Left + 60, w.Mima.Top + 80);
        w.Mima.Update();
        Assert.Equal(12, w.Mima.Hp);
        Assert.False(w.Shots.Moving[0]);
    }

    // ---- phase 1 ----

    [Fact]
    public void Square_pattern_aims_four_pellets_at_the_player_once_the_square_is_full_size()
    {
        var w = World();
        Run(w, 183);                                       // the 10th radius step is at frame 176; the first volley at 184
        Assert.Equal(0, w.Pellets.Alive);
        Run(w, 1);
        Assert.Equal(4, w.Pellets.Alive);
        Run(w, 361 - 184);                                 // frame 361: over
        Assert.Equal(0, w.Mima!.PhaseFrame);
        Assert.Equal(1, w.Mima.PatternCur);
    }

    [Fact]
    public void Missile_pattern_fires_four_missiles_per_volley_from_frame_248()
    {
        var w = World();
        w.Mima!.PatternCur = 1;
        Run(w, 247);
        Assert.Equal(0, w.Missiles.Alive);
        Run(w, 1);
        Assert.Equal(4, w.Missiles.Alive);
        Run(w, 8);
        Assert.Equal(8, w.Missiles.Alive);
    }

    [Fact]
    public void Two_squares_pattern_spawns_pellets_with_clouds_and_ends_on_frame_321()
    {
        var w = World();
        w.Mima!.PatternCur = 2;
        Run(w, 320);
        Assert.True(w.Pellets.Alive > 0);
        Assert.False(w.Pellets.SpawnWithCloud);
        Run(w, 1);
        Assert.Equal(0, w.Mima.PhaseFrame);
        Assert.Equal(3, w.Mima.PatternCur);
    }

    [Fact]
    public void Hop_pattern_teleports_four_times_and_fires_a_ring_of_chase_pellets_after_each()
    {
        var w = World();
        w.Mima!.PatternCur = 3;
        int guard = 0;
        bool sawFirstHop = false;
        while (w.Mima.PatternCur == 3 && guard++ < 2000)
        {
            w.Mima.Update();
            if (w.Mima.Hop == 1 && w.Mima.PhaseFrame > Mima.W / 32 + 1 && !sawFirstHop)
            {
                sawFirstHop = true;
                Assert.Equal(0, w.Mima.Left);                       // direction X_RIGHT: starts at the left edge
                Assert.Equal(Mima.BaseTop - Playfield.Height / 14, w.Mima.Top);
            }
        }
        Assert.True(sawFirstHop);
        Assert.Equal(0, w.Mima.PatternCur);
        Assert.Equal(4 * 8, PelletsOf(w, PelletMotion.Chase));      // 8 per ring on Normal
        Assert.Equal(-1, w.Mima.Hop);
    }

    [Fact]
    public void Phase_1_waits_for_the_pattern_to_end_and_then_re_appears_into_phase_3()
    {
        var w = World();
        w.Mima!.Hp = 6;
        int guard = 0;
        while (w.Mima.Phase == 1 && guard++ < 1000) w.Mima.Update();
        Assert.Equal(2, w.Mima.Phase);
        Assert.Equal((Mima.BaseLeft, Mima.BaseTop), (w.Mima.Left, w.Mima.Top));
        Assert.True(guard >= 361);                                  // the first pattern ran to its end
        while (w.Mima.Phase == 2 && guard++ < 2000) w.Mima.Update();
        Assert.Equal(3, w.Mima.Phase);
        Assert.Equal(0, w.Mima.PatternCur);
    }

    // ---- phase 3 ----

    [Fact]
    public void Pillars_draw_eight_random_columns_in_order_and_each_ends_in_a_three_pellet_spread()
    {
        var w = InPhase3(0);
        var probe = new IRand(9);
        int[] min = { 16, 0, 64, 336, 128, 336, 64, 336 };
        int[] range = { 48, 48, 224, 224, 160, 160, 224, 224 };
        Run(w, 100);
        for (int i = 0; i < 8; i++)
        {
            int r = probe.Next();
            int x = i == 1 ? 624 - r % 48 : min[i] + r % range[i];   // pillar 1 hugs the right edge
            Assert.Equal(x, w.Mima!.PillarCenterXAt(i));
        }
        Assert.Equal(probe.Seed, w.Rng.Seed);
        w.PlayerInvincible = true;
        int guard = 0;
        while (w.Mima!.PatternCur == 0 && guard++ < 1000) w.Mima.Update();
        Assert.Equal(1, w.Mima.PatternCur);
        Assert.Equal(8 * 3, w.Pellets.Alive);
    }

    [Fact]
    public void A_pillar_hurts_a_player_standing_in_it()
    {
        var w = InPhase3(0);
        Run(w, 100);
        // Put the player under pillar 0 and wait for its column phase.
        w.Player.Left = w.Mima!.PillarCenterXAt(0) - 16;
        int guard = 0;
        while (!w.PlayerIsHit && w.Mima.PatternCur == 0 && guard++ < 1000) w.Mima.Update();
        Assert.True(w.PlayerIsHit);
    }

    [Fact]
    public void Halfcircle_missiles_start_at_angle_0_and_turn_by_0x0D_each_volley()
    {
        var w = InPhase3(1);
        Run(w, 184);                                       // first volley: frame > 180 and frame % 16 == 8 -> 184
        Assert.Equal(4, w.Missiles.Alive);
        int i = Enumerable.Range(0, Missiles.Count).First(k => w.Missiles.Flag[k] != Missiles.FlagFree);
        Assert.Equal(4 << 4, w.Missiles.VelX[i]);          // 4.5 px chopped to 4, straight to the right
        Assert.Equal(0, w.Missiles.VelY[i]);
        Run(w, 16);
        int j = Enumerable.Range(0, Missiles.Count).Where(k => w.Missiles.Flag[k] != Missiles.FlagFree).Last();
        Assert.Equal(AngleMath.PolarX(0, 4, 0x0D) << 4, w.Missiles.VelX[j]);
    }

    [Fact]
    public void Slow_spray_fires_four_pellets_per_step_after_the_square_is_full()
    {
        var w = InPhase3(2);
        Run(w, 183);
        Assert.Equal(0, w.Pellets.Alive);                  // radius 80 is reached at frame 144; the fire starts above 180
        Run(w, 1);                                         // frame 184
        Assert.Equal(4, w.Pellets.Alive);
        Run(w, 8);
        Assert.Equal(8, w.Pellets.Alive);
    }

    [Fact]
    public void Lasers_come_out_one_per_frame_from_the_corners()
    {
        var w = InPhase3(3);
        Run(w, 181);                                       // frame 181 % 4 = 1
        Assert.True(w.Lasers.Lasers[1].Alive);
        Assert.False(w.Lasers.Lasers[0].Alive);
        Assert.Equal(54 / 8, w.Lasers.Lasers[1].RayExtendSpeed);
        Run(w, 3);
        Assert.True(w.Lasers.Lasers.Take(4).All(l => l.Alive));
    }

    // ---- shoot-out lasers on their own ----

    [Fact]
    public void A_laser_hits_a_player_in_its_path_and_leaves_the_field_after_the_move_out()
    {
        var w = World();
        w.Player.Left = 304;
        var l = w.Lasers.Lasers[0];
        l.Spawn(320, 300, 320, 400, 48, 15, 20, 4);        // straight down, 6 px per tick
        Assert.Equal(6, l.RayExtendSpeed);
        for (int i = 0; i < 5; i++) l.Update();
        Assert.False(w.PlayerIsHit);                       // 30 pixels long: down to y = 329
        int guard = 0;
        while (!w.PlayerIsHit && guard++ < 40) l.Update();
        Assert.True(w.PlayerIsHit);
        w.PlayerIsHit = false;
        guard = 0;
        while (l.Alive && guard++ < 100) l.Update();
        Assert.False(l.Alive);
    }

    [Fact]
    public void An_invincible_player_is_not_hit_by_a_laser_and_spawn_ignores_a_live_laser()
    {
        var w = World();
        w.PlayerInvincible = true;
        var l = w.Lasers.Lasers[0];
        l.Spawn(320, 300, 320, 400, 48, 15, 20, 4);
        for (int i = 0; i < 30; i++) l.Update();
        Assert.False(w.PlayerIsHit);
        int age = l.Age;
        l.Spawn(10, 100, 20, 200, 80, 15, 20, 4);
        Assert.Equal(age, l.Age);
        Assert.Equal(6, l.RayExtendSpeed);
    }

    // ---- missiles on their own ----

    [Fact]
    public void A_missile_explodes_at_the_field_edge_and_frees_its_slot()
    {
        var w = World();
        w.Missiles.Add(620, 200, 8, 0);
        w.Missiles.Update();
        Assert.Equal(Missiles.FlagHit, w.Missiles.Flag[0]);
        Assert.Equal(Playfield.Right - Missiles.W, w.Missiles.HitLeft[0]);
        int guard = 0;
        while (w.Missiles.Alive > 0 && guard++ < 10) { w.FrameRand++; w.Missiles.Update(); }
        Assert.Equal(0, w.Missiles.Alive);
    }

    [Fact]
    public void A_missile_on_the_player_hits_unless_the_player_is_invincible()
    {
        var w = World();
        w.Missiles.Add(w.Player.Left + 10, Player.Top + 10, 0, 0);
        w.Missiles.Update();
        Assert.True(w.PlayerIsHit);
        var v = World();
        v.PlayerInvincible = true;
        v.Missiles.Add(v.Player.Left + 10, Player.Top + 10, 0, 0);
        v.Missiles.Update();
        Assert.False(v.PlayerIsHit);
    }

    // ---- defeat ----

    [Fact]
    public void Last_hp_ends_the_fight_and_clears_missiles_and_lasers()
    {
        var w = InPhase3(0);
        w.Mima!.Hp = 1;
        w.Missiles.Add(300, 200, 1, 1);
        w.Lasers.Lasers[2].Spawn(320, 300, 320, 400, 48, 15, 20, 4);
        PlaceOrbOnBoss(w);
        w.Mima.Update();
        Assert.Equal(0, w.Mima.Hp);
        Assert.True(w.Mima.Defeated);
        Assert.Equal(0, w.Missiles.Alive);
        Assert.False(w.Lasers.Lasers[2].Alive);
    }

    [Fact]
    public void Defeat_leads_straight_to_the_stage_bonus_without_a_route_menu()
    {
        var g = new GameSession(new GameOptions { FrameRand = 12, Stage = JigokuBoss(), Lives = 3, Bombs = 1, Rank = Rank.Hard });
        for (int i = 0; i < 51; i++) g.Step(GameButtons.None);
        Assert.Equal(SessionPhase.Playing, g.Phase);
        var b = g.World.Mima!;
        b.Phase = 3; b.Hp = 1; b.Invincible = false; b.PatternCur = 1;
        g.World.Orb.Left = b.Left + 32; g.World.Orb.Top = b.Top + 32; g.World.Orb.Y.Force = 0; g.World.Orb.ForceFrame = 0;
        g.World.Orb.PrevLeft = g.World.Orb.Left; g.World.Orb.PrevTop = g.World.Orb.Top;
        g.Step(GameButtons.None);
        Assert.True(b.Defeated);
        g.Step(GameButtons.None);
        Assert.Equal(SessionPhase.BossDefeat, g.Phase);
        int ticks = 0;
        while (g.Phase == SessionPhase.BossDefeat && ticks++ < 2000) g.Step(GameButtons.None);
        Assert.Equal(SessionPhase.StageBonus, g.Phase);
        Assert.Equal(StageBonus.ComputeTotle(10, g.World.Timer.Value, g.World.CardComboMax, g.World.RemLives, g.World.RemBombs), g.Bonus!.Value);
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
        for (int i = 0; i < 9000; i++)
        {
            x ^= x << 13; x ^= x >> 17; x ^= x << 5;
            if (i % 6 == 0)
            {
                cur = GameButtons.None;
                if ((x & 1) != 0) cur |= GameButtons.Left;
                if ((x & 2) != 0) cur |= GameButtons.Right;
                if ((x & 4) != 0) cur |= GameButtons.Shot;
            }
            if (i % 150 == 149 && g.Phase == SessionPhase.Playing && g.World.Mima is { Phase: 1 or 3 } b)
            {
                g.World.Orb.Left = b.Left + 32; g.World.Orb.Top = b.Top + 32;
            }
            g.Step(cur);
            if (g.World.Mima != null) maxPhase = Math.Max(maxPhase, g.World.Mima.Phase);
            maxAlive = Math.Max(maxAlive, g.World.Pellets.Alive + g.World.Missiles.Alive);
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
        Assert.True(a.maxPhase >= 1);
        Assert.True(a.maxAlive > 5);
        Assert.NotEqual(a.hash, Fight(32, rank).hash);
    }
}
