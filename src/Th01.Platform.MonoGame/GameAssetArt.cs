using Th01.Assets.Model;
using Th01.Core.Graphics;

namespace Th01.Platform.MonoGame;

/// <summary>Adapts Assets models (<see cref="SpriteSheet"/>, <see cref="IndexedImage"/>) to the Core's <see cref="IGameArt"/>.</summary>
public sealed class GameAssetArt : IGameArt
{
    readonly Dictionary<string, SpriteSheet> _sheets = new(StringComparer.OrdinalIgnoreCase);
    readonly Dictionary<string, ArtFrame[]> _frames = new(StringComparer.OrdinalIgnoreCase);
    ArtFrame? _background;

    public void AddSheet(string name, SpriteSheet sheet)
    {
        _sheets[name] = sheet;
        _frames[name] = sheet.Frames.Select(f => ToFrame(f, f.Alpha)).ToArray();
    }

    public void SetBackground(IndexedImage image) => _background = ToFrame(image, null);

    public int FrameCount(string sheet) => _frames.TryGetValue(sheet, out var f) ? f.Length : 0;

    public bool TryGetFrame(string sheet, int frame, out ArtFrame art)
    {
        if (_frames.TryGetValue(sheet, out var f) && (uint)frame < (uint)f.Length) { art = f[frame]; return true; }
        art = default;
        return false;
    }

    public bool TryGetBackground(out ArtFrame art)
    {
        art = _background ?? default;
        return _background.HasValue;
    }

    static ArtFrame ToFrame(IndexedImage img, byte[]? alpha)
    {
        byte[] pal = [];
        if (img.Palette is { } p)
        {
            pal = new byte[Palette4.Count * 3];
            for (int i = 0; i < Palette4.Count; i++) (pal[i * 3], pal[i * 3 + 1], pal[i * 3 + 2]) = p[i];
        }
        return new ArtFrame(img.Width, img.Height, img.Pixels, alpha ?? [], pal);
    }
}
