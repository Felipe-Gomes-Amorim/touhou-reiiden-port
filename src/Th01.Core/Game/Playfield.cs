namespace Th01.Core.Game;

/// <summary>Playfield geometry and shared sizes. Pixels, PC-98 640x400, origin top-left.</summary>
public static class Playfield
{
    // ref: th01/main/playfld.hpp
    public const int Left = 0;
    public const int Top = 64;
    public const int Right = 640;
    public const int Bottom = 400;
    public const int Width = Right - Left;
    public const int Height = Bottom - Top;
    public const int CenterX = Width / 2 + Left;

    // ref: th01/main/player/player.hpp
    public const int PlayerW = 32;
    public const int PlayerH = 32;
    public const int PlayerLeftMin = Left;
    public const int PlayerLeftMax = Right - PlayerW;
    public const int PlayerLeftStart = CenterX - PlayerW / 2;
    public const int PlayerTop = Bottom - PlayerH;
    public const int PlayerMissInvincibilityFrames = 150;

    // ref: th01/main/player/orb.hpp
    public const int OrbW = 32;
    public const int OrbH = 32;
    public const int OrbLeftMin = Left;
    public const int OrbLeftMax = Right - OrbW;
    public const int OrbTopMin = Top;
    public const int OrbTopMax = Bottom - OrbH;
    public const int OrbLeftStart = OrbLeftMax - 8;
    public const int OrbTopStart = OrbTopMax - 88;

    // ref: th01/common.h
    public const int LivesMax = 6;
    public const int BombsMax = 5;
}

/// <summary>Generic overlap predicates; names mirror the original macros so the strict/non-strict choice stays visible.</summary>
public static class Overlap
{
    // ref: th01/math/overlap.hpp
    public static bool XywhXywhLtGt(int x1, int y1, int w1, int h1, int x2, int y2, int w2, int h2) =>
        x1 - x2 < w2 && x1 - x2 > -w1 && y1 - y2 < h2 && y1 - y2 > -h1;

    public static bool XywhXywhLeGe(int x1, int y1, int w1, int h1, int x2, int y2, int w2, int h2) =>
        x1 - x2 <= w2 && x1 - x2 >= -w1 && y1 - y2 <= h2 && y1 - y2 >= -h1;

    public static bool XyXywhLeGe(int x1, int y1, int x2, int y2, int w2, int h2) =>
        x1 >= x2 && x1 <= x2 + w2 && y1 >= y2 && y1 <= y2 + h2;
}

/// <summary>Difficulty. ref: th01/rank.h</summary>
public enum Rank : byte { Easy, Normal, Hard, Lunatic }

/// <summary>FNV-1a over little-endian 64-bit words; same convention as DummyState.</summary>
public struct StateHasher
{
    public ulong Value = 14695981039346656037UL;
    public StateHasher() { }

    public void Add(long v)
    {
        for (int i = 0; i < 8; i++)
        {
            Value ^= (byte)(v >> (i * 8));
            Value *= 1099511628211UL;
        }
    }

    public void Add(bool v) => Add(v ? 1 : 0);
    public void Add(double v) => Add(BitConverter.DoubleToInt64Bits(v));
}
