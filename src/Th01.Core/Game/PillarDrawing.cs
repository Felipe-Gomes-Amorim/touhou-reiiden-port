using Th01.Core.Graphics;

namespace Th01.Core.Game;

/// <summary>
/// Mima's standing pillars (the red columns that rise from the floor). The original builds a column out of the embedded
/// "pillar" sprite table: frame 0 is the 32x8 body segment (jagged edges), frame 1 the 32x8 top. The column is drawn
/// from the floor upwards one sprite row at a time, with the rows of each segment in reverse order, in a single colour.
/// ref: th01/main/boss/b10j.cpp (pillar drawing)
/// </summary>
public static class PillarDrawing
{
    public const string Sheet = "pillar";
    public const int Width = 32;
    public const int SegmentHeight = 8;

    /// <summary>
    /// Draws a pillar <paramref name="height"/> rows tall whose lowest row is screen row <paramref name="bottom"/>.
    /// <paramref name="left"/> is rounded down to a multiple of 8: the original draws on byte boundaries, so the picture
    /// can sit up to 7 px left of the hitbox (a quirk of the original, kept). Returns false, drawing nothing, when the
    /// sprite table is not available, so the caller can fall back to a plain box.
    /// </summary>
    public static bool Draw(Framebuffer fb, IGameArt? art, int left, int bottom, int height, byte color)
    {
        if (art == null || !art.TryGetFrame(Sheet, 0, out var body) || !art.TryGetFrame(Sheet, 1, out var top)) return false;
        if (!Fits(body) || !Fits(top)) return false;

        left &= ~7;
        int row = 0;
        for (int y = 0; y < height - SegmentHeight; y++)
            PutRow(fb, body, SegmentHeight - 1 - y % SegmentHeight, left, bottom - row++, color);
        for (int y = 0; y < SegmentHeight; y++)
            PutRow(fb, top, SegmentHeight - 1 - y, left, bottom - row++, color);
        return true;
    }

    static bool Fits(in ArtFrame f) => f.Width == Width && f.Height == SegmentHeight;

    static void PutRow(Framebuffer fb, in ArtFrame f, int spriteRow, int left, int screenY, byte color)
    {
        var alpha = f.Alpha.Span;
        var pixels = f.Pixels.Span;
        for (int c = 0; c < Width; c++)
        {
            int k = spriteRow * Width + c;
            bool on = alpha.Length != 0 ? alpha[k] != 0 : pixels[k] != 0;
            if (on) fb.SetPixel(left + c, screenY, color);
        }
    }
}
