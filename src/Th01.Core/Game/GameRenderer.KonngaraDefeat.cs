using Th01.Core.Graphics;

namespace Th01.Core.Game;

/// <summary>
/// Konngara's defeat (ref: th01/main/boss/b20j.cpp, the blocking sequence at the end of konngara_main()): nine full-screen
/// "kuji" images over a background that flips between black and white, a 64-tick shake while the palette fades to black, then
/// the first picture scrolls while the palette fades in and out. Drawing only.
/// </summary>
internal sealed partial class GameRenderer
{
    const int KujiFirstImage = 7;

    /// <summary>
    /// Draws the whole screen of the defeat sequence at the given tick of it (see Konngara.DefeatSequenceTicks for the measured timeline).
    /// The background (palette entry 0) blinks four times during the kuji in the original mode; with soft flashes it stays black.
    /// ASSUMPTIONS: the lit colour is white (source b20j.cpp, G=B measured); the shake over ticks 91-160 is measured (T5 13b); the stage picture is BOSS7_D1; the scroll is +32 lines a tick (T5 confirmed, no aliasing).
    /// </summary>
    void DrawKonngaraDefeat(Framebuffer fb, int tick)
    {
        DrawKonngaraDefeatScreen(fb, tick);
        if (_s.Visuals.ScreenShake && tick is >= Konngara.DefeatShakeFirst and <= Konngara.DefeatShakeLast)
            fb.ScrollY = 8 + (tick - Konngara.DefeatShakeFirst + 1) % 2 * 16;                  // measured: lines 8 and 24, every tick
    }

    void DrawKonngaraDefeatScreen(Framebuffer fb, int tick)
    {
        fb.Clear(0);
        int kujiEnd = Konngara.KujiCount * Konngara.KujiTicks, fadeStart = kujiEnd + Konngara.KujiHoldTicks, fadeEnd = fadeStart + Konngara.DefeatShakeTicks;
        if (tick < fadeEnd)
        {
            int k = Math.Min(tick / Konngara.KujiTicks, Konngara.KujiCount - 1);
            int level = !_s.Visuals.SoftFlashes && tick < kujiEnd && Konngara.KujiBackgroundLit(tick) ? PaletteMax : 0;
            fb.SetPalette4(0, level, level, level);
            DrawColorKey(fb, SlashSheet, KujiFirstImage + k);
            if (tick >= fadeStart)
            {
                int f = tick - fadeStart + 1;
                int steps = f / 4;                                                    // z_palette_black_out_step every 4 ticks
                MapPalette(fb, c => Math.Max(0, c - steps));
            }
            return;
        }
        if (tick < Konngara.DefeatPictureAppears || tick >= Konngara.DefeatBlankAt) { fb.SetPalette4(0, 0, 0, 0); return; }   // black gap and the erased end
        // the stage picture, its palette coming in and then going out again, the scroll running from tick 166 to 426
        if (Picture(ScrollPicture, out var pic))
        {
            for (int y = 0; y < Framebuffer.Height; y++)
                for (int x = 0; x < Framebuffer.Width; x++) fb.SetPixel(x, y, pic.Pixels.Span[y * pic.Width + x]);
            if (pic.Palette.Length == 3 * Framebuffer.ColorCount) fb.ApplyPalette(pic);
        }
        int sf = tick - Konngara.DefeatScrollStart;
        if (sf >= 0 && tick <= Konngara.DefeatScrollEnd && _s.Visuals.ScreenShake) fb.ScrollY = sf * 32 % Framebuffer.Height;
        int level2 = tick < Konngara.DefeatFadeInStart ? 0 : Math.Min((tick - Konngara.DefeatFadeInStart) / 8 + 1, PaletteMax);
        if (tick >= Konngara.DefeatFadeOutStart) level2 = Math.Min(level2, Math.Max(0, PaletteMax - (tick - Konngara.DefeatFadeOutStart) / 8));
        MapPalette(fb, c => Math.Min(c, level2));
    }

    const int SoftDefeatCeilingKuji = 3;
}
