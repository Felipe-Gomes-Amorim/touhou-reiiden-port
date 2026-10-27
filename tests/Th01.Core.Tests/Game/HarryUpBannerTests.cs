using Th01.Core.Audio;
using Th01.Core.Game;
using Th01.Core.Graphics;
using Th01.Core.Input;

namespace Th01.Core.Tests.Game;

/// <summary>The red "HARRY" then "UP" banner when the stage timer runs out (same text-layer routine as "STAGE nn").</summary>
public class HarryUpBannerTests
{
    sealed class OneDotFont : IFontSource
    {
        public bool TryGetGlyph(int codePoint, out Glyph glyph)
        {
            var rows = new byte[16];
            rows[0] = 0x80;
            glyph = new Glyph(8, rows);
            return true;
        }
    }

    sealed class Clicks : ISoundSink
    {
        public readonly List<int> Effects = new();
        public void PlayBgm(string trackFileName) { }
        public void StopBgm() { }
        public void FadeOutBgm() { }
        public void PlaySe(int effectId) => Effects.Add(effectId);
    }

    static GameSession HarryUpSession(IFontSource? font, ISoundSink? sink = null)
    {
        var cards = new bool[StageSetup.Rows, StageSetup.Cols];
        cards[9, 19] = true;
        var setup = new StageSetup { StageId = 1, Cards = cards, Obstacles = new byte[StageSetup.Rows, StageSetup.Cols] };
        var g = new GameSession(new GameOptions { FrameRand = 5, Stage = setup }, sink, null, font);
        g.Step(GameButtons.Shot);                         // the stage starts on the first shot
        g.Step(GameButtons.None);
        for (int i = 0; i < 4; i++) g.Step(GameButtons.None);   // let the shoot animation end
        Assert.Equal(SessionPhase.Playing, g.Phase);
        g.World.Timer.Value = 2;
        g.World.FrameRand = 103;                         // the next frame lands on 104: the timer ticks to 0
        g.Step(GameButtons.None);
        return g;
    }

    [Fact]
    public void Harry_is_shown_for_27_ticks_then_up_for_27_and_then_nothing()
    {
        var font = new OneDotFont();
        var fb = new Framebuffer();

        EntranceOverlay.DrawHarryUp(fb, font, 0);
        Assert.Equal(TextColor.Red, fb.TextAt(0, 80));          // first of five letters
        Assert.Equal(TextColor.Red, fb.TextAt(4 * 128, 80));    // the fifth fills the width
        Assert.Null(fb.TextAt(0, 0));                           // no black mask: the stage stays visible

        fb.ClearTextLayer();
        EntranceOverlay.DrawHarryUp(fb, font, EntranceOverlay.HarryUpWordTicks - 1);
        Assert.Equal(TextColor.Red, fb.TextAt(0, 80));          // still HARRY on the last tick

        fb.ClearTextLayer();
        EntranceOverlay.DrawHarryUp(fb, font, EntranceOverlay.HarryUpWordTicks);
        Assert.Null(fb.TextAt(0, 80));                          // HARRY is gone...
        Assert.Equal(TextColor.Red, fb.TextAt(192, 80));        // ...UP is centred (two letters)
        Assert.Equal(TextColor.Red, fb.TextAt(192 + 128, 80));
        Assert.Null(fb.TextAt(192 + 2 * 128, 80));

        fb.ClearTextLayer();
        EntranceOverlay.DrawHarryUp(fb, font, 2 * EntranceOverlay.HarryUpWordTicks);
        Assert.False(fb.HasTextLayer);
    }

    [Fact]
    public void The_session_shows_the_banner_while_frozen_and_plays_the_effect_once()
    {
        var sink = new Clicks();
        var g = HarryUpSession(new OneDotFont(), sink);
        Assert.Equal(SessionPhase.HarryUp, g.Phase);
        Assert.Equal(new[] { 17 }, sink.Effects.Where(e => e == 17).ToArray());

        var fb = new Framebuffer();
        g.Render(fb);
        Assert.Equal(TextColor.Red, fb.TextAt(0, 80));          // HARRY

        for (int i = 0; i < EntranceOverlay.HarryUpWordTicks; i++) g.Step(GameButtons.None);
        g.Render(fb);
        Assert.Equal(TextColor.Red, fb.TextAt(192, 80));        // UP

        while (g.Phase == SessionPhase.HarryUp) g.Step(GameButtons.None);
        g.Render(fb);
        Assert.False(fb.HasTextLayer);                          // gone once the game goes on
        Assert.Single(sink.Effects.Where(e => e == 17));        // and the effect was not repeated
    }

    [Fact]
    public void Drawing_the_banner_does_not_change_the_simulation()
    {
        var plain = HarryUpSession(null);
        var drawn = HarryUpSession(new OneDotFont());
        var fb = new Framebuffer();
        for (int i = 0; i < GameSession.HarryUpBannerTicks + 5; i++)
        {
            plain.Step(GameButtons.None); drawn.Step(GameButtons.None);
            drawn.Render(fb);
            Assert.Equal(plain.StateHash(), drawn.StateHash());
        }
    }
}
