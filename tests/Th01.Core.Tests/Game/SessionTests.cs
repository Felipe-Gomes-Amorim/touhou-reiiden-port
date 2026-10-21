using Th01.Core.Audio;
using Th01.Core.Game;
using Th01.Core.Graphics;
using Th01.Core.Input;

namespace Th01.Core.Tests.Game;

public class SessionTests
{
    const GameButtons L = GameButtons.Left;
    const GameButtons R = GameButtons.Right;
    const GameButtons Z = GameButtons.Shot;
    const GameButtons X = GameButtons.Bomb;

    static GameSession New(uint seed = 99, int lives = 4, int bombs = 1) =>
        new(new GameOptions { FrameRand = seed, Lives = lives, Bombs = bombs });

    static void Run(GameSession s, GameButtons b, int ticks)
    {
        for (int i = 0; i < ticks; i++) s.Step(b);
    }

    /// <summary>Shot press leaves WaitForShot; returns with the session in Playing and the shot key released.</summary>
    static GameSession Started(uint seed = 99, int lives = 4, int bombs = 1)
    {
        var s = New(seed, lives, bombs);
        s.Step(Z);
        s.Step(GameButtons.None);
        Assert.Equal(SessionPhase.Playing, s.Phase);
        return s;
    }

    // ---- Determinism ----

    static ulong RunScript(uint seed, int ticks, List<ulong>? trace = null)
    {
        var s = New(seed);
        uint x = 0x2545F491;   // test-local xorshift; independent of the game RNG
        GameButtons cur = GameButtons.None;
        for (int i = 0; i < ticks; i++)
        {
            x ^= x << 13; x ^= x >> 17; x ^= x << 5;
            if (i % 7 == 0)   // change the held set every few ticks, like a human
            {
                cur = GameButtons.None;
                if ((x & 1) != 0) cur |= L;
                if ((x & 2) != 0) cur |= R;
                if ((x & 4) != 0) cur |= Z;
                if ((x & 8) != 0) cur |= X;
            }
            s.Step(cur);
            if (i % 50 == 0) trace?.Add(s.StateHash());
        }
        return s.StateHash();
    }

    [Fact]
    public void Same_seed_and_inputs_give_the_same_hash_at_every_checkpoint()
    {
        var a = new List<ulong>(); var b = new List<ulong>();
        Assert.Equal(RunScript(5, 4000, a), RunScript(5, 4000, b));
        Assert.Equal(a, b);
        Assert.True(a.Distinct().Count() > 10, "hash should evolve");
    }

    [Fact]
    public void Different_seed_changes_the_trajectory()
    {
        Assert.NotEqual(RunScript(5, 4000), RunScript(6, 4000));
    }

    [Fact]
    public void Render_does_not_touch_simulation_state()
    {
        var s = Started();
        Run(s, L | Z, 30);
        ulong before = s.StateHash();
        var fb = new Framebuffer();
        s.Render(fb);
        s.Render(fb);
        Assert.Equal(before, s.StateHash());
    }

    // ---- Player ----

    [Fact]
    public void Player_dashes_four_pixels_per_tick_and_clamps_to_the_field()
    {
        var s = New();
        Run(s, L, 10);
        Assert.Equal(Playfield.PlayerLeftStart - 40, s.World.Player.Left);
        Run(s, L, 100);
        Assert.Equal(0, s.World.Player.Left);
        Run(s, R, 300);
        Assert.Equal(Playfield.PlayerLeftMax, s.World.Player.Left);
    }

    [Fact]
    public void Nothing_but_the_player_moves_before_the_first_shot()
    {
        var s = New();
        int top = s.World.Orb.Top;
        Run(s, L, 20);
        Assert.Equal(SessionPhase.WaitForShot, s.Phase);
        Assert.Equal(top, s.World.Orb.Top);
    }

    [Fact]
    public void First_shot_press_starts_the_stage_and_fires_one_shot()
    {
        var s = New();
        s.Step(Z);                              // press: player enters the shoot mode
        Assert.Equal(SessionPhase.WaitForShot, s.Phase);
        s.Step(Z);                              // stage starts, shot slot gets filled this tick
        Assert.Equal(SessionPhase.Playing, s.Phase);
        var sh = s.World.Shots;
        Assert.True(sh.Moving[0]);
        Assert.Equal(Playfield.PlayerLeftStart + 8, sh.Left[0]);
        Assert.Equal(Playfield.PlayerTop - 12, sh.Top[0]);   // added then moved up in the same tick
    }

    sealed class CountingSink : ISoundSink
    {
        public int Shots;
        public void PlayBgm(string trackFileName) { }
        public void StopBgm() { }
        public void FadeOutBgm() { }
        public void PlaySe(int effectId) { if (effectId == 1) Shots++; }
    }

    [Fact]
    public void Holding_the_shot_key_does_not_autofire()
    {
        var sink = new CountingSink();
        var s = new GameSession(new GameOptions { FrameRand = 3 }, sink);
        s.Step(Z); s.Step(GameButtons.None);   // start the stage: one shot
        Assert.Equal(1, sink.Shots);
        Run(s, GameButtons.None, 5);            // let the shoot animation finish; a press during it is swallowed
        Run(s, Z, 120);                         // one fresh press, then held
        Assert.Equal(2, sink.Shots);
        Run(s, GameButtons.None, 5);
        Run(s, Z, 5);                           // a second press fires again
        Assert.Equal(3, sink.Shots);
    }

    [Fact]
    public void Swing_keeps_player_in_the_swing_mode_for_23_frames_and_repels_the_orb()
    {
        var s = Started();
        var w = s.World;
        Run(s, GameButtons.None, 5);   // finish the shoot animation first
        // Park the orb right above the player, falling slowly.
        w.Orb.Left = w.Player.Left; w.Orb.Top = Playfield.PlayerTop - 20; w.Orb.Y.Force = 0; w.Orb.ForceFrame = 0;
        s.Step(X);   // strike press with no direction: swing + invincible against the orb
        Assert.False(w.PlayerIsHit);
        // First frame is the swing's first update: friction (frame counter) is small, so the orb is pushed up.
        Run(s, GameButtons.None, 3);
        Assert.False(w.PlayerIsHit);
        Assert.True(w.Orb.Y.Force < 0);
        Assert.True(w.PlayerDeflecting);
        Run(s, GameButtons.None, 30);
        Assert.False(w.PlayerDeflecting);
    }

    // ---- Bomb ----

    [Fact]
    public void Left_right_plus_shot_starts_a_bomb_that_runs_140_frames()
    {
        var s = New(bombs: 2);
        var w = s.World;
        s.Step(L | R | Z);                // sensed: bomb request is queued by the player update
        s.Step(L | R | Z);                // stage starts; bomb frame 0 runs
        Assert.Equal(1, w.RemBombs);
        Assert.True(w.PlayerInvincible);
        Assert.True(w.PlayerDeflecting);

        int guard = 0;
        while (!w.Bomb.Damaging && guard++ < 200) s.Step(L | R);
        Assert.Equal(51u, w.BombFrame);   // frame 50 was evaluated, counter advanced after it
        while (w.Bomb.Damaging && guard++ < 400) s.Step(L | R);
        Assert.Equal(100, w.PlayerInvincibilityTime);
        // The 100-tick grace period counts down, then the flag drops.
        Run(s, GameButtons.None, 100);
        Assert.False(w.PlayerInvincible);
        Assert.False(w.PlayerDeflecting);
    }

    [Fact]
    public void Bomb_needs_a_bomb_in_stock()
    {
        var s = New(bombs: 0);
        s.Step(L | R | Z);
        s.Step(L | R | Z);
        Assert.Equal(0, s.World.RemBombs);
        Assert.False(s.World.PlayerInvincible);
    }

    // ---- Miss / lives ----

    static GameSession Hit(GameSession s)
    {
        var w = s.World;
        w.Orb.Left = w.Player.Left; w.Orb.Top = Playfield.PlayerTop - 8; w.Orb.Y.Force = 0; w.Orb.ForceFrame = 0;
        s.Step(GameButtons.None);
        return s;
    }

    [Fact]
    public void Orb_touching_the_player_costs_a_life_and_runs_the_miss_animation()
    {
        var s = Started(lives: 3, bombs: 1);
        Hit(s);
        var w = s.World;
        Assert.Equal(SessionPhase.MissAnimation, s.Phase);
        Assert.Equal(2, w.RemLives);

        int ticks = 0;
        while (s.Phase == SessionPhase.MissAnimation && ticks < 500) { s.Step(GameButtons.None); ticks++; }
        Assert.Equal(SessionPhase.Playing, s.Phase);
        // 16 shake iterations * 2 ticks, then 30 effect frames whose delays sum to 50 ticks.
        Assert.Equal(32 + 50, ticks);
        Assert.Equal(2, w.RemBombs);                       // +1 bomb per life lost
        Assert.False(w.PlayerIsHit);
        Assert.Equal(Playfield.PlayerMissInvincibilityFrames, w.PlayerInvincibilityTime);
        Assert.True(w.PlayerInvincible);
        Assert.Equal(Playfield.PlayerLeftStart, w.Player.Left);   // move-out then move-in lands back on the start column
    }

    /// <summary>
    /// ASSUMPTION, not measured: the shake lasts 16 iterations x 2 ticks and each effect frame lasts its
    /// frame_delay in ticks. T5 calibrates against the original; update the two constants when it does.
    /// </summary>
    [Fact]
    public void Miss_animation_duration_assumption_is_82_ticks()
    {
        Assert.Equal(16 * 2, GameSession.MissShakeTicks);
        Assert.Equal(50, GameSession.MissEffectTicks);
        Assert.Equal(82, GameSession.MissTotalTicks);
    }

    [Fact]
    public void Miss_refill_is_capped_at_five_bombs()
    {
        var s = Started(bombs: 5);
        Hit(s);
        while (s.Phase == SessionPhase.MissAnimation) s.Step(GameButtons.None);
        Assert.Equal(5, s.World.RemBombs);
    }

    [Fact]
    public void Post_miss_invincibility_ignores_orb_contact_for_149_ticks()
    {
        var s = Started();
        Hit(s);
        while (s.Phase == SessionPhase.MissAnimation) s.Step(GameButtons.None);
        var w = s.World;
        for (int i = 0; i < 149; i++)
        {
            w.Orb.Left = w.Player.Left; w.Orb.Top = Playfield.PlayerTop - 8; w.Orb.Y.Force = 0; w.Orb.ForceFrame = 0;
            s.Step(GameButtons.None);
            Assert.False(w.PlayerIsHit, $"tick {i}");
        }
    }

    [Fact]
    public void Hit_with_no_lives_left_is_game_over()
    {
        var s = Started(lives: 0);
        Hit(s);
        Assert.Equal(SessionPhase.GameOver, s.Phase);
    }

    [Fact]
    public void Extend_grants_a_life_at_each_400000_points_up_to_the_cap()
    {
        var s = Started(lives: 4);
        var w = s.World;
        w.Score = 400000;
        s.Step(GameButtons.None);
        Assert.Equal(5, w.RemLives);
        Assert.Equal(2, w.ExtendNext);
        s.Step(GameButtons.None);
        Assert.Equal(5, w.RemLives);   // threshold passed, no repeat
        w.Score = 1_200_000;
        w.RemLives = Playfield.LivesMax;
        s.Step(GameButtons.None);
        Assert.Equal(Playfield.LivesMax, w.RemLives);
        Assert.Equal(3, w.ExtendNext); // the counter still advances at the cap
    }

    // ---- Pause ----

    [Fact]
    public void Pause_freezes_the_simulation_until_pressed_again()
    {
        var s = Started();
        s.Step(GameButtons.Pause);          // frame completes, then the pause menu would appear
        Assert.True(s.World.Input.Paused);
        s.Step(GameButtons.None);
        int top = s.World.Orb.Top;
        ulong h = s.StateHash();
        Run(s, GameButtons.None, 10);
        Assert.Equal(top, s.World.Orb.Top);
        Run(s, GameButtons.Pause, 1);
        Run(s, GameButtons.None, 1);
        Assert.False(s.World.Input.Paused);
        Run(s, GameButtons.None, 1);
        Assert.NotEqual(top, s.World.Orb.Top);
        Assert.NotEqual(h, s.StateHash());
    }
}

public class InputLatchTests
{
    [Fact]
    public void Shot_is_a_latch_cleared_by_the_game_or_on_release()
    {
        var i = new InputLatch(); int d = 100;
        i.Sense(GameButtons.Shot, ref d);
        Assert.True(i.Shot);
        i.Shot = false;                              // the game consumes it
        i.Sense(GameButtons.Shot, ref d);
        Assert.False(i.Shot);                        // still held: no new edge
        i.Sense(GameButtons.None, ref d);
        i.Sense(GameButtons.Shot, ref d);
        Assert.True(i.Shot);
    }

    [Fact]
    public void Lr_follows_held_direction_keys()
    {
        var i = new InputLatch(); int d = 100;
        i.Sense(GameButtons.Left, ref d);
        Assert.Equal(InputLatch.LrLeft, i.Lr);
        i.Sense(GameButtons.Left | GameButtons.Right, ref d);
        Assert.Equal(InputLatch.LrBoth, i.Lr);
        i.Sense(GameButtons.Right, ref d);
        Assert.Equal(InputLatch.LrRight, i.Lr);
    }

    [Fact]
    public void Bomb_by_double_tapping_both_buttons_inside_the_window()
    {
        var i = new InputLatch(); int d = 0;
        void Tap(GameButtons b) { i.Sense(b, ref d); i.Sense(GameButtons.None, ref d); d++; }
        Tap(GameButtons.Shot);
        Tap(GameButtons.Bomb);
        Tap(GameButtons.Shot);
        Assert.False(i.Bomb);
        i.Sense(GameButtons.Bomb, ref d);
        Assert.True(i.Bomb);
    }

    [Fact]
    public void Late_tap_restarts_the_window()
    {
        var i = new InputLatch(); int d = 0;
        i.Sense(GameButtons.Shot, ref d); i.Sense(GameButtons.None, ref d);
        d = InputLatch.BombDoubletapWindow;    // too late: this tap counts as the first again
        i.Sense(GameButtons.Shot, ref d); i.Sense(GameButtons.None, ref d);
        Assert.Equal(0, d);
        i.Sense(GameButtons.Bomb, ref d); i.Sense(GameButtons.None, ref d);
        i.Sense(GameButtons.Bomb, ref d); i.Sense(GameButtons.None, ref d);
        i.Sense(GameButtons.Shot, ref d);
        Assert.True(i.Bomb);
    }
}
