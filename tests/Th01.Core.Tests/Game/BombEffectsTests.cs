using Th01.Core.Game;
using Th01.Core.Graphics;

namespace Th01.Core.Tests.Game;

/// <summary>Drawing of the bomb: kuji-in rings, inverted squares and shake; the palette flash of the original is left out on purpose.</summary>
public class BombEffectsTests
{
    sealed class Art : IGameArt
    {
        public int FrameCount(string sheet) => 0;
        public bool TryGetBackground(out ArtFrame art) { art = default; return false; }
        public bool TryGetFrame(string sheet, int frame, out ArtFrame art)
        {
            art = default;
            if (sheet is not (BombEffects.Kuji1Sheet or BombEffects.Kuji2Sheet)) return false;
            var on = Enumerable.Repeat((byte)1, 32 * 32).ToArray();
            art = new ArtFrame(32, 32, on, on, Array.Empty<byte>());
            return true;
        }
    }

    static Framebuffer Screen()
    {
        var fb = new Framebuffer();
        for (int i = 0; i < Framebuffer.ColorCount; i++) fb.SetPalette4(i, i, i, i);
        return fb;
    }

    [Fact]
    public void The_shake_follows_the_original_schedule()
    {
        Assert.Equal(0, BombEffects.ShakeLine(51u));
        Assert.Equal(0, BombEffects.ShakeLine(52u));                    // frame 52 % 16 == 4: back to the normal position
        Assert.Equal(Framebuffer.Height - 16, BombEffects.ShakeLine(56u));
        Assert.Equal(0, BombEffects.ShakeLine(60u));
        Assert.Equal(Framebuffer.Height - 8, BombEffects.ShakeLine(64u));
        Assert.Equal(Framebuffer.Height - 8, BombEffects.ShakeLine(67u));   // held until the next change
        Assert.Equal(0, BombEffects.ShakeLine(68u));
        Assert.Equal(0, BombEffects.ShakeLine((uint)Bomb.KeyframeDone));
        Assert.Equal(0, BombEffects.ShakeLine(30u));                     // none while the kuji are out
    }

    [Fact]
    public void White_drifts_towards_blue_while_the_kuji_close_in_and_is_back_after_that()
    {
        Assert.Equal(0, BombEffects.WhiteFade(1u));
        Assert.Equal(1, BombEffects.WhiteFade(2u));
        Assert.Equal(1, BombEffects.WhiteFade(4u));                     // the speed grows after the palette is set
        Assert.Equal(2, BombEffects.WhiteFade(6u));
        Assert.Equal(10, BombEffects.WhiteFade(38u));
        Assert.Equal(10, BombEffects.WhiteFade(40u));
        Assert.Equal(9, BombEffects.WhiteFade(42u));                    // the speed turns around from frame 40 on
        Assert.Equal(8, BombEffects.WhiteFade(48u));
        Assert.Equal(0, BombEffects.WhiteFade((uint)Bomb.KeyframeActive));
        Assert.Equal(0, BombEffects.WhiteFade(100u));
    }

    [Fact]
    public void Kuji_are_drawn_in_white_from_frame_two_and_only_on_even_frames()
    {
        var effects = new BombEffects();
        var art = new Art();
        var first = Screen(); effects.Draw(first, art, 1u);
        Assert.All(first.Pixels.ToArray(), p => Assert.Equal(0, p));

        var a = Screen(); effects.Draw(a, art, 20u);
        var b = Screen(); effects.Draw(b, art, 21u);
        Assert.Contains((byte)7, a.Pixels.ToArray());
        Assert.Equal(a.Pixels.ToArray(), b.Pixels.ToArray());           // 21 still shows what frame 20 put there
    }

    [Fact]
    public void No_palette_flash_at_any_frame()
    {
        var effects = new BombEffects();
        for (uint f = 50; f < (uint)Bomb.KeyframeDone; f++)
        {
            var fb = Screen();
            effects.Draw(fb, new Art(), f);
            for (int i = 0; i < Framebuffer.ColorCount; i++)
                Assert.Equal(0xFF000000u | (uint)(i * 17 * 0x010101), fb.Palette[i]);
        }
    }

    [Fact]
    public void Squares_flip_plane_B_from_frame_fifty_one_and_stay_until_the_bomb_ends()
    {
        var effects = new BombEffects();
        var before = Screen(); effects.Draw(before, null, 50u);
        Assert.All(before.Pixels.ToArray(), p => Assert.Equal(0, p));

        var fb = Screen(); effects.Draw(fb, null, 51u);
        int flipped = fb.Pixels.ToArray().Count(p => p == 1);
        Assert.True(flipped > 0);
        Assert.All(fb.Pixels.ToArray(), p => Assert.True(p is 0 or 1));  // only bit 0 changes

        var later = Screen(); effects.Draw(later, null, 90u);
        Assert.NotEqual(flipped, later.Pixels.ToArray().Count(p => p == 1));

        var over = Screen(); effects.Draw(over, null, (uint)Bomb.KeyframeDone);
        Assert.All(over.Pixels.ToArray(), p => Assert.Equal(0, p));
    }

    [Fact]
    public void Rendering_a_frame_twice_or_after_a_gap_gives_the_same_picture()
    {
        var steady = new BombEffects();
        for (uint f = 51; f <= 80; f++) steady.Draw(Screen(), null, f);
        var a = Screen(); steady.Draw(a, null, 80u);

        var jumpy = new BombEffects();
        var b = Screen(); jumpy.Draw(b, null, 80u);
        Assert.Equal(a.Pixels.ToArray(), b.Pixels.ToArray());
    }

    [Fact]
    public void Drawing_a_bomb_does_not_change_the_simulation()
    {
        var plain = new GameSession(new GameOptions { FrameRand = 3, Lives = 3, Bombs = 3 }, null, null);
        var drawn = new GameSession(new GameOptions { FrameRand = 3, Lives = 3, Bombs = 3 }, null, new Art());
        var fb = new Framebuffer();
        plain.Step(Input.GameButtons.Shot); drawn.Step(Input.GameButtons.Shot);
        for (int i = 0; i < 200; i++)
        {
            var input = i < 4 ? Input.GameButtons.Bomb : Input.GameButtons.None;
            plain.Step(input); drawn.Step(input);
            drawn.Render(fb);
            Assert.Equal(plain.StateHash(), drawn.StateHash());
        }
    }
}
