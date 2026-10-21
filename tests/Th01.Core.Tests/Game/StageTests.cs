using Th01.Core.Audio;
using Th01.Core.Game;
using Th01.Core.Input;
using Th01.Core.Rng;
using Th01.Core.Sim;

namespace Th01.Core.Tests.Game;

public class StageTests
{
    const GameButtons None = GameButtons.None;

    /// <summary>Builds a StageSetup from (row, col) card entries and (row, col, value) obstacle entries.</summary>
    static StageSetup Setup(int stageId = 0, int route = 0, (int r, int c)[]? cards = null, (int r, int c, byte v)[]? obs = null)
    {
        var c = new bool[StageSetup.Rows, StageSetup.Cols];
        var o = new byte[StageSetup.Rows, StageSetup.Cols];
        foreach (var (r, col) in cards ?? Array.Empty<(int, int)>()) c[r, col] = true;
        foreach (var (r, col, v) in obs ?? Array.Empty<(int, int, byte)>()) o[r, col] = v;
        return new StageSetup { StageId = stageId, Route = route, Cards = c, Obstacles = o };
    }

    static GameWorld World(StageSetup s, uint seed = 1, Rank rank = Rank.Normal) =>
        new(new GameOptions { FrameRand = seed, Stage = s, Rank = rank }, NullSoundSink.Instance);

    static GameSession Session(StageSetup s, uint seed = 1, Rank rank = Rank.Normal, int lives = 4)
    {
        var g = new GameSession(new GameOptions { FrameRand = seed, Stage = s, Rank = rank, Lives = lives });
        g.Step(GameButtons.Shot);
        g.Step(None);
        Assert.Equal(SessionPhase.Playing, g.Phase);
        // let the shoot animation end so nothing else interferes
        for (int i = 0; i < 4; i++) g.Step(None);
        return g;
    }

    /// <summary>Presses and releases Shot, which closes the stage bonus box.</summary>
    internal static void Dismiss(GameSession g)
    {
        g.Step(GameButtons.Shot);
        g.Step(None);
    }

    static void ParkOrb(GameWorld w, int left, int top, double force = 0)
    {
        w.Orb.Left = left; w.Orb.Top = top; w.Orb.Y.Force = force; w.Orb.ForceFrame = 0;
    }

    // ---- Setup ----

    [Fact]
    public void Setup_rejects_wrong_dimensions()
    {
        var bad = new StageSetup { Cards = new bool[9, 20], Obstacles = new byte[10, 20] };
        Assert.Throws<ArgumentException>(() => World(bad));
    }

    [Fact]
    public void Cards_are_numbered_row_major_with_hp_from_the_obstacle_array()
    {
        var w = World(Setup(cards: new[] { (0, 0), (0, 3), (2, 1) },
                            obs: new (int, int, byte)[] { (0, 3, 15), (2, 1, 14), (5, 5, 16) /* no card here: ignored */ }));
        var s = w.Stage!;
        Assert.Equal(3, s.CardCount);
        Assert.Equal(new[] { 0, 96, 32 }, s.CardLeft);
        Assert.Equal(new[] { 64, 64, 128 }, s.CardTop);
        Assert.Equal(new[] { 0, 2, 1 }, s.CardHp);   // 14 -> 1 extra flip, 15 -> 2, none -> 0
        Assert.Equal(0, s.ObstacleCount);            // 14..16 are never obstacles
    }

    // ---- Cards ----

    [Fact]
    public void Card_touched_by_the_orb_flips_scores_and_is_removed_after_30_ticks()
    {
        var g = Session(Setup(cards: new[] { (0, 5) }));
        var w = g.World;
        ParkOrb(w, 160, 64);
        g.Step(None);                                  // hit on this tick
        Assert.Equal(CardFlag.Flipping, w.Stage!.CardFlagArr[0]);
        Assert.Equal(100u, w.Score);                   // stage 0, combo 0
        Assert.Equal(1, w.CardComboCur);
        Assert.Equal(1, w.Stage.CardFlipFrame[0]);     // already advanced once on the hit tick
        for (int i = 0; i < 28; i++) { g.Step(None); Assert.Equal(SessionOutcome.Running, g.Outcome); }
        g.Step(None);
        Assert.Equal(SessionPhase.StageBonus, g.Phase);
        Assert.Equal(SessionOutcome.Running, g.Outcome);
        Assert.Equal(CardFlag.Removed, w.Stage.CardFlagArr[0]);
        Dismiss(g);
        Assert.Equal(SessionOutcome.Cleared, g.Outcome);
    }

    [Fact]
    public void Card_hit_boundary_is_strictly_below_24_pixels()
    {
        var w = World(Setup(cards: new[] { (0, 5) }));
        ParkOrb(w, 160 + 23, 64 + 23);
        w.Stage!.CardsHitTest();
        Assert.Equal(CardFlag.Flipping, w.Stage.CardFlagArr[0]);
        var w2 = World(Setup(cards: new[] { (0, 5) }));
        ParkOrb(w2, 160 + 24, 64);
        w2.Stage!.CardsHitTest();
        Assert.Equal(CardFlag.Alive, w2.Stage.CardFlagArr[0]);
    }

    [Fact]
    public void Combo_raises_the_score_of_each_following_card_and_lunatic_scales_it_up()
    {
        var w = World(Setup(cards: new[] { (0, 5), (0, 6) }));
        ParkOrb(w, 176, 64);                           // within 24 px of both
        w.Stage!.CardsHitTest();
        Assert.Equal(100u + (100u + 1 * 20), w.Score);
        Assert.Equal(2, w.CardComboMax);

        var l = World(Setup(cards: new[] { (0, 5), (0, 6) }), rank: Rank.Lunatic);
        ParkOrb(l, 176, 64);
        l.Stage!.CardsHitTest();
        Assert.Equal(100u + (100u + 1 * 35), l.Score);
    }

    [Fact]
    public void Card_score_is_capped_and_grows_with_the_stage_scene()
    {
        var w = World(Setup(stageId: 10, cards: new[] { (0, 5) }));
        w.CardComboCur = 40;                           // 40^2 * 20 = 32000 -> capped
        ParkOrb(w, 160, 64);
        w.Stage!.CardsHitTest();
        Assert.Equal(25600u, w.Score);
        var v = World(Setup(stageId: 10, cards: new[] { (0, 5) }));
        ParkOrb(v, 160, 64);
        v.Stage!.CardsHitTest();
        Assert.Equal(300u, v.Score);                   // (10 / 5) * 100 + 100
    }

    [Fact]
    public void Combo_square_wraps_like_a_16_bit_int()
    {
        var w = World(Setup(cards: new[] { (0, 5) }));
        w.CardComboCur = 200;                          // 40000 wraps to -25536 and sign-extends: score is capped
        ParkOrb(w, 160, 64);
        w.Stage!.CardsHitTest();
        Assert.Equal(25600u, w.Score);
    }

    [Fact]
    public void Multi_flip_card_returns_to_alive_with_one_hp_less()
    {
        var g = Session(Setup(cards: new[] { (0, 5), (9, 19) }, obs: new (int, int, byte)[] { (0, 5, 15) }));
        var w = g.World;
        Assert.Equal(2, w.Stage!.CardHp[0]);
        ParkOrb(w, 160, 64);
        g.Step(None);
        for (int i = 0; i < 29; i++) { ParkOrb(w, 600, 300); g.Step(None); }   // complete the flip, orb elsewhere
        Assert.Equal(CardFlag.Alive, w.Stage.CardFlagArr[0]);
        Assert.Equal(1, w.Stage.CardHp[0]);
        Assert.Equal(0, w.Stage.CardFlipFrame[0]);
        Assert.Equal(SessionOutcome.Running, g.Outcome);
    }

    [Fact]
    public void Every_tenth_flipped_card_drops_a_point_item_and_the_cycle_wraps_at_140_with_a_bomb()
    {
        var w = World(Setup(cards: new[] { (0, 5) }));
        w.CardFlipCycle = 0;
        ParkOrb(w, 160, 64);
        w.Stage!.CardsHitTest();                       // cycle 0: (0 % 10) == 0 -> point item
        Assert.Equal(1, w.CardFlipCycle);
        Assert.Equal(ItemFlag.Splash, w.Items.Points[0].Flag);
        Assert.Equal(ItemFlag.Free, w.Items.Bombs[0].Flag);

        var b = World(Setup(cards: new[] { (0, 5) }));
        b.CardFlipCycle = 139;                         // 139 % 10 != 0: nothing
        ParkOrb(b, 160, 64);
        b.Stage!.CardsHitTest();
        Assert.Equal(140, b.CardFlipCycle);
        Assert.Equal(ItemFlag.Free, b.Items.Bombs[0].Flag);

        var c = World(Setup(cards: new[] { (0, 5) }));
        c.CardFlipCycle = 130;                         // 130 % 10 == 0, 131 < 140: point
        ParkOrb(c, 160, 64);
        c.Stage!.CardsHitTest();
        Assert.Equal(ItemFlag.Splash, c.Items.Points[0].Flag);

        var d = World(Setup(cards: new[] { (0, 5) }));
        d.CardFlipCycle = 250;                         // 250 % 10 == 0, 251 >= 140 -> bomb item, cycle = 1
        ParkOrb(d, 160, 64);
        d.Stage!.CardsHitTest();
        Assert.Equal(1, d.CardFlipCycle);
        Assert.Equal(ItemFlag.Splash, d.Items.Bombs[0].Flag);
    }

    [Fact]
    public void Bomb_picks_one_card_per_frame_by_frame_modulo_and_always_burns_a_random_draw()
    {
        var w = World(Setup(cards: new[] { (0, 0), (0, 10), (5, 5) }));
        w.Bomb.Damaging = true;
        w.BombFrame = 4;                               // 4 % 3 == 1: only card 1 is eligible
        ParkOrb(w, 600, 300);                          // nowhere near any card
        w.Rng = new IRand(77);
        var probe = new IRand(77);
        bool pass = probe.Next() % 4 != 0;
        w.Stage!.CardsHitTest();
        Assert.Equal(probe.Seed, w.Rng.Seed);          // exactly one draw (card 1), none for the other slots
        Assert.Equal(pass ? CardFlag.Flipping : CardFlag.Alive, w.Stage.CardFlagArr[1]);
        Assert.Equal(CardFlag.Alive, w.Stage.CardFlagArr[0]);
    }

    [Fact]
    public void Stage_without_cards_is_cleared_on_the_first_frame()
    {
        var g = new GameSession(new GameOptions { Stage = Setup() });
        g.Step(GameButtons.Shot);
        g.Step(None);
        Assert.Equal(SessionPhase.StageBonus, g.Phase);
        Dismiss(g);
        Assert.Equal(SessionOutcome.Cleared, g.Outcome);
    }

    // ---- Items ----

    [Fact]
    public void Point_item_falls_bounces_and_is_collected_with_a_growing_value()
    {
        var w = World(Setup(cards: new[] { (0, 0) }));
        w.Player.Left = 304;
        w.Items.AddPoint(300, 300);
        int guard = 0;
        while (w.Items.Points[0].Flag != ItemFlag.CollectedOverCap && guard++ < 200) w.Items.Update();
        Assert.Equal(ItemFlag.CollectedOverCap, w.Items.Points[0].Flag);
        Assert.Equal(1000, w.PointValue);
        Assert.Equal(1000u, w.Score);
        Assert.Equal(15, w.Items.Points[0].State);     // the collect animation already ran once on the hit frame
        for (int i = 0; i < 15; i++) w.Items.Update();
        Assert.Equal(ItemFlag.Free, w.Items.Points[0].Flag);
    }

    [Fact]
    public void Point_value_steps_by_1000_then_10000_and_caps_at_65530()
    {
        var w = World(Setup(cards: new[] { (0, 0) }));
        w.Player.Left = 304;
        foreach (var (before, after) in new (ushort, ushort)[] { (0, 1000), (9000, 10000), (10000, 20000), (50000, 65530), (65530, 65530) })
        {
            w.PointValue = before;
            w.Items.Reset();
            w.Items.AddPoint(300, 360);
            w.Items.Points[0].Flag = ItemFlag.Fall;
            w.Items.Update();
            Assert.Equal(after, w.PointValue);
        }
    }

    [Fact]
    public void Missed_point_item_resets_the_value_and_a_full_bomb_stock_pays_score()
    {
        var w = World(Setup(cards: new[] { (0, 0) }));
        w.Player.Left = 0;
        w.PointValue = 5000;
        w.Items.AddPoint(500, 350);
        int guard = 0;
        while (w.Items.Points[0].Flag != ItemFlag.Free && guard++ < 300) w.Items.Update();
        Assert.Equal(0, w.PointValue);

        w.Player.Left = 304;
        w.RemBombs = Playfield.BombsMax;
        w.Items.AddBomb(300, 340);
        guard = 0;
        while (w.Items.Bombs[0].Flag < ItemFlag.Collected && guard++ < 300) w.Items.Update();
        Assert.Equal(ItemFlag.CollectedOverCap, w.Items.Bombs[0].Flag);
        Assert.Equal(10000u, w.Score);
        Assert.Equal(Playfield.BombsMax, w.RemBombs);
    }

    // ---- Bumpers ----

    [Fact]
    public void Bumper_teleports_the_orb_clear_of_it_bounces_and_blocks_for_8_frames()
    {
        var w = World(Setup(obs: new (int, int, byte)[] { (5, 10, 1) }));   // bumper at (320, 224)
        var o = w.Orb;
        ParkOrb(w, 320, 230);
        o.VelocityX = OrbVelocityX.Right4;
        o.Y.VelocityY = 10.0;
        w.Stage!.ObstaclesUpdate(false);
        Assert.Equal(224 + 24, o.Top);                 // below the bumper: pushed to bumper.top + 24
        Assert.Equal(-10.0 * 0.78, o.Y.Force);
        Assert.Equal(OrbVelocityX.Right4, o.VelocityX); // dx == 0: direction kept
        Assert.Equal(2, w.Stage.ObsFrame[0]);
        int calls = 0;
        while (w.Stage.ObsFrame[0] != 0) { w.Stage.ObstaclesUpdate(false); calls++; }
        Assert.Equal(6, calls);                        // 3..7 then back to 0 at 8
    }

    [Fact]
    public void Bumper_hit_from_the_side_flips_only_a_4px_horizontal_velocity_away_from_it()
    {
        var w = World(Setup(obs: new (int, int, byte)[] { (5, 10, 1) }));
        ParkOrb(w, 320 - 10, 224 - 5);                 // left of and above the bumper
        w.Orb.VelocityX = OrbVelocityX.Right4;
        w.Stage!.ObstaclesUpdate(false);
        Assert.Equal(OrbVelocityX.Left4, w.Orb.VelocityX);
        Assert.Equal(224 - 24, w.Orb.Top);
        var e = World(Setup(obs: new (int, int, byte)[] { (5, 10, 1) }));
        ParkOrb(e, 320 - 10, 224 - 5);
        e.Orb.VelocityX = OrbVelocityX.Right8;         // 8 px/tick is not handled: unchanged
        e.Stage!.ObstaclesUpdate(false);
        Assert.Equal(OrbVelocityX.Right8, e.Orb.VelocityX);
    }

    // ---- Bars ----

    [Fact]
    public void Horizontal_bars_reflect_the_vertical_force_once_per_block_period()
    {
        var w = World(Setup(obs: new (int, int, byte)[] { (5, 10, 19) }));   // bottom bar at (320, 224)
        ParkOrb(w, 330, 240);
        w.Orb.Y.VelocityY = 5.0;
        w.Stage!.ObstaclesUpdate(false);
        Assert.Equal(-5.0, w.Orb.Y.Force);
        Assert.Equal(2, w.Stage.ObsFrame[0]);
        w.Orb.Y.VelocityY = 7.0;
        w.Stage.ObstaclesUpdate(false);                // still blocked
        Assert.Equal(-5.0, w.Orb.Y.Force);
        // Top bar: orb above the bar, within one tile.
        var t = World(Setup(obs: new (int, int, byte)[] { (5, 10, 18) }));
        ParkOrb(t, 320, 224 - 32);
        t.Orb.Y.VelocityY = -3.0;
        t.Stage!.ObstaclesUpdate(false);
        Assert.Equal(3.0, t.Orb.Y.Force);
        // One pixel above the allowed band: no hit.
        var n = World(Setup(obs: new (int, int, byte)[] { (5, 10, 18) }));
        ParkOrb(n, 320, 224 - 33);
        n.Stage!.ObstaclesUpdate(false);
        Assert.Equal(0, n.Stage.ObsFrame[0]);
    }

    [Fact]
    public void Vertical_bars_flip_x_and_only_one_of_a_column_reacts_at_a_time()
    {
        var w = World(Setup(obs: new (int, int, byte)[] { (5, 10, 20), (6, 10, 20) }));   // two left bars stacked
        ParkOrb(w, 300, 240);                          // within 32 of both rows
        w.Orb.VelocityX = OrbVelocityX.Right4;
        w.Stage!.ObstaclesUpdate(false);
        Assert.Equal(OrbVelocityX.Left4, w.Orb.VelocityX);   // flipped exactly once, not twice
        Assert.Equal(2, w.Stage.ObsFrame[0]);
        Assert.Equal(0, w.Stage.ObsFrame[1]);

        var z = World(Setup(obs: new (int, int, byte)[] { (5, 10, 21) }));  // right bar, orb standing still horizontally
        ParkOrb(z, 330, 230);
        z.Orb.VelocityX = OrbVelocityX.Zero;
        z.Orb.Y.VelocityY = 4.0;
        z.Stage!.ObstaclesUpdate(false);
        Assert.Equal(-4.0, z.Orb.Y.Force);             // OVX_0 reflects vertically instead
    }

    // ---- Portals ----

    [Fact]
    public void Portal_takes_the_orb_for_40_frames_and_sends_it_to_another_portal_with_ordered_draws()
    {
        var w = World(Setup(obs: new (int, int, byte)[] { (2, 2, 17), (6, 15, 17) }));
        var s = w.Stage!;
        ParkOrb(w, 64, 128);
        w.Rng = new IRand(31337);

        // Reference of the draw order, written independently of the implementation.
        var p = new IRand(31337);
        int dst = 0, loops = 0, slot = 0;
        var left = new[] { 64, 480 }; var top = new[] { 128, 256 };
        while (true)
        {
            if (p.Next() % 4 == 0 && dst != slot) break;   // both slots are portals: every dst draws
            dst++;
            if (loops > 15) { dst = slot; break; }
            if (dst >= 2) { dst = 0; loops++; }
        }
        int expectedVx = p.Next() % 5;
        int expectedForce = p.Next() % 19 - 9;

        s.ObstaclesUpdate(false);
        Assert.True(w.Orb.InPortal);
        for (int i = 2; i <= 39; i++) { s.ObstaclesUpdate(false); Assert.True(w.Orb.InPortal, $"frame {i}"); }
        s.ObstaclesUpdate(false);                          // frame 40
        Assert.False(w.Orb.InPortal);
        Assert.Equal(left[dst], w.Orb.Left);
        Assert.Equal(top[dst], w.Orb.Top);
        Assert.Equal((OrbVelocityX)expectedVx, w.Orb.VelocityX);
        Assert.Equal((double)expectedForce, w.Orb.Y.Force);
        Assert.Equal(p.Seed, w.Rng.Seed);
        // Entry portal stays blocked until frame 60.
        for (int i = 41; i < 60; i++) s.ObstaclesUpdate(false);
        Assert.NotEqual(0, s.ObsFrame[0]);
        s.ObstaclesUpdate(false);
        Assert.Equal(0, s.ObsFrame[0]);
    }

    [Fact]
    public void Life_reset_pass_clears_the_portal_state_and_releases_the_orb()
    {
        var w = World(Setup(obs: new (int, int, byte)[] { (2, 2, 17), (6, 15, 17) }));
        ParkOrb(w, 64, 128);
        w.Stage!.ObstaclesUpdate(false);
        Assert.True(w.Orb.InPortal);
        w.Stage.ObstaclesUpdate(true);
        Assert.False(w.Orb.InPortal);
        Assert.Equal(0, w.Stage.ObsFrame[0]);
    }

    // ---- Timer ----

    [Theory]
    [InlineData(0, 0, 1000)]
    [InlineData(4, 0, 2000)]
    [InlineData(5, 0, 1200)]
    [InlineData(5, 1, 1200)]
    [InlineData(9, 0, 3000)]
    [InlineData(9, 1, 2000)]
    [InlineData(18, 0, 600)]
    [InlineData(16, 1, 1800)]
    [InlineData(19, 1, 8000)]
    public void Timer_table(int stageId, int route, int expected) =>
        Assert.Equal(expected, StageTimer.InitialFor(stageId, route));

    [Fact]
    public void Timer_counts_down_by_2_every_fourth_frame_and_extends_after_a_miss()
    {
        var g = Session(Setup(cards: new[] { (9, 19) }));
        var w = g.World;
        int start = w.Timer.Value;
        w.FrameRand = 100;                             // next increments land on 101,102,103,104
        for (int i = 0; i < 4; i++) g.Step(None);
        Assert.Equal(start - 2, w.Timer.Value);
        w.Timer.Extend();
        Assert.Equal(start - 2 + 200, w.Timer.Value);
        w.Timer.Value = 0;
        w.Timer.Extend();
        Assert.Equal(1000, w.Timer.Value);
    }

    /// <summary>ASSUMPTION, not measured: HARRY UP banner = 2 x frame_delay(27) = 54 frozen ticks. TODO(T5).</summary>
    [Fact]
    public void Harry_up_freezes_the_simulation_for_the_assumed_banner_length()
    {
        var g = Session(Setup(cards: new[] { (9, 19) }));
        var w = g.World;
        w.Timer.Value = 2;
        w.FrameRand = 103;                             // next frame lands on 104: timer ticks to 0
        g.Step(None);
        Assert.Equal(SessionPhase.HarryUp, g.Phase);
        Assert.Equal(-4 + 2, w.PelletSpeed);
        int top = w.Orb.Top;
        for (int i = 0; i < GameSession.HarryUpBannerTicks - 1; i++)
        {
            g.Step(None);
            Assert.Equal(SessionPhase.HarryUp, g.Phase);
            Assert.Equal(top, w.Orb.Top);
        }
        g.Step(None);
        Assert.Equal(SessionPhase.Playing, g.Phase);
        Assert.Equal(54, GameSession.HarryUpBannerTicks);
    }

    // ---- Outcome ----

    [Fact]
    public void Outcome_never_returns_to_running_once_the_session_is_over()
    {
        var g = Session(Setup(cards: new[] { (0, 5) }));
        ParkOrb(g.World, 160, 64);
        while (g.Phase != SessionPhase.StageBonus) g.Step(None);
        Assert.Equal(SessionOutcome.Running, g.Outcome);
        Dismiss(g);
        Assert.Equal(SessionOutcome.Cleared, g.Outcome);
        ulong h = g.StateHash();
        for (int i = 0; i < 200; i++)
        {
            g.Step(GameButtons.Left | GameButtons.Shot);
            Assert.Equal(SessionOutcome.Cleared, g.Outcome);
        }
        Assert.Equal(h, g.StateHash());

        var o = Session(Setup(cards: new[] { (9, 19) }), lives: 0);
        ParkOrb(o.World, o.World.Player.Left, Playfield.PlayerTop - 8);
        o.Step(None);
        Assert.Equal(SessionOutcome.GameOver, o.Outcome);
        for (int i = 0; i < 200; i++) { o.Step(GameButtons.Shot); Assert.Equal(SessionOutcome.GameOver, o.Outcome); }
    }

    [Fact]
    public void Clearing_the_last_card_wins_over_a_simultaneous_hit_on_the_player()
    {
        var g = Session(Setup(cards: new[] { (0, 5) }), lives: 0);
        var w = g.World;
        w.Stage!.CardFlagArr[0] = CardFlag.Flipping;
        w.Stage.CardFlipFrame[0] = 29;                 // removed on the next update
        ParkOrb(w, w.Player.Left, Playfield.PlayerTop - 8);
        g.Step(None);
        Assert.Equal(SessionPhase.StageBonus, g.Phase);
    }

    [Fact]
    public void Miss_extends_the_timer()
    {
        var g = Session(Setup(cards: new[] { (9, 19) }));
        var w = g.World;
        int before = w.Timer.Value;
        ParkOrb(w, w.Player.Left, Playfield.PlayerTop - 8);
        g.Step(None);
        Assert.Equal(SessionPhase.MissAnimation, g.Phase);
        while (g.Phase == SessionPhase.MissAnimation) g.Step(None);
        Assert.True(w.Timer.Value >= before - 2 + 200 - 2);
    }

    // ---- Determinism over a busy stage ----

    static StageSetup Busy()
    {
        var st = Setup(stageId: 7, route: 1,
            cards: new[] { (0, 2), (0, 7), (1, 4), (2, 9), (3, 12), (3, 13), (4, 1), (4, 17), (5, 6), (6, 10), (7, 3), (8, 15) },
            obs: new (int, int, byte)[]
            {
                (2, 5, 1), (4, 8, 1), (5, 12, 1), (6, 3, 17), (6, 16, 17), (3, 0, 20), (3, 19, 21), (7, 8, 18), (7, 9, 19),
                (2, 14, 4), (5, 1, 9),
            });
        // 4-flip cards keep the stage alive for the whole run.
        for (int r = 0; r < StageSetup.Rows; r++)
            for (int c = 0; c < StageSetup.Cols; c++)
                if (st.Cards[r, c]) st.Obstacles[r, c] = 16;
        return st;
    }

    static ulong Run(uint seed, int ticks, List<ulong>? trace = null)
    {
        var s = new GameSession(new GameOptions { FrameRand = seed, Stage = Busy(), Rank = Rank.Hard, Bombs = 0, Lives = 1000 });
        uint x = 0x9E3779B9;
        GameButtons cur = None;
        for (int i = 0; i < ticks; i++)
        {
            x ^= x << 13; x ^= x >> 17; x ^= x << 5;
            if (i % 5 == 0)
            {
                cur = None;
                if ((x & 1) != 0) cur |= GameButtons.Left;
                if ((x & 2) != 0) cur |= GameButtons.Right;
                if ((x & 4) != 0) cur |= GameButtons.Shot;
                if ((x & 24) == 24) cur |= GameButtons.Bomb;
            }
            s.Step(cur);
            if (i % 20 == 0) trace?.Add(s.StateHash());
        }
        return s.StateHash();
    }

    [Fact]
    public void Lunatic_busy_stage_with_turrets_and_pellets_is_deterministic()
    {
        (ulong hash, int maxAlive) Go()
        {
            var s = new GameSession(new GameOptions { FrameRand = 21, Stage = Busy(), Rank = Rank.Lunatic, Lives = 1000 });
            uint x = 0x1234ABCD; GameButtons cur = None; int max = 0;
            for (int i = 0; i < 4000; i++)
            {
                x ^= x << 13; x ^= x >> 17; x ^= x << 5;
                if (i % 7 == 0) { cur = None; if ((x & 1) != 0) cur |= GameButtons.Left; if ((x & 2) != 0) cur |= GameButtons.Right; if ((x & 12) == 12) cur |= GameButtons.Shot; }
                s.Step(cur);
                max = Math.Max(max, s.World.Pellets.Alive);
            }
            return (s.StateHash(), max);
        }
        var a = Go(); var b = Go();
        Assert.Equal(a, b);
        Assert.True(a.maxAlive > 3, "turrets/revenge should have produced pellets");
    }

    [Fact]
    public void Busy_stage_is_deterministic_over_many_ticks()
    {
        var a = new List<ulong>(); var b = new List<ulong>();
        ulong ha = Run(11, 6000, a), hb = Run(11, 6000, b);
        Assert.Equal(ha, hb);
        Assert.Equal(a, b);
        Assert.True(a.Distinct().Count() > 20);
        Assert.NotEqual(ha, Run(12, 6000));
    }
}
