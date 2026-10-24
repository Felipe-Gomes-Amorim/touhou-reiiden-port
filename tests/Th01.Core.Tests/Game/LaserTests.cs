using Th01.Core.Audio;
using Th01.Core.Game;

namespace Th01.Core.Tests.Game;

public class LaserTests
{
    static GameWorld World()
    {
        var w = new GameWorld(new GameOptions { FrameRand = 1 }, NullSoundSink.Instance);
        w.Player.Left = 304;
        return w;
    }

    [Fact]
    public void Spawn_sets_direction_speed_and_width_and_is_ignored_while_alive()
    {
        var w = World();
        var l = w.Lasers[0];
        l.Spawn(310, 100, 310, 300, 64, 9, 50, 3);             // straight down at 8 px per frame
        Assert.Equal((0, 256), (l.StepX, l.StepY));
        Assert.Equal(8, l.RayExtendSpeed);
        Assert.Equal(8, l.RayMoveoutSpeed);
        Assert.Equal(2, l.WidthCel);                           // width 3 is stored as 2
        Assert.Equal(310 << 8, l.RayStartLeft);
        Assert.True(l.Alive && l.Damaging);
        l.Spawn(500, 200, 0, 0, 8, 1, 1, 1);                   // already alive: nothing changes
        Assert.Equal(310 << 8, l.OriginLeft);
        Assert.Equal(8, l.RayExtendSpeed);
    }

    [Fact]
    public void Speed_is_divided_by_eight_with_truncation()
    {
        var w = World();
        w.Lasers[1].Spawn(100, 100, 200, 100, 70, 9, 50, 1);
        Assert.Equal(8, w.Lasers[1].RayExtendSpeed);           // 70 / 8
        w.Lasers[2].Spawn(100, 100, 100, 200, 7, 9, 50, 1);
        Assert.Equal(0, w.Lasers[2].RayExtendSpeed);
    }

    [Fact]
    public void Pool_index_wraps_at_ten()
    {
        var w = World();
        Assert.Same(w.Lasers[3], w.Lasers[13]);
    }

    [Fact]
    public void Ray_grows_per_frame_and_hits_the_player_when_a_cell_past_row_376_has_been_left()
    {
        var w = World();
        w.Lasers[0].Spawn(310, 100, 310, 300, 64, 9, 1000, 1);
        int age = 0;
        while (!w.PlayerIsHit && age < 100) { w.Lasers[0].Update(); age++; }
        // the ray must reach cell y = 378 (so that the cell it just left, 377, passes "y > player_top + 8"):
        // length 279 needed, 8 px per frame -> frame 35
        Assert.Equal(35, age);
        Assert.Equal(280, w.Lasers[0].RayLength);
    }

    [Theory]
    [InlineData(307, false)]
    [InlineData(308, true)]      // player_left + 4
    [InlineData(327, true)]
    [InlineData(328, false)]     // player_left + 24 (exclusive)
    public void Hit_window_is_the_middle_of_the_player_sprite(int x, bool hit)
    {
        var w = World();
        w.Lasers[0].Spawn(x, 100, x, 300, 8 * 40, 9, 1000, 1);   // 40 px per frame
        for (int k = 0; k < 20; k++) w.Lasers[0].Update();
        Assert.Equal(hit, w.PlayerIsHit);
    }

    [Fact]
    public void Invincible_player_and_non_damaging_laser_are_safe()
    {
        var w = World();
        w.PlayerInvincible = true;
        w.Lasers[0].Spawn(310, 100, 310, 300, 8 * 40, 9, 1000, 1);
        for (int k = 0; k < 20; k++) w.Lasers[0].Update();
        Assert.False(w.PlayerIsHit);
        var v = World();
        v.Lasers[0].Spawn(310, 100, 310, 300, 8 * 40, 9, 1000, 1);
        v.Lasers[0].Damaging = false;
        for (int k = 0; k < 20; k++) v.Lasers[0].Update();
        Assert.False(v.PlayerIsHit);
    }

    [Fact]
    public void A_ray_that_stops_above_the_row_cannot_hit()
    {
        var w = World();
        w.Lasers[0].Spawn(310, 100, 310, 300, 8 * 30, 9, 1000, 1);   // 30 px/frame
        for (int k = 0; k < 9; k++) w.Lasers[0].Update();             // length 270: end at y = 370
        Assert.False(w.PlayerIsHit);
    }

    [Fact]
    public void After_the_move_out_age_the_start_slides_away_and_the_laser_dies_leaving_the_field()
    {
        var w = World();
        w.PlayerInvincible = true;
        var l = w.Lasers[0];
        l.Spawn(310, 100, 310, 300, 64, 9, 5, 1);
        for (int k = 0; k < 5; k++) l.Update();                       // grows for 5 frames
        Assert.Equal(40, l.RayLength);
        Assert.Equal(100 << 8, l.RayStartY);
        l.Update();                                                   // age 5 >= 5: the start moves by 8 px
        Assert.Equal(40, l.RayLength);                                // no longer growing
        Assert.Equal(108 << 8, l.RayStartY);
        int guard = 0;
        while (l.Alive && guard++ < 200) l.Update();
        Assert.False(l.Alive);
        Assert.True(guard < 50);                                      // left through the bottom at y >= 400
        l.Spawn(200, 100, 200, 200, 64, 9, 5, 1);                     // the slot can be reused
        Assert.True(l.Alive);
    }

    [Fact]
    public void Diagonal_ray_follows_the_unit_vector_and_is_deterministic()
    {
        ulong Run()
        {
            var w = World();
            w.PlayerInvincible = true;
            for (int i = 0; i < 6; i++)
                w.Lasers[i].Spawn(100 + i * 80, 80, 320, 380, 8 * (4 + i), 5 + i, 20 + i * 5, 1 + i % 3);
            for (int k = 0; k < 200; k++) w.Lasers.UpdateAll();
            var h = new StateHasher();
            w.Lasers.Hash(ref h);
            return h.Value;
        }
        Assert.Equal(Run(), Run());
        var l = World().Lasers[0];
        l.Spawn(0, 0, 100, 100, 8, 1, 10, 1);
        Assert.Equal((181, 181), (l.StepX, l.StepY));                 // 45 degrees: 181 / 256
    }

    [Fact]
    public void Session_updates_all_lasers_every_tick()
    {
        var g = new GameSession(new GameOptions { FrameRand = 3 });
        g.World.PlayerInvincible = true;
        g.World.Lasers[0].Spawn(100, 100, 100, 300, 64, 1, 100, 1);
        for (int k = 0; k < 4; k++) g.Step(Th01.Core.Input.GameButtons.Shot);
        Assert.True(g.World.Lasers[0].Age > 0);
    }
}
