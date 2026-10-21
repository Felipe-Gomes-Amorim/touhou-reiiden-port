using Th01.Assets;
using Th01.Assets.Formats;
using Th01.Core.Graphics;

namespace Th01.Platform.MonoGame;

/// <summary>Loads full-screen .GRP pictures from the user's game folder for the title screen.</summary>
public sealed class AssetScreenImages(IAssetSource assets) : IScreenImages
{
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
}
