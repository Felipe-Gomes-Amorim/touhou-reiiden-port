using Th01.Assets;
using Th01.Assets.Formats;
using Th01.Core.Graphics;

namespace Th01.Platform.MonoGame;

/// <summary>
/// Loads full-screen .GRP pictures from the user's game folder for the title screen, and serves the story text of the
/// user's own FUUIN.EXE (ending captions) to the slideshow. The text is read at run time and never stored.
/// </summary>
public sealed class AssetScreenImages(IAssetSource assets) : IScreenImages, ICaptionSource, IVerdictSource
{
    Dictionary<string, string[]>? _scenes;
    Th01.Assets.Model.VerdictStrings? _verdict;
    bool _verdictRead;

    /// <summary>The verdict screen's strings from the user's FUUIN.EXE, or null if they cannot be found.</summary>
    public Th01.Assets.Model.VerdictStrings? Verdict
    {
        get
        {
            if (!_verdictRead) { _verdictRead = true; _scenes ??= LoadScenes(); }
            return _verdict;
        }
    }

    readonly Dictionary<string, ArtFrame?> _cache = new(StringComparer.OrdinalIgnoreCase);

    public bool TryLoad(string fileName, out ArtFrame image)
    {
        if (!_cache.TryGetValue(fileName, out var cached))
        {
            cached = null;
            if (assets.Exists(fileName))
            {
                try
                {
                    using var s = assets.Open(fileName);
                    var art = new GameAssetArt();
                    art.SetBackground(GrpImage.Load(s));
                    art.TryGetBackground(out var f);
                    cached = f;
                }
                catch (Exception e) when (e is InvalidDataException or EndOfStreamException or IOException)
                {
                    Console.Error.WriteLine($"{fileName} could not be read: {e.Message}");
                }
            }
            _cache[fileName] = cached;
        }
        image = cached ?? default;
        return cached.HasValue;
    }

    public IReadOnlyList<string> Lines(string scene)
    {
        _scenes ??= LoadScenes();
        _verdictRead = true;
        return _scenes.TryGetValue(scene, out var lines) ? lines : [];
    }

    Dictionary<string, string[]> LoadScenes()
    {
        var map = new Dictionary<string, string[]>(StringComparer.OrdinalIgnoreCase);
        const string exe = "FUUIN.EXE";
        if (!assets.Exists(exe)) return map;
        try
        {
            using var s = assets.Open(exe);
            using var ms = new MemoryStream();
            s.CopyTo(ms);
            byte[] bytes = ms.ToArray();
            var refs = ExeStringRefs.Scan(bytes, ExeText.Scan(exe, bytes).Runs);
            if (refs is null) return map;
            _verdict = ExeStringRefs.Verdict(bytes, refs);
            foreach (var scene in ExeStringRefs.Scenes(refs.Refs))
                map.TryAdd(scene.Image, scene.Lines.Select(l => l.Text).ToArray());
        }
        catch (Exception e) when (e is InvalidDataException or IOException)
        {
            Console.Error.WriteLine($"{exe} could not be read for captions: {e.Message}");
        }
        return map;
    }
}
