using Th01.Core.Graphics;

namespace Th01.Core.Game;

/// <summary>
/// The two full-screen banners the original puts on the text layer when a stage starts. Each glyph dot becomes a solid
/// 16x16 block (the glyph is scaled up 16 times), centred on the 640x400 screen.
/// 1) On the first stage of a scene and on boss stages: the title, one character at a time (6 characters, 8 frames
///    each, a click for each). The whole screen is black except the glyph, which is cut out so the stage shows through.
/// 2) Always: the red letters "STAGE" for 35 frames, then the stage number as two red digits for 35 frames, drawn over
///    the visible stage.
/// ref: th01/main_01.cpp (stage_entrance, stage_num_animate), th01/shiftjis/entrance.hpp, th01/hardware/tram_x16.cpp
/// </summary>
public static class EntranceOverlay
{
    /// <summary>The title characters, in the order they appear.</summary>
    public const string TitleChars = "東方★靈異伝";

    public const int TitleCharTicks = 8;
    public const int TitleTicks = 6 * TitleCharTicks;
    public const int WordTicks = 35;
    public const int NumberTicks = 35;
    public const int BannerTicks = WordTicks + NumberTicks;
    public const int HarryUpWordTicks = 27;

    const int Dot = 16;          // one glyph dot = one 16x16 block
    const int Top = 80;          // glyph row 0 is on text row 5
    const int KanjiLeft = 192;   // (640 - 16 dots * 16) / 2

    /// <summary>Total ticks of the entrance: the banner, plus the title when <paramref name="withTitle"/>.</summary>
    public static int TotalTicks(bool withTitle) => BannerTicks + (withTitle ? TitleTicks : 0);

    /// <summary>
    /// Draws the banner for tick <paramref name="elapsed"/> (0-based since the entrance began) onto the text layer.
    /// Draws nothing when the font lacks a needed glyph, so the stage is simply visible.
    /// </summary>
    public static void Draw(Framebuffer fb, IFontSource? font, int elapsed, bool withTitle, int stageNumber)
    {
        if (font == null || elapsed < 0) return;
        int t = elapsed;
        if (withTitle)
        {
            if (t < TitleTicks) { DrawTitleChar(fb, font, TitleChars[t / TitleCharTicks]); return; }
            t -= TitleTicks;
        }
        if (t < WordTicks) DrawAnk(fb, font, "STAGE", 0);
        else if (t < BannerTicks) DrawAnk(fb, font, $"{Math.Clamp(stageNumber, 0, 99):00}", KanjiLeft);
    }

    /// <summary>The red "HARRY" then "UP" banner (27 ticks each), the same text-layer routine as "STAGE nn". ref: th01/main/stage/timer.cpp harryup_animate</summary>
    public static void DrawHarryUp(Framebuffer fb, IFontSource? font, int elapsed)
    {
        if (font == null || elapsed < 0) return;
        if (elapsed < HarryUpWordTicks) DrawAnk(fb, font, "HARRY", 0);
        else if (elapsed < 2 * HarryUpWordTicks) DrawAnk(fb, font, "UP", KanjiLeft);
    }

    static void DrawTitleChar(Framebuffer fb, IFontSource font, char c)
    {
        if (!font.TryGetGlyph(c, out var g) || g.Width != 16) return;
        fb.TextFill(0, 0, Framebuffer.Width, Framebuffer.Height, TextColor.Black);
        var rows = g.Rows.Span;
        for (int r = 0; r < Glyph.Height; r++)
            for (int d = 0; d < 16; d++)
                if ((rows[r * 2 + d / 8] >> (7 - d % 8) & 1) != 0)
                    fb.TextClear(KanjiLeft + d * Dot, Top + r * Dot, Dot, Dot);
    }

    static void DrawAnk(Framebuffer fb, IFontSource font, string text, int left)
    {
        var glyphs = new Glyph[text.Length];
        for (int i = 0; i < text.Length; i++)
            if (!font.TryGetGlyph(text[i], out glyphs[i]) || glyphs[i].Width != 8) return;
        for (int i = 0; i < glyphs.Length; i++)
        {
            var rows = glyphs[i].Rows.Span;
            for (int r = 0; r < Glyph.Height; r++)
                for (int d = 0; d < 8; d++)
                    if ((rows[r] >> (7 - d) & 1) != 0)
                        fb.TextFill(left + (i * 8 + d) * Dot, Top + r * Dot, Dot, Dot, TextColor.Red);
        }
    }
}
