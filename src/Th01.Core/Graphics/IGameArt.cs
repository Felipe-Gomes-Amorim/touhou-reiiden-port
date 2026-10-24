namespace Th01.Core.Graphics;

/// <summary>
/// One image as raw arrays so Th01.Core never references Th01.Assets.
/// <see cref="Pixels"/> holds Width*Height palette indices (0..15); <see cref="Alpha"/> is empty (all opaque) or
/// Width*Height bytes where 0 = transparent; <see cref="Palette"/> is empty or 48 bytes (R,G,B per colour, each 0..15).
/// </summary>
public readonly record struct ArtFrame(
    int Width, int Height, ReadOnlyMemory<byte> Pixels, ReadOnlyMemory<byte> Alpha, ReadOnlyMemory<byte> Palette);

/// <summary>
/// What gameplay may ask the platform to draw. Sprites are addressed by a sheet name (the file name in the game data,
/// e.g. "REIMU.PTN") and a frame index. Every lookup can fail: callers must keep a fallback (the flat boxes).
/// </summary>
public interface IGameArt
{
    // Sheet names. Colour sheets use the file name in the archive ("STG.PTN", "MIKO_AC.BOS", ...). Monochrome sheets compiled
    // into REIIDEN.EXE use fixed lower-case names: "pellet" (3 frames of 8x8: solid, ring, dotted ring), "pellet_c" (cloud, 2 of 16x16),
    // "laser_s" (8 of 16x8), "bonusbox" (1 of 8x4), "pillar" (2 of 32x8), "shape8x8" (7 of 8x8), "leaf_s", "leaf_l", "leaf_r"
    // (1 of 8x8 each), "ileave_m" (10 of 8x8), "mousecur" (2 of 16x16). Their frames are monochrome: Pixels are 0/1, Alpha == Pixels,
    // Palette is empty; paint them with Framebuffer.DrawMono in the colour you want.

    /// <summary>Number of frames in the sheet, or 0 if the sheet is not available.</summary>
    int FrameCount(string sheet);

    bool TryGetFrame(string sheet, int frame, out ArtFrame art);

    /// <summary>Full-screen background of the current stage (with its palette), if one was loaded.</summary>
    bool TryGetBackground(out ArtFrame art);
}

public sealed class NullGameArt : IGameArt
{
    public static readonly NullGameArt Instance = new();
    public int FrameCount(string sheet) => 0;
    public bool TryGetFrame(string sheet, int frame, out ArtFrame art) { art = default; return false; }
    public bool TryGetBackground(out ArtFrame art) { art = default; return false; }
}

public static class ArtFrameExtensions
{
    /// <summary>
    /// Draws a monochrome sheet frame (the sprites compiled into the executable, and .GRC): <see cref="ArtFrame.Pixels"/> are 0/1 and
    /// <see cref="ArtFrame.Alpha"/> equals the bits, so every set bit is painted in <paramref name="color"/>, the rest is left alone.
    /// The drawing code picks the colour (the original's GRCG did the same).
    /// </summary>
    public static void DrawMono(this Framebuffer fb, in ArtFrame art, int x, int y, byte color)
    {
        var alpha = art.Alpha.Span;
        for (int sy = 0; sy < art.Height; sy++)
            for (int sx = 0; sx < art.Width; sx++)
                if (alpha[sy * art.Width + sx] != 0) fb.SetPixel(x + sx, y + sy, color);
    }

    /// <summary>Sets the framebuffer palette from the frame's 48-byte palette, if it has one.</summary>
    public static void ApplyPalette(this Framebuffer fb, in ArtFrame art)
    {
        var p = art.Palette.Span;
        if (p.Length != Framebuffer.ColorCount * 3) return;
        for (int i = 0; i < Framebuffer.ColorCount; i++) fb.SetPalette4(i, p[i * 3] & 0xF, p[i * 3 + 1] & 0xF, p[i * 3 + 2] & 0xF);
    }

    /// <summary>Draws the frame at (x, y), honouring its alpha. Does not touch the palette.</summary>
    public static void Draw(this Framebuffer fb, in ArtFrame art, int x, int y) =>
        fb.Blit(art.Pixels.Span, art.Width, art.Height, x, y, art.Alpha.Span);
}
