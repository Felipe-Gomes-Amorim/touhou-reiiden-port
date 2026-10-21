using Th01.Core.Audio;
using Th01.Core.Game;
using Th01.Core.Input;

namespace Th01.Core.Tests.Game;

public class AngleMathTests
{
    [Theory]
    [InlineData(0, 0, 256)]
    [InlineData(1, 6, 256)]
    [InlineData(4, 25, 255)]
    [InlineData(32, 181, 181)]
    [InlineData(64, 256, 0)]
    [InlineData(128, 0, -256)]
    [InlineData(192, -256, 0)]
    [InlineData(255, -6, 256)]
    public void Sin_cos_tables(int angle, int sin, int cos)
    {
        Assert.Equal(sin, AngleMath.Sin8(angle));
        Assert.Equal(cos, AngleMath.Cos8(angle));
    }

    [Theory]
    [InlineData(0, 0, 0)]
    [InlineData(1, 1, 32)]       // exact diagonal
    [InlineData(0, 5, 0)]
    [InlineData(5, 0, 64)]
    [InlineData(0, -5, 128)]
    [InlineData(-5, 0, 192)]
    [InlineData(1, 2, 19)]       // atan(0.5) = 18.9 -> 19
    [InlineData(-1, 2, 237)]
    [InlineData(2, -1, 83)]
    [InlineData(-2, -1, 173)]
    [InlineData(284, 12, 62)]
    public void Iatan2_vectors(int y, int x, int expected) => Assert.Equal(expected, AngleMath.Iatan2(y, x));

    [Fact]
    public void Polar_rounds_negative_values_away_from_zero()
    {
        Assert.Equal(5, AngleMath.PolarX(0, 56, 60));    // 56 * 25 >> 8 = 5
        Assert.Equal(-6, AngleMath.PolarX(0, 56, 68));   // 56 * -25 >> 8 = -6 (floor), the asymmetry ZUN's code has
    }
}

public class PelletTests
{
    static GameWorld World(Rank rank = Rank.Normal, int pelletSpeed = -4)
    {
        var w = new GameWorld(new GameOptions { FrameRand = 5, Rank = rank, PelletSpeed = pelletSpeed }, NullSoundSink.Instance);
        w.Player.Left = 304;
        w.Orb.Left = 0; w.Orb.Top = 70;   // out of the way
        return w;
    }

    static int FirstMoving(Pellets p) => Array.FindIndex(p.Moving, m => m);

    // ---- speed and spawning ----

    [Theory]
    [InlineData(Rank.Easy, 0, 56, 56)]
    [InlineData(Rank.Normal, 0, 56, 62)]
    [InlineData(Rank.Normal, -4, 56, 56)]     // 62 + (-4 * 62) / 40 = 62 - 6
    [InlineData(Rank.Lunatic, 20, 56, 111)]   // 74 + (20 * 74) / 40 = 74 + 37
    [InlineData(Rank.Easy, -15, 16, 16)]      // never below 1 px/tick
    public void Pellet_speed_is_tuned_by_rank_and_resident_speed(Rank rank, int resident, int baseSp, int expected)
    {
        var w = World(rank, resident);
        w.Pellets.AddGroup(300, 100, PelletGroup.One, baseSp);
        Assert.Equal(expected, w.Pellets.VelY[FirstMoving(w.Pellets)]);
    }

    [Fact]
    public void Aimed_pellet_heads_for_the_player_with_the_original_offsets()
    {
        var w = World();
        w.Pellets.AddGroup(300, 100, PelletGroup.OneAimed, Pellets.Sp3_5);
        int i = FirstMoving(w.Pellets);
        // speed 56; target (320 - 8, 384): iatan2(284, 12) = 62 -> (56 * 13 >> 8, 56 * 256 >> 8)
        Assert.Equal(2, w.Pellets.VelX[i]);
        Assert.Equal(56, w.Pellets.VelY[i]);
    }

    [Fact]
    public void Static_spread_has_the_floor_asymmetry()
    {
        var w = World();
        w.Pellets.AddGroup(300, 100, PelletGroup.TwoSpreadNarrow, Pellets.Sp3_5);
        Assert.Equal(2, w.Pellets.Alive);
        var xs = Enumerable.Range(0, Pellets.Count).Where(k => w.Pellets.Moving[k]).Select(k => w.Pellets.VelX[k]).ToArray();
        Assert.Equal(new[] { -6, 5 }, xs);   // +4 and -4 around straight down
    }

    [Theory]
    [InlineData(PelletGroup.One, 1)]
    [InlineData(PelletGroup.OneAimed, 1)]
    [InlineData(PelletGroup.TwoSpreadWide, 2)]
    [InlineData(PelletGroup.ThreeSpreadNarrowAimed, 3)]
    [InlineData(PelletGroup.FourSpreadWide, 4)]
    [InlineData(PelletGroup.FiveSpreadWideAimed, 5)]
    [InlineData(PelletGroup.OneRandomNarrowAimed, 1)]
    [InlineData(PelletGroup.OneRandomWide, 1)]
    public void Group_sizes(PelletGroup g, int n)
    {
        var w = World();
        w.Pellets.AddGroup(300, 100, g, Pellets.Sp3_5);
        Assert.Equal(n, w.Pellets.Alive);
    }

    [Fact]
    public void Wide_spreads_fan_out_wider_than_narrow_ones()
    {
        var n = World(); n.Pellets.AddGroup(300, 100, PelletGroup.FiveSpreadNarrow, Pellets.Sp3_5);
        var w = World(); w.Pellets.AddGroup(300, 100, PelletGroup.FiveSpreadWide, Pellets.Sp3_5);
        int Max(Pellets p) => Enumerable.Range(0, Pellets.Count).Where(k => p.Moving[k]).Max(k => Math.Abs(p.VelX[k]));
        Assert.True(Max(w.Pellets) > Max(n.Pellets));
    }

    [Fact]
    public void Random_groups_draw_one_number_in_a_fixed_range()
    {
        var w = World();
        w.Rng = new Th01.Core.Rng.IRand(99);
        var probe = new Th01.Core.Rng.IRand(99);
        int r = probe.Next();
        w.Pellets.AddGroup(300, 100, PelletGroup.OneRandomWide, Pellets.Sp3_0);
        Assert.Equal(probe.Seed, w.Rng.Seed);
        int angle = (r & 0x3F) + 0x20;
        int i = FirstMoving(w.Pellets);
        int speed = 48 + 6 + (-4 * 54 / 40);
        Assert.Equal(AngleMath.PolarX(0, speed, angle), w.Pellets.VelX[i]);
        Assert.Equal(AngleMath.PolarY(0, speed, angle), w.Pellets.VelY[i]);
    }

    [Fact]
    public void Pool_is_limited_to_100_and_spawns_outside_the_field_are_dropped()
    {
        var w = World();
        for (int k = 0; k < 120; k++) w.Pellets.AddGroup(300, 100, PelletGroup.One, 30);
        Assert.Equal(100, w.Pellets.Alive);
        var v = World();
        v.Pellets.AddGroup(640, 100, PelletGroup.One, 30);
        v.Pellets.AddGroup(300, 401, PelletGroup.One, 30);
        v.Pellets.AddGroup(300, 55, PelletGroup.One, 30);
        v.Pellets.AddGroup(-9, 100, PelletGroup.One, 30);
        Assert.Equal(0, v.Pellets.Alive);
        v.Pellets.AddGroup(-8, 100, PelletGroup.One, 30);
        Assert.Equal(1, v.Pellets.Alive);
    }

    // ---- movement and clipping ----

    [Fact]
    public void Pellets_move_by_subpixels_and_are_clipped_at_the_edges()
    {
        var w = World();
        w.Pellets.AddGroup(300, 380, PelletGroup.One, 30);   // speed 30 + 6 - 3 = 33 sp/tick
        int i = FirstMoving(w.Pellets);
        int sp = w.Pellets.VelY[i];
        w.Pellets.Update();
        Assert.Equal(380 * 16 + sp, w.Pellets.CurTop[i]);
        int guard = 0;
        while (w.Pellets.Moving[i] && guard++ < 100) w.Pellets.Update();
        Assert.False(w.Pellets.Moving[i]);
        Assert.Equal(0, w.Pellets.Alive);
    }

    [Fact]
    public void Velocity_cap_only_applies_to_positive_components()
    {
        var w = World();
        w.Pellets.AddGroup(300, 100, PelletGroup.One, 30);
        int i = FirstMoving(w.Pellets);
        w.Pellets.VelY[i] = 500; w.Pellets.VelX[i] = -500;
        w.Pellets.Update();
        Assert.Equal(128, w.Pellets.VelY[i]);
        Assert.Equal(-500, w.Pellets.VelX[i]);
    }

    // ---- interactions ----

    [Fact]
    public void Pellet_in_the_player_box_costs_a_life_unless_invincible()
    {
        var w = World();
        w.Pellets.AddGroup(310, 370, PelletGroup.One, 16);
        w.Pellets.Update();
        Assert.True(w.PlayerIsHit);

        var v = World();
        v.PlayerInvincible = true;
        v.Pellets.AddGroup(310, 370, PelletGroup.One, 16);
        v.Pellets.Update();
        Assert.False(v.PlayerIsHit);
    }

    [Fact]
    public void Player_box_edges()
    {
        // left in [player+4, player+20], top in [368 (+8 while sliding), 368 + 32 - 8)
        foreach (var (x, y, sliding, hit) in new[]
        {
            (308, 368, false, true), (324, 368, false, true), (307, 368, false, false), (325, 368, false, false),
            (310, 391, false, true), (310, 392, false, false), (310, 367, false, false),
            (310, 375, true, false), (310, 376, true, true),
        })
        {
            var w = World();
            w.PlayerSliding = sliding;
            w.Pellets.AddGroup(x, y, PelletGroup.One, 16);
            int i = FirstMoving(w.Pellets);
            w.Pellets.VelY[i] = 0; w.Pellets.VelX[i] = 0;
            w.Pellets.CurLeft[i] = x * 16; w.Pellets.CurTop[i] = y * 16;
            w.Pellets.Update();
            Assert.True(hit == w.PlayerIsHit, $"({x},{y}) sliding={sliding}");
        }
    }

    [Fact]
    public void Orb_destroys_a_pellet_halves_its_velocity_and_pays_ten_points()
    {
        var w = World();
        w.Orb.Left = 300; w.Orb.Top = 200;
        w.Pellets.AddGroup(310, 205, PelletGroup.One, 64);
        int i = FirstMoving(w.Pellets);
        int vy = w.Pellets.VelY[i];
        w.Pellets.Update();
        Assert.Equal(1 + 1, w.Pellets.DecayFrame[i]);      // set to 1 by the hit, advanced once in the same pass
        Assert.Equal(vy >> 1, w.Pellets.VelY[i]);
        Assert.Equal(10, w.Pellets.DestroyScoreDelta);
        for (int k = 0; k < 20; k++) w.Pellets.Update();
        Assert.False(w.Pellets.Moving[i]);
        Assert.Equal(0, w.Pellets.Alive);
    }

    [Fact]
    public void Deflecting_player_knocks_pellets_sideways_at_8_px()
    {
        var w = World();
        w.PlayerDeflecting = true;
        w.Pellets.AddGroup(290, 350, PelletGroup.One, 16);   // left of the player's centre: goes left
        w.Pellets.Update();
        int i = FirstMoving(w.Pellets);
        Assert.Equal(-128, w.Pellets.VelX[i]);
        Assert.Equal(0, w.Pellets.VelY[i]);

        var v = World();
        v.PlayerDeflecting = true;
        v.Pellets.AddGroup(335, 350, PelletGroup.One, 16);
        v.Pellets.Update();
        Assert.Equal(128, v.Pellets.VelX[FirstMoving(v.Pellets)]);
    }

    [Fact]
    public void A_player_shot_removes_a_pellet_without_points()
    {
        var w = World();
        w.Pellets.AddGroup(200, 200, PelletGroup.One, 16);
        w.Shots.Add(200, 200);
        w.Pellets.Update();
        Assert.Equal(0, w.Pellets.Alive);
        Assert.Equal(0, w.Pellets.DestroyScoreDelta);
        Assert.Equal(1, w.Shots.DecayFrame.Count(d => d != 0) + w.Shots.Moving.Count(m => m));
    }

    [Fact]
    public void Decay_divides_velocity_by_one_and_a_half_toward_zero()
    {
        var w = World();
        w.Pellets.AddGroup(200, 200, PelletGroup.One, 16);
        int i = FirstMoving(w.Pellets);
        w.Pellets.VelY[i] = -5; w.Pellets.VelX[i] = 5;
        w.Pellets.Decay();
        Assert.Equal(-3, w.Pellets.VelY[i]);
        Assert.Equal(3, w.Pellets.VelX[i]);
        Assert.Equal(1, w.Pellets.DecayFrame[i]);
        Assert.Equal(10, w.Pellets.DestroyScoreDelta);
        w.Pellets.Decay();                        // already decaying: nothing
        Assert.Equal(10, w.Pellets.DestroyScoreDelta);
    }

    [Fact]
    public void Spawn_cloud_delays_the_pellet_by_eight_frames()
    {
        var w = World();
        w.Pellets.SpawnWithCloud = true;
        w.Pellets.AddGroup(200, 200, PelletGroup.One, 16);
        Assert.False(w.Pellets.Moving[0]);
        Assert.Equal(1, w.Pellets.Alive);
        for (int k = 0; k < 7; k++) w.Pellets.Update();
        Assert.False(w.Pellets.Moving[0]);
        w.Pellets.Update();
        Assert.True(w.Pellets.Moving[0]);
    }

    // ---- speed tracking ----

    [Fact]
    public void Resident_speed_raise_and_lower_clamp()
    {
        var w = World(pelletSpeed: 19);
        w.Pellets.SpeedRaise(2);
        Assert.Equal(20, w.PelletSpeed);
        w.Pellets.SpeedLower(0, -2);              // above max(0): clamped to 0, no delta
        Assert.Equal(0, w.PelletSpeed);
        w.Pellets.SpeedLower(0, -2);
        Assert.Equal(-2, w.PelletSpeed);
        for (int k = 0; k < 20; k++) w.Pellets.SpeedLower(0, -2);
        Assert.Equal(-15, w.PelletSpeed);
    }

    // ---- sources ----

    [Fact]
    public void Turret_fires_on_the_original_schedule()
    {
        var obs = new byte[StageSetup.Rows, StageSetup.Cols];
        obs[2, 5] = 2;                              // slow aimed turret
        var cards = new bool[StageSetup.Rows, StageSetup.Cols]; cards[9, 19] = true;
        var w = new GameWorld(new GameOptions { Stage = new StageSetup { Cards = cards, Obstacles = obs }, Rank = Rank.Normal }, NullSoundSink.Instance);
        w.Orb.Left = 600; w.Orb.Top = 300;
        for (int k = 1; k <= 209; k++) w.Stage!.ObstaclesUpdate(false);
        Assert.Equal(0, w.Pellets.Alive);
        w.Stage!.ObstaclesUpdate(false);            // 210: fire
        Assert.Equal(1, w.Pellets.Alive);
        for (int k = 211; k <= 217; k++) w.Stage.ObstaclesUpdate(false);
        Assert.Equal(0, w.Stage.ObsFrame[0]);       // cycle restarts after the cool-down
        Assert.Equal(1, w.Pellets.Alive);
    }

    [Fact]
    public void Turrets_stay_silent_on_easy()
    {
        var obs = new byte[StageSetup.Rows, StageSetup.Cols];
        obs[2, 5] = 8;
        var cards = new bool[StageSetup.Rows, StageSetup.Cols]; cards[9, 19] = true;
        var w = new GameWorld(new GameOptions { Stage = new StageSetup { Cards = cards, Obstacles = obs }, Rank = Rank.Easy }, NullSoundSink.Instance);
        for (int k = 0; k < 1000; k++) w.Stage!.ObstaclesUpdate(false);
        Assert.Equal(0, w.Pellets.Alive);
    }

    [Fact]
    public void Quick_turret_fires_every_100_frames_and_picks_its_group_by_type()
    {
        var obs = new byte[StageSetup.Rows, StageSetup.Cols];
        obs[2, 5] = 13;                             // quick, 5-way spread
        var cards = new bool[StageSetup.Rows, StageSetup.Cols]; cards[9, 19] = true;
        var w = new GameWorld(new GameOptions { Stage = new StageSetup { Cards = cards, Obstacles = obs }, Rank = Rank.Hard }, NullSoundSink.Instance);
        w.Orb.Left = 600; w.Orb.Top = 300;
        for (int k = 1; k <= 110; k++) w.Stage!.ObstaclesUpdate(false);
        Assert.Equal(5, w.Pellets.Alive);
    }

    [Fact]
    public void Lunatic_cards_fire_a_revenge_pellet_when_the_flip_ends()
    {
        var cards = new bool[StageSetup.Rows, StageSetup.Cols]; cards[0, 5] = true; cards[9, 19] = true;
        var setup = new StageSetup { Cards = cards, Obstacles = new byte[StageSetup.Rows, StageSetup.Cols] };
        foreach (var (rank, pellets) in new[] { (Rank.Lunatic, 1), (Rank.Hard, 0) })
        {
            var w = new GameWorld(new GameOptions { Stage = setup, Rank = rank }, NullSoundSink.Instance);
            w.Stage!.CardFlagArr[0] = CardFlag.Flipping;
            w.Stage.CardFlipFrame[0] = 24;
            w.Stage.CardsUpdate();                  // frame 25
            Assert.Equal(pellets, w.Pellets.Alive);
        }
    }

    [Fact]
    public void Harry_up_rains_pellets_with_growing_frequency()
    {
        var cards = new bool[StageSetup.Rows, StageSetup.Cols]; cards[9, 19] = true;
        var w = new GameWorld(new GameOptions { Stage = new StageSetup { Cards = cards, Obstacles = new byte[StageSetup.Rows, StageSetup.Cols] }, Rank = Rank.Hard }, NullSoundSink.Instance);
        w.Timer.Value = 0;
        for (int k = 0; k < 4; k++) w.Timer.TickDown(w);
        Assert.Equal(0, w.Pellets.Alive);
        w.Timer.TickDown(w);                        // 5th call: every 5th at first; rank Hard => 3 pellets
        Assert.Equal(3, w.Pellets.Alive);
        for (int k = 0; k < 495; k++) w.Timer.TickDown(w);
        var t = w.Pellets.Alive;
        Assert.True(t > 3);
    }

    [Fact]
    public void Bomb_start_and_kuji_phase_decay_pellets()
    {
        var w = World();
        w.Pellets.AddGroup(200, 200, PelletGroup.One, 16);
        w.Bomb.UpdateFrame(w, 0);
        Assert.Equal(1, w.Pellets.DecayFrame[FirstMoving(w.Pellets)]);
        var v = World();
        v.Pellets.AddGroup(200, 200, PelletGroup.One, 16);
        v.Bomb.UpdateFrame(v, 7);
        Assert.Equal(0, v.Pellets.DecayFrame[FirstMoving(v.Pellets)]);
        v.Bomb.UpdateFrame(v, 10);
        Assert.Equal(1, v.Pellets.DecayFrame[FirstMoving(v.Pellets)]);
    }

    [Fact]
    public void Destroyed_pellet_points_are_added_to_the_score_at_the_end_of_the_tick()
    {
        var g = new GameSession(new GameOptions { FrameRand = 3 });
        g.Step(GameButtons.Shot); g.Step(GameButtons.None);
        var w = g.World;
        w.Orb.Left = 300; w.Orb.Top = 200; w.Orb.Y.Force = 0; w.Orb.ForceFrame = 0;
        w.Pellets.AddGroup(310, 205, PelletGroup.One, 16);
        uint before = w.Score;
        g.Step(GameButtons.None);
        Assert.Equal(before + 10, w.Score);
        Assert.Equal(0, w.Pellets.DestroyScoreDelta);
    }

    [Fact]
    public void Pellet_speed_ramps_with_the_frame_counter_and_carries_over_clamped_to_zero()
    {
        var g = new GameSession(new GameOptions { FrameRand = 3, Lives = 4, Bombs = 1 });
        g.Step(GameButtons.Shot); g.Step(GameButtons.None);
        var w = g.World;
        int cycle = 1800 - 4 * 200 - 1 * 50;        // 950
        w.FrameRand = (uint)(cycle * 7 - 1);
        int before = w.PelletSpeed;
        g.Step(GameButtons.None);
        Assert.Equal(before + 1, w.PelletSpeed);

        var cards = new bool[StageSetup.Rows, StageSetup.Cols]; cards[0, 5] = true;
        var s = new GameSession(new GameOptions
        {
            Stage = new StageSetup { Cards = cards, Obstacles = new byte[StageSetup.Rows, StageSetup.Cols] }, PelletSpeed = -4,
        });
        s.Step(GameButtons.Shot); s.Step(GameButtons.None);
        s.World.Orb.Left = 160; s.World.Orb.Top = 64;
        int guard = 0;
        while (s.Phase != SessionPhase.StageBonus && guard++ < 500) s.Step(GameButtons.None);
        StageTests.Dismiss(s);
        Assert.Equal(0, s.CarryOver!.PelletSpeed);   // "negative becomes 0" on stage clear
    }
}
