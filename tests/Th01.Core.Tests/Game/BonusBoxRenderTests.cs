using Th01.Core.Game;
using Th01.Core.Graphics;
using Th01.Core.Input;
using Th01.Core.Sim;

namespace Th01.Core.Tests.Game;

/// <summary>The stage bonus box (opens from its middle row) and the route menu over the stage picture: drawing and timing.</summary>
public class BonusBoxRenderTests
{
    sealed class SolidFont : IFontSource
    {
        public bool TryGetGlyph(int cp, out Glyph g)
        {
            bool wide = cp > 0xFF;
            g = new Glyph(wide ? 16 : 8, Enumerable.Repeat((byte)0xFF, wide ? 32 : 16).ToArray());
            return true;
        }
    }

    /// <summary>Only a flat background of one colour, so the box (painted in colour 0) can be told from the stage.</summary>
    sealed class FlatBackground(byte color) : IGameArt
    {
        public int FrameCount(string sheet) => 0;
        public bool TryGetFrame(string sheet, int frame, out ArtFrame art) { art = default; return false; }
        public bool TryGetBackground(out ArtFrame art)
        {
            art = new ArtFrame(640, 400, Enumerable.Repeat(color, 640 * 400).ToArray(), Array.Empty<byte>(), Array.Empty<byte>());
            return true;
        }
    }

    static GameSession ClearedStage(IGameArt? art, IFontSource? font)
    {
        var cards = new bool[StageSetup.Rows, StageSetup.Cols];
        cards[0, 5] = true;
        var setup = new StageSetup { StageId = 2, Cards = cards, Obstacles = new byte[StageSetup.Rows, StageSetup.Cols] };
        var g = new GameSession(new GameOptions { Stage = setup, Lives = 3, Bombs = 1 }, null, art, font);
        g.Step(GameButtons.Shot); g.Step(GameButtons.None);
        g.World.Orb.Left = 160; g.World.Orb.Top = 64;
        for (int guard = 0; g.Phase != SessionPhase.StageBonus && guard < 500; guard++) g.Step(GameButtons.None);
        Assert.Equal(SessionPhase.StageBonus, g.Phase);
        return g;
    }

    static Framebuffer Render(GameSession g)
    {
        var fb = new Framebuffer();
        g.Render(fb);
        return fb;
    }

    [Fact]
    public void The_box_grows_from_its_middle_row_six_rows_per_tick_until_it_is_open()
    {
        var g = ClearedStage(new FlatBackground(5), null);
        var fb = Render(g);
        Assert.Equal(0, fb.GetPixel(100, 180));                  // only the middle row so far
        Assert.Equal(5, fb.GetPixel(100, 179));
        Assert.Equal(5, fb.GetPixel(100, 181));

        for (int k = 1; k <= 3; k++) g.Step(GameButtons.None);
        fb = Render(g);
        Assert.Equal(0, fb.GetPixel(100, 180 - 18));              // 6 rows per tick
        Assert.Equal(0, fb.GetPixel(100, 180 + 18));
        Assert.Equal(5, fb.GetPixel(100, 180 - 19));
        Assert.Equal(5, fb.GetPixel(100, 180 + 19));

        for (int k = 4; k <= GameSession.BonusBoxTicks; k++) g.Step(GameButtons.None);
        fb = Render(g);
        Assert.Equal(0, fb.GetPixel(100, 41));                    // fully open: 40 + 280 high, 140 rows each way
        Assert.Equal(0, fb.GetPixel(100, 319));
        Assert.Equal(5, fb.GetPixel(100, 40));
        Assert.Equal(5, fb.GetPixel(100, 320));
        Assert.Equal(5, fb.GetPixel(20, 180));                    // the box starts at x = 32
        Assert.Equal(0, fb.GetPixel(40, 180));
    }

    [Fact]
    public void Keys_are_ignored_until_the_box_is_open_and_then_a_release_closes_it()
    {
        var g = ClearedStage(null, null);
        g.Step(GameButtons.Shot); g.Step(GameButtons.None);       // too early: the original is not sensing keys yet
        Assert.Equal(SessionPhase.StageBonus, g.Phase);
        Assert.Equal(SessionOutcome.Running, g.Outcome);
        for (int i = 2; i < GameSession.BonusBoxTicks; i++) g.Step(GameButtons.Shot);
        g.Step(GameButtons.None);
        Assert.Equal(SessionPhase.StageBonus, g.Phase);           // holding through the opening does not count either
        g.Step(GameButtons.Shot); g.Step(GameButtons.None);
        Assert.Equal(SessionOutcome.Cleared, g.Outcome);
    }

    [Fact]
    public void The_text_is_written_only_once_the_box_is_open_in_red_and_white()
    {
        var g = ClearedStage(new FlatBackground(5), new SolidFont());
        static int Count(Framebuffer fb, byte color)
        {
            int n = 0;
            for (int y = 80; y < 300; y++)
                for (int x = 32; x < 352; x++)
                    if (fb.GetPixel(x, y) == color) n++;
            return n;
        }
        var fb = Render(g);
        Assert.Equal(0, Count(fb, 10));                           // no red title yet
        for (int i = 0; i < GameSession.BonusBoxTicks; i++) g.Step(GameButtons.None);
        fb = Render(g);
        Assert.True(Count(fb, 10) > 0);                           // red title and hit-key line
        Assert.True(Count(fb, 7) > 0);                            // white labels and numbers
    }

    [Fact]
    public void Drawing_the_bonus_box_does_not_change_the_simulation()
    {
        var plain = ClearedStage(null, null);
        var drawn = ClearedStage(new FlatBackground(5), new SolidFont());
        var fb = new Framebuffer();
        for (int i = 0; i < GameSession.BonusBoxTicks + 3; i++)
        {
            plain.Step(GameButtons.None); drawn.Step(GameButtons.None);
            drawn.Render(fb);
            Assert.Equal(plain.StateHash(), drawn.StateHash());
        }
    }

    [Fact]
    public void Fullwidth_numerals_are_used_when_the_texts_ask_for_them()
    {
        var wide = new UiTexts { FullwidthNumerals = true, BonusTitle = "STAGE      COMPLETE" };
        var cards = new bool[StageSetup.Rows, StageSetup.Cols]; cards[0, 5] = true;
        var setup = new StageSetup { StageId = 2, Cards = cards, Obstacles = new byte[StageSetup.Rows, StageSetup.Cols] };
        var asked = new List<int>();
        var font = new RecordingFont(asked);
        var g = new GameSession(new GameOptions { Stage = setup, Lives = 3 }, null, null, font, wide);
        g.Step(GameButtons.Shot); g.Step(GameButtons.None);
        g.World.Orb.Left = 160; g.World.Orb.Top = 64;
        for (int guard = 0; g.Phase != SessionPhase.StageBonus && guard < 500; guard++) g.Step(GameButtons.None);
        for (int i = 0; i < GameSession.BonusBoxTicks; i++) g.Step(GameButtons.None);
        asked.Clear();
        g.Render(new Framebuffer());
        Assert.Contains((int)'０', asked);                         // the fixed trailing 0 of every number
        Assert.Contains((int)'３', asked);                         // stage 3 in the title
    }

    sealed class RecordingFont(List<int> asked) : IFontSource
    {
        public bool TryGetGlyph(int cp, out Glyph g)
        {
            asked.Add(cp);
            g = new Glyph(cp > 0xFF ? 16 : 8, new byte[cp > 0xFF ? 32 : 16]);
            return true;
        }
    }
}
