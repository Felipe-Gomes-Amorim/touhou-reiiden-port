namespace Th01.Core.Graphics;

/// <summary>A generic 16-colour test pattern. The palette is our own, not the game's.</summary>
public static class TestPattern
{
    public static void Draw(Framebuffer fb)
    {
        // Index bits: bit0=blue, bit1=red, bit2=green, bit3=intensity (generic digital-RGB look).
        for (int i = 0; i < Framebuffer.ColorCount; i++)
        {
            int lo = (i & 8) != 0 ? 15 : 10;
            int r = (i & 2) != 0 ? lo : (i & 8) != 0 ? 5 : 0;
            int g = (i & 4) != 0 ? lo : (i & 8) != 0 ? 5 : 0;
            int b = (i & 1) != 0 ? lo : (i & 8) != 0 ? 5 : 0;
            fb.SetPalette4(i, r, g, b);
        }

        const int cell = 80;
        for (int i = 0; i < Framebuffer.ColorCount; i++)
            fb.FillRect((i % 8) * cell, (i / 8) * 100, cell, 100, (byte)i);

        // Border and diagonal to verify 640x400 geometry and no scaling error.
        for (int x = 0; x < Framebuffer.Width; x++) { fb.SetPixel(x, 0, 15); fb.SetPixel(x, Framebuffer.Height - 1, 15); }
        for (int y = 0; y < Framebuffer.Height; y++) { fb.SetPixel(0, y, 15); fb.SetPixel(Framebuffer.Width - 1, y, 15); }
        for (int y = 0; y < Framebuffer.Height; y++) fb.SetPixel(y * Framebuffer.Width / Framebuffer.Height, y, 0);

        // Bottom strip: gradient ramp of each index to catch palette lookups.
        for (int i = 0; i < Framebuffer.ColorCount; i++)
            fb.FillRect(i * 40, 300, 40, 100, (byte)i);
    }
}
