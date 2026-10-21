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
