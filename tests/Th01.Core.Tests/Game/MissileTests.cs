using Th01.Core.Audio;
using Th01.Core.Game;

namespace Th01.Core.Tests.Game;

public class MissileTests
{
    sealed class SeSink : ISoundSink
    {
        public readonly List<int> Played = new();
        public void PlayBgm(string trackFileName) { }
        public void StopBgm() { }
        public void FadeOutBgm() { }
        public void PlaySe(int effectId) => Played.Add(effectId);
    }

    static GameWorld World(SeSink? sink = null, uint frameRand = 0)
    {
        var w = new GameWorld(new GameOptions { FrameRand = frameRand }, sink ?? new SeSink());
        w.FrameRand = frameRand;
        w.Player.Left = 304;
        return w;
    }

    [Fact]
    public void Missile_moves_in_a_straight_line_in_subpixels()
    {
        var w = World();
        w.Missiles.Add(100, 100, 24, -8);               // 1.5 px right, 0.5 px up per frame
        for (int k = 0; k < 4; k++) w.Missiles.Update();
        Assert.Equal((100 * 16 + 96, 100 * 16 - 32), (w.Missiles.CurLeft[0], w.Missiles.CurTop[0]));
        Assert.Equal(1, w.Missiles.Alive);
    }

    [Fact]
    public void Pool_has_50_slots_and_reset_frees_them()
    {
        var w = World();
        for (int k = 0; k < 60; k++) w.Missiles.Add(100, 100, 0, 0);
        Assert.Equal(50, w.Missiles.Alive);
        w.Missiles.Reset();
        Assert.Equal(0, w.Missiles.Alive);
    }

    [Theory]
    [InlineData(623, 100, 16, 0, false)]     // lands on 624, the last inside column
    [InlineData(624, 100, 16, 0, true)]      // 625: explodes
    [InlineData(1, 100, -16, 0, false)]      // lands on 0
    [InlineData(1, 100, -32, 0, true)]       // -1
    [InlineData(300, 65, 0, -16, false)]     // lands on row 64, the first one
    [InlineData(300, 64, 0, -16, true)]      // 63
    [InlineData(300, 64, 0, -1, true)]       // 1/16 px above row 64 already counts as 63 (floor)
    [InlineData(300, 398, 0, 16, false)]     // 399 is the last row
    [InlineData(300, 399, 0, 16, true)]      // 400
    public void Field_edges_are_inclusive_and_trigger_the_explosion(int x, int y, int vx, int vy, bool explodes)
    {
        var w = World();
        w.PlayerInvincible = true;
        w.Missiles.Add(x, y, vx, vy);
        w.Missiles.Update();
        Assert.Equal(explodes ? (int)MissileState.Hit : (int)MissileState.Moving, w.Missiles.Flag[0]);
    }

    [Fact]
    public void Exploding_missile_records_the_clamped_edge_position()
    {
        var w = World();
        w.PlayerInvincible = true;
        w.Missiles.Add(620, 200, 100, 0);               // leaves through the right edge
        w.Missiles.Update();
        Assert.Equal(624 << 4, w.Missiles.PrevLeft[0]);
        var v = World();
        v.PlayerInvincible = true;
        v.Missiles.Add(300, 396, 0, 100);               // bottom: explodes at 400 - 8
        v.Missiles.Update();
        Assert.Equal(392 << 4, v.Missiles.PrevTop[0]);
    }

    [Fact]
    public void Explosion_advances_only_on_the_frames_whose_parity_matches_the_slot_and_ends_with_sound_7()
    {
        var sink = new SeSink();
        var w = World(sink, frameRand: 0);
        w.PlayerInvincible = true;
        w.Missiles.Add(620, 200, 100, 0);               // slot 0
        w.Missiles.Add(620, 220, 100, 0);               // slot 1
        w.Missiles.Update();                            // both explode (Hit = 2); parity 0 -> slot 0 only advances later
        Assert.Equal(new[] { 2, 2 }, new[] { w.Missiles.Flag[0], w.Missiles.Flag[1] });
        w.Missiles.Update();                            // frame_rand still even: slot 0 -> 3, slot 1 stays
        Assert.Equal(new[] { 3, 2 }, new[] { w.Missiles.Flag[0], w.Missiles.Flag[1] });
        w.Missiles.Update();                            // slot 0 -> 4 > 3: freed, SE 7
        Assert.Equal(0, w.Missiles.Flag[0]);
        Assert.Equal(new[] { 7 }, sink.Played);
        w.FrameRand = 1;                                // odd frame: slot 1 advances now
        w.Missiles.Update();
        Assert.Equal(3, w.Missiles.Flag[1]);
        w.Missiles.Update();
        Assert.Equal(0, w.Missiles.Flag[1]);
        Assert.Equal(new[] { 7, 7 }, sink.Played);
    }

    [Theory]
    [InlineData(304 - 8, 400, false)]               // strict: x must be > player_left - 8
    [InlineData(304 - 7, 380, true)]
    [InlineData(304 + 23, 380, true)]
    [InlineData(304 + 24, 380, false)]              // and < player_left + 24
    [InlineData(304, 368 + 31, true)]               // y < player_top + 32
    [InlineData(304, 368 + 32, false)]
    [InlineData(304, 368 - 7, true)]                // y > player_top - 8
    [InlineData(304, 368 - 8, false)]
    public void Player_hitbox_is_46_pixels_around_the_centre(int x, int y, bool hit)
    {
        var w = World();
        w.Missiles.Add(x, y, 0, 0);
        w.Missiles.Update();
        Assert.Equal(hit, w.PlayerIsHit);
    }

    [Fact]
    public void Invincibility_protects_and_exploding_missiles_still_hurt()
    {
        var w = World();
        w.PlayerInvincible = true;
        w.Missiles.Add(304, 380, 0, 0);
        w.Missiles.Update();
        Assert.False(w.PlayerIsHit);

        var v = World();
        v.Missiles.Add(620, 380, 0, 0);
        v.Missiles.Flag[0] = (int)MissileState.Hit;     // already exploding, standing on the player
        v.Missiles.CurLeft[0] = 310 << 4; v.Missiles.CurTop[0] = 380 << 4;
        v.Missiles.Update();
        Assert.True(v.PlayerIsHit);
    }

    [Theory]
    [InlineData(16, 0, 0)]
    [InlineData(0, 16, 4)]
    [InlineData(-16, 0, 8)]
    [InlineData(0, -16, 12)]
    [InlineData(16, 16, 2)]
    public void Direction_sprite_is_the_heading_in_sixteen_steps(int vx, int vy, int cel)
    {
        var w = World();
        w.Missiles.Add(100, 100, vx, vy);
        Assert.Equal(cel, w.Missiles.DirectionCel(0));
    }

    [Fact]
    public void A_volley_is_deterministic()
    {
        ulong Run()
        {
            var w = World();
            w.PlayerInvincible = true;
            for (int k = 0; k < 300; k++)
            {
                w.FrameRand++;
                if (k % 10 == 0) w.Missiles.Add(320, 150, (k % 7 - 3) * 12, 8 + k % 5);
                w.Missiles.Update();
            }
            var h = new StateHasher();
            w.Missiles.Hash(ref h);
            return h.Value;
        }
        Assert.Equal(Run(), Run());
    }
}
