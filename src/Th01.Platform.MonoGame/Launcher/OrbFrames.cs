namespace Th01.Platform.MonoGame.Launcher;

/// <summary>The yin-yang orb turned by whole steps. Frames are made once per radius and kept; only drawing, never the simulation.</summary>
public static class OrbFrames
{
    /// <summary>Steps in one full turn.</summary>
    public const int Count = 32;

    static readonly Dictionary<(int Radius, int Frame), uint[]> Cache = [];

    /// <summary>Pixels of one frame, (2r+1) x (2r+1), row-major, 0 where nothing is drawn.</summary>
    public static uint[] Get(int radius, int frame)
    {
        frame = ((frame % Count) + Count) % Count;
        lock (Cache)
        {
            if (!Cache.TryGetValue((radius, frame), out var px)) Cache[(radius, frame)] = px = Build(radius, frame);
            return px;
        }
    }

    static uint[] Build(int r, int frame)
    {
        int side = 2 * r + 1;
        var px = new uint[side * side];
        double a = 2 * Math.PI * frame / Count, cos = Math.Cos(a), sin = Math.Sin(a);
        double h = r / 2.0, dot = Math.Max(1.5, r * 0.15);
        for (int y = -r; y <= r; y++)
            for (int x = -r; x <= r; x++)
            {
                // Where this pixel comes from in the upright orb (turning the orb clockwise on screen).
                double u = x * cos + y * sin, v = -x * sin + y * cos;
                if (u * u + v * v > r * r + 0.25) continue;
                double dyu = v + h, dyl = v - h;
                uint c;
                if (u * u + dyu * dyu <= dot * dot) c = LauncherColors.Red;               // dot in the upper (white) bulb
                else if (u * u + dyl * dyl <= dot * dot) c = LauncherColors.White;        // dot in the lower (red) bulb
                else if (u * u + dyu * dyu <= h * h) c = LauncherColors.White;
                else if (u * u + dyl * dyl <= h * h) c = LauncherColors.Red;
                else c = u >= 0 ? LauncherColors.Red : LauncherColors.White;
                px[(y + r) * side + x + r] = c;
            }
        return px;
    }
}
