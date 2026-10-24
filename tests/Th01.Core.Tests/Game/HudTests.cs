using Th01.Core.Game;
using Th01.Core.Graphics;

namespace Th01.Core.Tests.Game;

/// <summary>Rules: HUD layout from th01/main/hud/hud.cpp. The record row follows the score once beaten; lives wrap after 6.</summary>
public class HudTests
{
    sealed class OneBitArt : IGameArt
    {
        public bool TryGetFrame(string sheet, int frame, out ArtFrame art)
        {
            if (sheet == "mask" && frame == 0)
            {
                var px = new byte[640 * 66];
                px[5 * 640 + 10] = 1;
                art = new ArtFrame(640, 66, px, Array.Empty<byte>(), Array.Empty<byte>());
                return true;
            }
            art = default;
            return false;
        }
        public int FrameCount(string sheet) => sheet == "mask" ? 1 : 0;
        public bool TryGetBackground(out ArtFrame art) { art = default; return false; }
    }

    sealed class Font : IFontSource
    {
        public bool TryGetGlyph(int cp, out Glyph g)
        {
            g = new Glyph(8, Enumerable.Repeat((byte)0xFF, 16).ToArray());
            return true;
        }
    }

    static GameSession Session(uint hi = 0, uint score = 0, int lives = 4, IGameArt? art = null) =>
        new(new GameOptions { FrameRand = 5, Lives = lives, Bombs = 2, HiScore = hi, Score = score }, null, art, new Font());

    static Framebuffer Render(GameSession s)
    {
        var fb = new Framebuffer();
        s.Render(fb);
        return fb;
    }

    [Fact]
    public void The_record_is_the_stored_one_until_the_score_beats_it()
    {
        var s = Session(hi: 50000, score: 1000);
        Assert.Equal(50000u, s.World.HudHiScore);
        s.World.Score = 60000;
        Assert.Equal(60000u, s.World.HudHiScore);
    }

    [Fact]
    public void Lives_wrap_to_a_second_row_after_six()
    {
        var s = Session(lives: 9);
        var fb = Render(s);
        Assert.Equal(9, fb.GetPixel(128 + 5 * 16 + 4, 4));          // 6th life, first row
        Assert.Equal(9, fb.GetPixel(128 + 2 * 16 + 4, 16 + 4));     // 9th life: column 2 of row 2 (columns 0-1 hold the bombs)
        Assert.Equal(0, fb.GetPixel(128 + 6 * 16 + 4, 4));           // nothing at a 7th column
    }

    [Fact]
    public void The_hud_mask_is_drawn_in_red()
    {
        var fb = Render(Session(art: new OneBitArt()));
        Assert.Equal(10, fb.GetPixel(10, 5));
    }

    [Fact]
    public void Record_row_and_current_row_show_digits_of_their_own_numbers()
    {
        var s = Session(hi: 1234567, score: 100);
        var fb = Render(s);
        // glyph boxes are fully lit: both rows have pixels at their own tops, and the stage digits sit at x = 608
        Assert.NotEqual(0, fb.GetPixel(256 + 4, 0));
        Assert.NotEqual(0, fb.GetPixel(256 + 4, 16));
        Assert.NotEqual(0, fb.GetPixel(608 + 4, 32));
        Assert.NotEqual(0, fb.GetPixel(600, 48));                    // the rank caption is centred on x = 600
    }
}
