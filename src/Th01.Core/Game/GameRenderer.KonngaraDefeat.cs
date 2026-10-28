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
    /// Draws the whole screen of the defeat sequence at the given tick of it. The original flips the background colour
    /// (palette entry 0) between black and white every 10 ticks; with soft flashes it is a slow fade between black and a dark grey.
    /// </summary>
    void DrawKonngaraDefeat(Framebuffer fb, int tick)
    {
        fb.Clear(0);
        int kujiEnd = Konngara.KujiCount * Konngara.KujiTicks, shakeEnd = kujiEnd + Konngara.DefeatShakeTicks;
        if (tick < kujiEnd)
        {
            int k = tick / Konngara.KujiTicks, inK = tick % Konngara.KujiTicks;
            int target = k % 2 == 1 ? 1 : 0, previous = k == 0 ? 0 : 1 - target;     // 1 = the light background
            int level;
            if (!_s.Visuals.SoftFlashes) level = target * PaletteMax;
            else level = SoftDefeatCeilingKuji * (previous * 9 + (target - previous) * Math.Min(inK + 1, 9)) / 9;   // dark grey at most, a level every ~3 ticks
            fb.SetPalette4(0, level, level, level);
            int image = KujiFirstImage + k;
            fb.Clear(0);
            DrawColorKey(fb, SlashSheet, image);
            return;
        }
        if (tick < shakeEnd)
        {
            int f = tick - kujiEnd + 1;
            DrawColorKey(fb, SlashSheet, KujiFirstImage + Konngara.KujiCount - 1);
            if (_s.Visuals.ScreenShake) fb.ScrollY = (Framebuffer.Height + 8 + f % 2 * 16) % Framebuffer.Height;
            int steps = f / 4;                                                        // z_palette_black_out_step every 4 frames
            MapPalette(fb, c => Math.Max(0, c - steps));
            return;
        }
        // the final scroll: the first picture moves 32 lines a tick, the palette comes in for 150 ticks, then goes out
        int sf = tick - shakeEnd;
        if (Picture(ScrollPicture, out var pic))
        {
            for (int y = 0; y < Framebuffer.Height; y++)
                for (int x = 0; x < Framebuffer.Width; x++) fb.SetPixel(x, y, pic.Pixels.Span[y * pic.Width + x]);
            if (pic.Palette.Length == 3 * Framebuffer.ColorCount) fb.ApplyPalette(pic);
        }
        int line = sf * 32 % Framebuffer.Height;
        fb.ScrollY = line;
        int level2 = sf < 150 ? sf / 8 + 1 : Math.Max(0, PaletteMax - (sf - 150) / 8);
        MapPalette(fb, c => Math.Min(c, level2));
    }

    const int SoftDefeatCeilingKuji = 3;
}
