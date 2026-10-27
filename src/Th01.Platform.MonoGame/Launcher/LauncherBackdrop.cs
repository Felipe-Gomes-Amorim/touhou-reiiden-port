namespace Th01.Platform.MonoGame.Launcher;

public enum DitherKind { Quarter, Half, ThreeQuarter }

/// <summary>The still and moving parts of the launcher's backdrop: glow, stars, and the dotted bands that scroll left.</summary>
public static class LauncherBackdrop
{
    public const int BandHeight = 10, BandPixelsPerTick = 1, TilePeriod = 4;

    /// <summary>Is this pixel of a 4x4 dither tile painted? Quarter = top-right block, Half = checker of 2x2 blocks, ThreeQuarter = all but top-right.</summary>
    public static bool Dither(DitherKind kind, int tx, int ty)
    {
        bool topRight = (tx & 3) >= 2 && (ty & 3) < 2;
        bool bottomLeft = (tx & 3) < 2 && (ty & 3) >= 2;
        return kind switch
        {
            DitherKind.Quarter => topRight,
            DitherKind.Half => topRight || bottomLeft,
            _ => !topRight,
        };
    }

    /// <summary>Frame of the spinning orb at a tick: one turn per second at the game's tick rate (56.423 Hz), in <see cref="OrbFrames.Count"/> steps.</summary>
    public static int SpinFrame(int tick) => (int)((long)tick * OrbFrames.Count * 1000 / Th01.Core.Timing.TickRate.TicksPerThousandSeconds % OrbFrames.Count);

    /// <summary>Horizontal offset of the bands at a tick: the picture moves left one pixel per tick and repeats every 4 pixels.</summary>
    public static int ScrollOffset(int tick) => (int)((long)tick * BandPixelsPerTick % TilePeriod);

    /// <summary>Draws the three dotted bands (stacked, 10 px each) whose bottom edge is <paramref name="bottomOffset"/> above the canvas bottom.</summary>
    public static void DrawBands(LauncherCanvas c, int tick, int bottomOffset)
    {
        int shift = ScrollOffset(tick);
        int top = LauncherCanvas.Height - bottomOffset - 3 * BandHeight;
        for (int band = 0; band < 3; band++)
            for (int y = 0; y < BandHeight; y++)
                for (int x = 0; x < LauncherCanvas.Width; x++)
                    if (Dither((DitherKind)band, x + shift, y)) c.Put(x, top + band * BandHeight + y, LauncherColors.Purple1);
    }

    static readonly Dictionary<(int, int, uint, int), uint[]> Cache = new();

    /// <summary>
    /// Backdrop colour with an elliptical glow (CSS "ellipse 60% 70% at X% Y%", fading to nothing at 70 % of the radius) and the stars.
    /// Built once per combination and then copied.
    /// </summary>
    public static uint[] Base(int glowXPercent, int glowYPercent, uint glowColor, int glowAlpha, bool wide)
    {
        var key = (glowXPercent, glowYPercent, glowColor, glowAlpha + (wide ? 1000 : 0));
        lock (Cache)
        {
            if (Cache.TryGetValue(key, out var cached)) return cached;
            var px = new uint[LauncherCanvas.Width * LauncherCanvas.Height];
            double cx = LauncherCanvas.Width * glowXPercent / 100.0, cy = LauncherCanvas.Height * glowYPercent / 100.0;
            double rx = LauncherCanvas.Width * 0.6, ry = LauncherCanvas.Height * 0.7;
            for (int y = 0; y < LauncherCanvas.Height; y++)
                for (int x = 0; x < LauncherCanvas.Width; x++)
                {
                    double dx = (x - cx) / rx, dy = (y - cy) / ry;
                    double d = Math.Sqrt(dx * dx + dy * dy);
                    int a = d >= 0.7 ? 0 : (int)(glowAlpha * (1 - d / 0.7));
                    px[y * LauncherCanvas.Width + x] = a <= 0 ? LauncherColors.Background : LauncherCanvas.Mix(LauncherColors.Background, glowColor, a);
                }
            var tmp = new LauncherCanvas();
            tmp.CopyFrom(px);
            foreach (var (sx, sy, bright) in Stars(wide)) tmp.FillRect(sx, sy, 2, 2, bright ? LauncherColors.White : LauncherColors.Star);
            Cache[key] = tmp.Pixels;
            return tmp.Pixels;
        }
    }

    // Star positions of the design (box-shadow list of the home screen).
    static IEnumerable<(int, int, bool)> Stars(bool wide)
    {
        (int, int, bool)[] s =
        [
            (140, 36, true), (420, 92, false), (610, 28, false), (760, 120, true), (910, 64, false), (1040, 30, true),
            (1180, 110, false), (1240, 200, false), (700, 210, false), (60, 250, false), (330, 214, true), (1100, 250, true),
        ];
        return wide ? s : s.Take(8);
    }
}
