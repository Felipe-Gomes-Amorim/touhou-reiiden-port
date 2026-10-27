namespace Th01.Platform.MonoGame;

/// <summary>The three looks of the video options. The numbers are what launcher.json stores.</summary>
public enum ImageFilter { Sharp = 0, Smooth = 1, Scanlines = 2 }

/// <summary>How one frame is drawn for a filter. Pure data, so the choice can be tested without a graphics device.</summary>
/// <param name="Linear">Bilinear sampling (false = nearest neighbour, hard pixels).</param>
/// <param name="TwoTap">Draw the picture twice, half a source pixel apart, and average: a visible softening at any scale.</param>
/// <param name="TapShift">Distance between the two copies, in window pixels (at least 1).</param>
/// <param name="Scanlines">Overlay the dark line of the CRT on every line of the picture.</param>
public sealed record FilterPlan(bool Linear, bool TwoTap, int TapShift, bool Scanlines)
{
    /// <param name="scale">Window pixels per picture pixel (width of the drawn box over the width of the picture).</param>
    public static FilterPlan Choose(ImageFilter filter, double scale) => filter switch
    {
        // Nearest neighbour, whatever the scale: pixels stay hard.
        ImageFilter.Sharp => new FilterPlan(false, false, 0, false),
        // Plain bilinear hardly changes anything at whole-number scales (the texel centres line up), so it is blended with a copy shifted by
        // half a picture pixel: clearly softer at 1x, 2x, 3x or 1.5x, without turning into a blur.
        ImageFilter.Smooth => new FilterPlan(true, true, Math.Max(1, (int)Math.Round(scale * 0.5)), false),
        // Hard pixels plus the scanline overlay.
        _ => new FilterPlan(false, false, 0, true),
    };
}

/// <summary>
/// Where the dark scanline bands go. One band at the bottom of every picture line, whatever the scale, so the lines stay tied to the
/// picture (a stretched texture gives uneven bands at scales like 2.5x). Below 2 window rows per line there is no room for a band
/// between lines, so every second window row is darkened instead (the classic look at 1x).
/// </summary>
public static class Scanlines
{
    /// <summary>Opacity of the dark bands (black), 0..255: noticeable, not a black bar.</summary>
    public const int Alpha = 110;

    /// <summary>Bands as (row offset from the top of the picture box, height in window rows), for a picture of <paramref name="lines"/> lines drawn
    /// <paramref name="destHeight"/> window rows tall.</summary>
    public static IReadOnlyList<(int Offset, int Height)> Rows(int lines, int destHeight)
    {
        var rows = new List<(int, int)>();
        if (lines <= 0 || destHeight <= 0) return rows;
        if (destHeight < lines * 2)
        {
            for (int y = 1; y < destHeight; y += 2) rows.Add((y, 1));
            return rows;
        }
        double scale = destHeight / (double)lines;
        int band = Math.Max(1, (int)Math.Round(scale * 0.34));
        for (int line = 0; line < lines; line++)
        {
            int top = (int)Math.Round(line * scale), bottom = (int)Math.Round((line + 1) * scale);
            int h = Math.Min(band, bottom - top - 1);          // always leave a clear row in the line
            if (h >= 1) rows.Add((bottom - h, h));
        }
        return rows;
    }
}
