using Th01.Assets;
using Th01.Assets.Formats;
using Th01.Assets.Model;

namespace Th01.Platform.MonoGame.Launcher;

/// <summary>
/// The game's own title logo (the kanji and the English subtitle), cut out of the player's REIIDEN3.GRP at run time: only the white
/// letters are kept (black is transparent, the red spiral is left out). It exists in memory only, never on disk or in the repository.
/// </summary>
public sealed class LauncherLogo
{
    /// <summary>Where the letters are in the 640x400 title picture (a generous box; the logo is trimmed to what is drawn inside it).</summary>
    public const int SearchX = 96, SearchY = 80, SearchWidth = 448, SearchHeight = 130;
    /// <summary>The logo is enlarged by this whole number, nearest neighbour.</summary>
    public const int Scale = 2;
    public const string FileName = "REIIDEN3.GRP";

    public int Width { get; }
    public int Height { get; }
    /// <summary>0xAARRGGBB, row-major; 0 = transparent.</summary>
    public uint[] Pixels { get; }

    LauncherLogo(int width, int height, uint[] pixels) { Width = width; Height = height; Pixels = pixels; }

    /// <summary>Size on the launcher picture.</summary>
    public int DrawnWidth => Width * Scale;
    public int DrawnHeight => Height * Scale;

    /// <summary>Cuts the logo out of a title picture; null when no letters are found where they should be.</summary>
    public static LauncherLogo? FromImage(IndexedImage image, Palette4? paletteOverride = null)
    {
        var palette = image.Palette ?? paletteOverride;
        if (palette is null) return null;
        int x0 = Math.Min(SearchX, image.Width), y0 = Math.Min(SearchY, image.Height);
        int x1 = Math.Min(SearchX + SearchWidth, image.Width), y1 = Math.Min(SearchY + SearchHeight, image.Height);
        int minX = int.MaxValue, minY = int.MaxValue, maxX = -1, maxY = -1;
        for (int y = y0; y < y1; y++)
            for (int x = x0; x < x1; x++)
                if (IsLetter(palette, image.Pixels[y * image.Width + x]))
                {
                    minX = Math.Min(minX, x); maxX = Math.Max(maxX, x);
                    minY = Math.Min(minY, y); maxY = Math.Max(maxY, y);
                }
        if (maxX < 0) return null;
        int w = maxX - minX + 1, h = maxY - minY + 1;
        var px = new uint[w * h];
        for (int y = 0; y < h; y++)
            for (int x = 0; x < w; x++)
            {
                byte idx = image.Pixels[(minY + y) * image.Width + minX + x];
                if (!IsLetter(palette, idx)) continue;
                var (r, g, b) = palette.ToRgb888(idx);
                px[y * w + x] = 0xFF000000u | (uint)(r << 16) | (uint)(g << 8) | b;
            }
        return new LauncherLogo(w, h, px);
    }

    /// <summary>Black is the background; the letters are the neutral (grey/white) colours, which leaves the red spiral out.</summary>
    static bool IsLetter(Palette4 p, byte index)
    {
        var (r, g, b) = p[index & 15];
        if (r == 0 && g == 0 && b == 0) return false;
        int hi = Math.Max(r, Math.Max(g, b)), lo = Math.Min(r, Math.Min(g, b));
        return hi - lo <= 2 && hi >= 6;
    }

    /// <summary>Reads REIIDEN3.GRP from the user's game files; null when it is missing or unreadable.</summary>
    public static LauncherLogo? TryLoad(IAssetSource assets)
    {
        try
        {
            if (!assets.Exists(FileName)) return null;
            using var s = assets.Open(FileName);
            return FromImage(GrpImage.Load(s));
        }
        catch (Exception e) when (e is InvalidDataException or EndOfStreamException or IOException or UnauthorizedAccessException)
        {
            return null;
        }
    }

    /// <summary>Draws the logo at (x, y), enlarged by <see cref="Scale"/> with no smoothing.</summary>
    public void Draw(LauncherCanvas canvas, int x, int y)
    {
        for (int yy = 0; yy < Height; yy++)
            for (int xx = 0; xx < Width; xx++)
            {
                uint c = Pixels[yy * Width + xx];
                if (c != 0) canvas.FillRect(x + xx * Scale, y + yy * Scale, Scale, Scale, c);
            }
    }
}
