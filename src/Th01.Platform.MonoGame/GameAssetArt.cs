using Th01.Assets.Model;
using Th01.Core.Graphics;

namespace Th01.Platform.MonoGame;

/// <summary>Adapts Assets models (<see cref="SpriteSheet"/>, <see cref="IndexedImage"/>) to the Core's <see cref="IGameArt"/>.</summary>
public sealed class GameAssetArt : IGameArt
{
    readonly Dictionary<string, SpriteSheet> _sheets = new(StringComparer.OrdinalIgnoreCase);
    readonly Dictionary<string, ArtFrame[]> _frames = new(StringComparer.OrdinalIgnoreCase);
    readonly Dictionary<string, Func<SpriteSheet>> _lazy = new(StringComparer.OrdinalIgnoreCase);
    readonly Dictionary<string, string> _failed = new(StringComparer.OrdinalIgnoreCase);
    readonly MissingArtLog? _log;
    ArtFrame? _background;

    public GameAssetArt(MissingArtLog? log = null) => _log = log;

    /// <summary>Registers a sheet that is only read from disk the first time a frame is asked for. A sheet that fails to load stays missing (callers fall back).</summary>
    public void AddLazySheet(string name, Func<SpriteSheet> load) => _lazy[name] = load;

    ArtFrame[]? Resolve(string sheet)
    {
        if (_frames.TryGetValue(sheet, out var f)) return f;
        if (!_lazy.Remove(sheet, out var load)) return null;
        try { AddSheet(sheet, load()); return _frames[sheet]; }
        catch (Exception e) when (e is InvalidDataException or FileNotFoundException or IOException)
        {
            Console.Error.WriteLine($"{sheet} could not be loaded, using plain shapes instead: {e.Message}");
            _failed[sheet] = e.Message;
            return null;
        }
    }

    public void AddSheet(string name, SpriteSheet sheet)
    {
        _sheets[name] = sheet;
        _frames[name] = sheet.Frames.Select(f => ToFrame(f, f.Alpha)).ToArray();
    }

    public void SetBackground(IndexedImage image) => _background = ToFrame(image, null);

    public int FrameCount(string sheet)
    {
        var f = Resolve(sheet);
        if (f is null) NoteSheetMissing(sheet, -1);
        return f?.Length ?? 0;
    }

    void NoteSheetMissing(string sheet, int index) =>
        _log?.Note(sheet, index, _failed.TryGetValue(sheet, out var why) ? $"sheet failed to load ({why})" : "sheet not registered");

    public bool TryGetFrame(string sheet, int frame, out ArtFrame art)
    {
        var f = Resolve(sheet);
        if (f is not null && (uint)frame < (uint)f.Length) { art = f[frame]; return true; }
        if (f is null) NoteSheetMissing(sheet, frame);
        else _log?.Note(sheet, frame, $"frame index outside the sheet (it has {f.Length} frame(s))");
        art = default;
        return false;
    }

    public bool TryGetBackground(out ArtFrame art)
    {
        art = _background ?? default;
        if (!_background.HasValue) _log?.Note("background", -1, "no background picture loaded for this stage");
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
