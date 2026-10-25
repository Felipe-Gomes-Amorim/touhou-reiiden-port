using Th01.Core.Audio;
using Th01.Core.Game;
using Th01.Core.Graphics;
using Th01.Core.Input;

namespace Th01.Core.Tests.Game;

/// <summary>The stage-start banners on the text layer: the scene title (cut out of a black mask) and the red "STAGE nn".</summary>
public class EntranceBannerTests
{
    /// <summary>A font where every kanji and every letter is a single set dot in the top-left corner of its glyph.</summary>
    sealed class OneDotFont : IFontSource
    {
        public bool TryGetGlyph(int codePoint, out Glyph glyph)
        {
            bool wide = codePoint > 0xFF;
            var rows = new byte[wide ? 32 : 16];
            rows[0] = 0x80;
            glyph = new Glyph(wide ? 16 : 8, rows);
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

    static StageSetup Stage(int id) => new()
    {
        StageId = id,
        Cards = new bool[StageSetup.Rows, StageSetup.Cols],
        Obstacles = new byte[StageSetup.Rows, StageSetup.Cols],
    };

    // ---- the overlay itself ----

    [Fact]
    public void Title_masks_the_screen_in_black_and_cuts_the_glyph_out()
    {
        var fb = new Framebuffer();
        EntranceOverlay.Draw(fb, new OneDotFont(), elapsed: 0, withTitle: true, stageNumber: 1);

        Assert.Equal(TextColor.Black, fb.TextAt(0, 0));            // the whole screen is covered...
        Assert.Equal(TextColor.Black, fb.TextAt(639, 399));
        Assert.Null(fb.TextAt(192, 80));                           // ...except the one set dot: a 16x16 window
        Assert.Null(fb.TextAt(192 + 15, 80 + 15));
        Assert.Equal(TextColor.Black, fb.TextAt(192 + 16, 80));    // the next dot is not set
        Assert.Equal(TextColor.Black, fb.TextAt(192, 80 + 16));
    }

    [Fact]
    public void Each_title_character_is_shown_for_eight_ticks_and_there_are_six()
    {
        Assert.Equal(6, EntranceOverlay.TitleChars.Length);
        Assert.Equal("東方★靈異伝", EntranceOverlay.TitleChars);
        var font = new RecordingFont();
        var fb = new Framebuffer();
        for (int t = 0; t < EntranceOverlay.TitleTicks; t++) EntranceOverlay.Draw(fb, font, t, withTitle: true, stageNumber: 1);
        Assert.Equal(EntranceOverlay.TitleChars.ToCharArray(), font.Asked.Distinct().ToArray());
        Assert.All(font.Asked.GroupBy(c => c), g => Assert.Equal(8, g.Count()));
    }

    sealed class RecordingFont : IFontSource
    {
        public readonly List<char> Asked = new();
        public bool TryGetGlyph(int codePoint, out Glyph glyph)
        {
            Asked.Add((char)codePoint);
            return new OneDotFont().TryGetGlyph(codePoint, out glyph);
        }
    }

    [Fact]
    public void Banner_draws_STAGE_in_red_blocks_over_the_visible_stage_then_the_number()
    {
        var fb = new Framebuffer();
        EntranceOverlay.Draw(fb, new OneDotFont(), elapsed: 0, withTitle: false, stageNumber: 7);
        Assert.Equal(TextColor.Red, fb.TextAt(0, 80));             // first letter's dot
        Assert.Equal(TextColor.Red, fb.TextAt(15, 95));
        Assert.Null(fb.TextAt(16, 80));                            // not set, and no black mask: the stage shows through
        Assert.Null(fb.TextAt(0, 0));
        Assert.Equal(TextColor.Red, fb.TextAt(128, 80));           // second letter starts 8 dots * 16 px later
        Assert.Equal(TextColor.Red, fb.TextAt(4 * 128, 80));       // five letters fill the width

        fb.ClearTextLayer();
        EntranceOverlay.Draw(fb, new OneDotFont(), elapsed: EntranceOverlay.WordTicks, withTitle: false, stageNumber: 7);
        Assert.Null(fb.TextAt(0, 80));                             // the word is gone...
        Assert.Equal(TextColor.Red, fb.TextAt(192, 80));           // ...the two digits are centred (192 px margins)
        Assert.Equal(TextColor.Red, fb.TextAt(192 + 128, 80));

        fb.ClearTextLayer();
        EntranceOverlay.Draw(fb, new OneDotFont(), elapsed: EntranceOverlay.BannerTicks, withTitle: false, stageNumber: 7);
        Assert.False(fb.HasTextLayer);                             // nothing after the banner
    }

    [Fact]
    public void The_title_comes_first_and_the_banner_follows_it()
    {
        var fb = new Framebuffer();
        EntranceOverlay.Draw(fb, new OneDotFont(), EntranceOverlay.TitleTicks - 1, withTitle: true, stageNumber: 1);
        Assert.Equal(TextColor.Black, fb.TextAt(0, 0));            // still the mask
        fb.ClearTextLayer();
        EntranceOverlay.Draw(fb, new OneDotFont(), EntranceOverlay.TitleTicks, withTitle: true, stageNumber: 1);
        Assert.Null(fb.TextAt(0, 0));                              // no mask, red STAGE letters instead
        Assert.Equal(TextColor.Red, fb.TextAt(0, 80));
    }

    [Fact]
    public void Without_a_usable_font_nothing_is_drawn()
    {
        var fb = new Framebuffer();
        EntranceOverlay.Draw(fb, null, 0, true, 1);
        EntranceOverlay.Draw(fb, new NoGlyphs(), 0, true, 1);
        EntranceOverlay.Draw(fb, new NoGlyphs(), 60, true, 1);
        Assert.False(fb.HasTextLayer);
    }

    sealed class NoGlyphs : IFontSource
    {
        public bool TryGetGlyph(int codePoint, out Glyph glyph) { glyph = default; return false; }
    }

    // ---- the session ----

    [Fact]
    public void First_stage_of_a_scene_clicks_six_times_eight_ticks_apart_then_waits_for_the_banner()
    {
        var sink = new Clicks();
        var g = new GameSession(new GameOptions { Stage = Stage(0), Entrance = true }, sink);
        var clickTicks = new List<int>();
        for (int t = 0; t < EntranceOverlay.TotalTicks(true); t++)
        {
            int before = sink.Effects.Count;
            g.Step(GameButtons.None);
            if (sink.Effects.Count > before) clickTicks.Add(t);
        }
        Assert.Equal(new[] { 0, 8, 16, 24, 32, 40 }, clickTicks);
        Assert.All(sink.Effects, e => Assert.Equal(14, e));
        Assert.Equal(SessionPhase.WaitForShot, g.Phase);
    }

    [Theory]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(3)]
    public void Later_stages_of_a_scene_have_only_the_banner_and_no_clicks(int stageId)
    {
        var sink = new Clicks();
        var g = new GameSession(new GameOptions { Stage = Stage(stageId), Entrance = true }, sink);
        for (int t = 0; t < EntranceOverlay.TotalTicks(false) - 1; t++)
        {
            g.Step(GameButtons.None);
            Assert.Equal(SessionPhase.Entrance, g.Phase);
        }
        g.Step(GameButtons.None);
        Assert.Equal(SessionPhase.WaitForShot, g.Phase);
        Assert.Empty(sink.Effects);
    }

    [Fact]
    public void Boss_stage_shows_the_title_and_banner_before_the_boss_entrance()
    {
        var g = new GameSession(new GameOptions { Stage = Stage(StageSetup.BossStage), Entrance = true });
        Assert.Equal(SessionPhase.Entrance, g.Phase);
        for (int t = 0; t < EntranceOverlay.TotalTicks(true); t++) g.Step(GameButtons.None);
        Assert.Equal(SessionPhase.BossEntrance, g.Phase);
    }

    [Fact]
    public void Rendering_the_banner_does_not_change_the_simulation()
    {
        var plain = new GameSession(new GameOptions { Stage = Stage(0), Entrance = true });
        var drawn = new GameSession(new GameOptions { Stage = Stage(0), Entrance = true }, null, null, new OneDotFont());
        var fb = new Framebuffer();
        for (int t = 0; t < EntranceOverlay.TotalTicks(true); t++)
        {
            plain.Step(GameButtons.None); drawn.Step(GameButtons.None);
            drawn.Render(fb);
            Assert.Equal(plain.StateHash(), drawn.StateHash());
        }
        drawn.Render(fb);
        Assert.False(fb.HasTextLayer);                              // the banner leaves no trace once the stage starts
    }

    // ---- the text layer ----

    [Fact]
    public void Text_layer_covers_the_graphics_and_is_not_scrolled()
    {
        var fb = new Framebuffer();
        fb.SetPalette4(1, 0, 15, 0);
        fb.Clear(1);
        fb.TextFill(10, 20, 4, 2, TextColor.Red);
        fb.ScrollY = 7;
        var rgba = new uint[Framebuffer.Width * Framebuffer.Height];
        fb.ToRgba(rgba);
        Assert.Equal(0xFFFF0000u, rgba[20 * Framebuffer.Width + 10]);   // red block, at the same place despite the scroll
        Assert.Equal(0xFFFF0000u, rgba[21 * Framebuffer.Width + 13]);
        Assert.Equal(0xFF00FF00u, rgba[20 * Framebuffer.Width + 14]);   // the graphics (green) show elsewhere
        fb.ClearTextLayer();
        fb.ToRgba(rgba);
        Assert.Equal(0xFF00FF00u, rgba[20 * Framebuffer.Width + 10]);
    }
}
