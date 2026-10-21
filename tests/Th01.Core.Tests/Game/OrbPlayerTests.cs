using Th01.Core.Audio;
using Th01.Core.Game;

namespace Th01.Core.Tests.Game;

public class OrbPlayerTests
{
    static GameWorld NewWorld(uint seed = 1234)
    {
        var w = new GameWorld(new GameOptions { FrameRand = seed }, NullSoundSink.Instance);
        w.Orb.StageStart();
        w.Rng = new Th01.Core.Rng.IRand(seed);   // the constructor already drew the initial card_flip_cycle
        return w;
    }

    // ---- Orb vertical physics (values hand-derived from th01/main/player/orb.cpp) ----

    [Fact]
    public void Orb_ascent_from_stage_start()
    {
        var w = NewWorld();
        // Force -8: velocity = ForceFrame/5 - 8 => -8 for frames 1..4, -7 for 5..9 (integer division of the frame).
        for (int f = 1; f <= 9; f++)
        {
            w.Orb.ForceFrame++;
            w.Orb.Move();
        }
        Assert.Equal(280 - 4 * 8 - 5 * 7, w.Orb.Top);
        Assert.Equal(600 - 9 * 4, w.Orb.Left);   // starts moving left at 4 px/frame
    }

    [Fact]
    public void Orb_floor_bounce_uses_restitution_and_truncates_toward_zero()
    {
        var w = NewWorld();
        var o = w.Orb;
        o.Left = 300; o.Top = 365; o.Y.Force = 10.0; o.ForceFrame = 0; o.VelocityX = OrbVelocityX.Left4;
        o.Move();   // +10 => 375 > 368: clamp and bounce with -10 * 0.78
        Assert.Equal(Playfield.OrbTopMax, o.Top);
        Assert.Equal(-10.0 * 0.78, o.Y.Force);
        Assert.Equal(0, o.ForceFrame);
        o.Move();   // velocity -7.8 truncates to -7, not -8
        Assert.Equal(Playfield.OrbTopMax - 7, o.Top);
    }

    [Fact]
    public void Orb_velocity_is_clamped_in_the_stored_copy_but_not_in_the_movement()
    {
        var w = NewWorld();
        var o = w.Orb;
        o.Y.Force = 30.0; o.ForceFrame = 0;
        int dy = o.VelocityYUpdate();
        Assert.Equal(30, dy);
        Assert.Equal(16.0, o.Y.VelocityY);
    }

    [Fact]
    public void Orb_ceiling_bounce_subtracts_quarter_of_force_frame()
    {
        var w = NewWorld();
        var o = w.Orb;
        o.Left = 300; o.Top = 65; o.Y.Force = -8.0; o.ForceFrame = 4; o.VelocityX = OrbVelocityX.Zero;
        o.Move();   // -8 + 0 => 57 < 64
        Assert.Equal(Playfield.OrbTopMin, o.Top);
        Assert.Equal(8.0 - 4 / 4, o.Y.Force);
    }

    [Fact]
    public void Orb_bounces_off_side_walls_using_current_velocity()
    {
        var w = NewWorld();
        var o = w.Orb;
        o.Left = 4; o.Top = 200; o.VelocityX = OrbVelocityX.Left4; o.Y.Force = 0; o.ForceFrame = 0;
        o.Move();
        Assert.Equal(0, o.Left);
        Assert.Equal(OrbVelocityX.Right4, o.VelocityX);
        o.Left = Playfield.OrbLeftMax - 8; o.VelocityX = OrbVelocityX.Right8;
        o.Move();
        Assert.Equal(Playfield.OrbLeftMax, o.Left);
        Assert.Equal(OrbVelocityX.Left8, o.VelocityX);
    }

    [Fact]
    public void Orb_rotation_frame_wraps_with_the_one_frame_last_cel_quirk()
    {
        var w = NewWorld();
        var o = w.Orb;
        o.Left = 300; o.Top = 200; o.Y.Force = 0; o.VelocityX = OrbVelocityX.Left4;
        var seen = new List<int>();
        for (int i = 0; i < 12; i++) { o.ForceFrame = 0; o.Move(); seen.Add(o.RotationFrame); }
        // 1..9, then 10 >= 10 wraps to 0.
        Assert.Equal(new[] { 1, 2, 3, 4, 5, 6, 7, 8, 9, 0, 1, 2 }, seen);
        o.VelocityX = OrbVelocityX.Right4; o.RotationFrame = 0;
        o.Move();
        Assert.Equal(9, o.RotationFrame);   // below 0 jumps to (cels-1)*3
    }

    [Fact]
    public void Surface_bounce_with_zero_x_velocity_rolls_two_independent_dice()
    {
        var w = NewWorld(42);
        var probe = new Th01.Core.Rng.IRand(42);
        int a = probe.Next() % 50, b = probe.Next() % 50;
        var o = w.Orb;
        o.VelocityX = OrbVelocityX.Zero; o.ForceFrame = 3; o.Y.VelocityY = 5;
        o.ForceNew(0, OrbForce.BounceFromSurface);
        var expected = a == 0 ? OrbVelocityX.Left4 : OrbVelocityX.Zero;
        if (b == 1) expected = OrbVelocityX.Right4;
        Assert.Equal(expected, o.VelocityX);
        Assert.Equal(probe.Seed, w.Rng.Seed);   // exactly two draws
    }

    // ---- Shots vs. Orb ----

    [Fact]
    public void Shot_hits_orb_and_applies_extra_orb_step()
    {
        var w = NewWorld();
        var o = w.Orb;
        o.Left = 300; o.Top = 200; o.Y.VelocityY = 0; o.ForceFrame = 0; o.VelocityX = OrbVelocityX.Left4;
        w.Shots.Add(300, 215);
        w.Shots.Update();   // moves to y=203, overlaps
        Assert.False(w.Shots.Moving[0]);
        Assert.Equal(OrbVelocityX.Right4, o.VelocityX);   // dx = 0: neither > 8 nor == 8
        Assert.Equal(-10.0, o.Y.Force);
        Assert.Equal(304, o.Left);                        // extra MoveX
        Assert.Equal(190, o.Top);                         // extra vertical step: 200 - 10
    }

    [Fact]
    public void Shot_leaves_playfield_at_the_top_and_slots_are_reused()
    {
        var w = NewWorld();
        w.Orb.Left = 0; w.Orb.Top = 300;
        w.Shots.Add(500, 70);
        w.Shots.Update();   // 58 < 64
        Assert.False(w.Shots.Moving[0]);
        for (int i = 0; i < 9; i++) w.Shots.Add(500, 300);
        Assert.Equal(8, w.Shots.Moving.Count(m => m));   // 8 slots only; the 9th is dropped
    }

    [Fact]
    public void Shot_hits_pellet_hitbox_with_inclusive_bounds_then_decays()
    {
        var w = NewWorld();
        w.Shots.Add(100, 100);
        Assert.False(w.Shots.HitTestPellet(100 + 15, 100));   // dx -15 < -(16-2)
        Assert.True(w.Shots.HitTestPellet(100 + 14, 100));    // dx -14 == -(16-2): inclusive
        Assert.Equal(1, w.Shots.DecayFrame[0]);
        for (int i = 0; i < 7; i++) w.Shots.Update();
        Assert.Equal(0, w.Shots.DecayFrame[0]);
    }

    // ---- Player vs. Orb ----

    [Fact]
    public void Lethal_test_uses_shrunken_hitbox_and_respects_invincibility()
    {
        var w = NewWorld();
        w.Player.Left = 304;
        w.Orb.Left = 304 + 23; w.Orb.Top = 368 - 15;   // |dx| 23 < 24, |dy| 15 < 16
        w.Player.OrbPlayerHitTest(OrbRepel.None);
        Assert.True(w.PlayerIsHit);

        w.PlayerIsHit = false;
        w.Orb.Left = 304 + 24;                          // strict: 24 is not < 24
        w.Player.OrbPlayerHitTest(OrbRepel.None);
        Assert.False(w.PlayerIsHit);

        w.Orb.Left = 304;
        w.PlayerInvincible = true;
        w.Player.OrbPlayerHitTest(OrbRepel.None);
        Assert.False(w.PlayerIsHit);
    }

    [Fact]
    public void Repel_pushes_orb_away_with_friction_halved()
    {
        var w = NewWorld();
        w.Player.Left = 304;
        w.Orb.Left = 300; w.Orb.Top = 360;   // within |dx|<40, |dy|<32; player is right of orb
        w.Player.OrbPlayerHitTest(10);
        Assert.Equal(OrbVelocityX.Left4, w.Orb.VelocityX);
        Assert.Equal(10 / 2 + -13, w.Orb.Y.Force);
        Assert.Equal(0, w.Orb.ForceFrame);

        w.Orb.Left = 310;
        w.Player.OrbPlayerHitTest(1);
        Assert.Equal(OrbVelocityX.Right4, w.Orb.VelocityX);
        Assert.Equal(-13.0, w.Orb.Y.Force);    // friction 1 / 2 == 0
    }

    [Fact]
    public void Repel_with_orb_exactly_above_draws_two_rolls_and_never_picks_right()
    {
        var w = NewWorld(7);
        var probe = new Th01.Core.Rng.IRand(7);
        int a = probe.Next() % 8, b = probe.Next() % 8;
        w.Player.Left = 304;
        w.Orb.Left = 304; w.Orb.Top = 360;
        w.Player.OrbPlayerHitTest(4);
        var expected = (a == 0 || b == 4) ? OrbVelocityX.Left4 : OrbVelocityX.Zero;
        Assert.Equal(expected, w.Orb.VelocityX);
        Assert.Equal(probe.Seed, w.Rng.Seed);
    }

    [Fact]
    public void Fixed_direction_repel_sets_speed_and_force()
    {
        var w = NewWorld();
        w.Player.Left = 304;
        w.Orb.Left = 330; w.Orb.Top = 350;
        w.Player.OrbPlayerHitTest(OrbRepel.ThreeX8Right);
        Assert.Equal(OrbVelocityX.Right8, w.Orb.VelocityX);
        Assert.Equal(-10.0, w.Orb.Y.Force);
        w.Orb.Left = 200;   // out of range: untouched
        w.Orb.Y.Force = 3.0;
        w.Player.OrbPlayerHitTest(OrbRepel.ThreeX8Left);
        Assert.Equal(3.0, w.Orb.Y.Force);
    }
}
