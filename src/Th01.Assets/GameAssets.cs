using Th01.Assets.Formats;
using Th01.Assets.Model;

namespace Th01.Assets;

/// <summary>
/// Runtime facade over the user's game files: opens the archive once and decodes files on demand,
/// caching the results. Returned objects are shared; callers must not modify them.
/// Missing files throw <see cref="FileNotFoundException"/>; malformed files throw
/// <see cref="InvalidDataException"/> naming the file. Nothing is ever returned half-decoded.
/// </summary>
public sealed class GameAssets
{
    public const int StagesPerScene = StageDat.StagesPerScene;

    private readonly IAssetSource _source;
    private readonly byte[] _archive;
    private readonly IReadOnlyList<PackEntry> _entries;
    private readonly Dictionary<string, object> _cache = new(StringComparer.OrdinalIgnoreCase);
    private readonly object _lock = new();

    public GameAssets(IAssetSource source)
    {
        ArgumentNullException.ThrowIfNull(source);
        _source = source;
        if (!source.Exists(ExpectedFiles.ArchiveName))
            throw new FileNotFoundException($"The data archive '{ExpectedFiles.ArchiveName}' is not in the game folder.", ExpectedFiles.ArchiveName);
        using (var s = source.Open(ExpectedFiles.ArchiveName))
        using (var ms = new MemoryStream())
        {
            s.CopyTo(ms);
            _archive = ms.ToArray();
        }
        try { _entries = PackFile.Entries(new MemoryStream(_archive)); }
        catch (InvalidDataException e) { throw new InvalidDataException($"{ExpectedFiles.ArchiveName}: {e.Message}", e); }
    }

    /// <summary>Names of the files inside the archive.</summary>
    public IReadOnlyList<string> PackNames => _entries.Select(e => e.Name).ToList();

    /// <summary>All stages of a scene (the file STAGE{scene}.DAT; scenes 0..7, 5 stages each, the 5th is the boss stage).</summary>
    public IReadOnlyList<StageLayout> GetScene(int scene)
    {
        if (scene is < 0 or > 9) throw new ArgumentOutOfRangeException(nameof(scene), "Scene must be 0..9.");
        return Cached($"scene:{scene}", () => Parse($"STAGE{scene}.DAT", FromPack($"STAGE{scene}.DAT"), StageDat.Load));
    }

    /// <summary>One stage of a scene; <paramref name="index"/> is 0..4 (4 = boss stage).</summary>
    public StageLayout GetStage(int scene, int index)
    {
        var list = GetScene(scene);
        if ((uint)index >= (uint)list.Count) throw new ArgumentOutOfRangeException(nameof(index), $"STAGE{scene}.DAT has {list.Count} stage(s).");
        return list[index];
    }

    /// <summary>
    /// Stage by flat number <c>scene * 5 + index</c>. This is the position inside the data files, not the game's
    /// stage number: which scene a stage uses depends on the chosen route (decided by gameplay).
    /// </summary>
    public StageLayout GetStage(int stage)
    {
        if (stage < 0) throw new ArgumentOutOfRangeException(nameof(stage));
        return GetStage(stage / StagesPerScene, stage % StagesPerScene);
    }

    public SpriteSheet GetPtn(string name) => CachedPack(name, ".PTN", PtnSprites.Load);
    public SpriteSheet GetBos(string name) => CachedPack(name, ".BOS", BosSprites.Load);
    public SpriteSheet GetGrc(string name) => CachedPack(name, ".GRC", GrcSprites.Load);

    /// <summary>A loose .GRP image (640x400 with palette) from the game folder.</summary>
    public IndexedImage GetGrp(string name) =>
        Cached("grp:" + name, () => Parse<IndexedImage>(name, FromFolder(name), d => GrpImage.Load(d)));

    /// <summary>A loose .GRZ file: one sheet per image, each frame being one planar stream.</summary>
    public IReadOnlyList<SpriteSheet> GetGrz(string name) =>
        Cached("grz:" + name, () => Parse(name, FromFolder(name), d => GrzImage.LoadGrz(d)));

    /// <summary>The HUD mask (MASK.GRF): monochrome, 640x66.</summary>
    public IndexedImageA GetHudMask() =>
        Cached("grf:MASK", () => Parse("MASK.GRF", FromFolder("MASK.GRF"), d => GrfImage.LoadFirstPlane(d)));

    /// <summary>
    /// Monochrome sprite tables embedded in REIIDEN.EXE (pellets, leaves, lasers, masks, cursor...), found by content.
    /// Tables missing from this build are listed in EmbeddedSprites.Missing.
    /// </summary>
    public EmbeddedSprites GetEmbeddedSprites(string exeName = "REIIDEN.EXE") =>
        Cached("exe:" + exeName, () => EmbeddedSprites.Locate(FromFolder(exeName)));

    /// <summary>Raw decoded bytes of an archive entry (for readers this facade does not wrap).</summary>
    public byte[] GetPackBytes(string name) => FromPack(name);

    private SpriteSheet CachedPack(string name, string extension, ParseFunc<SpriteSheet> parse)
    {
        if (!name.EndsWith(extension, StringComparison.OrdinalIgnoreCase))
            throw new ArgumentException($"'{name}' is not a {extension} file.", nameof(name));
        return Cached("pack:" + name, () => Parse(name, FromPack(name), parse));
    }

    private delegate T ParseFunc<out T>(ReadOnlySpan<byte> data);

    private static T Parse<T>(string name, byte[] data, ParseFunc<T> parse)
    {
        try { return parse(data); }
        catch (InvalidDataException e) { throw new InvalidDataException($"{name}: {e.Message}", e); }
    }

    private byte[] FromPack(string name)
    {
        var entry = _entries.FirstOrDefault(e => string.Equals(e.Name, name, StringComparison.OrdinalIgnoreCase))
            ?? throw new FileNotFoundException($"'{name}' is not in the data archive.", name);
        try { return PackFile.Read(new MemoryStream(_archive), entry); }
        catch (InvalidDataException e) { throw new InvalidDataException($"{name}: {e.Message}", e); }
    }

    private byte[] FromFolder(string name)
    {
        if (!_source.Exists(name)) throw new FileNotFoundException($"'{name}' is not in the game folder.", name);
        using var s = _source.Open(name);
        using var ms = new MemoryStream();
        s.CopyTo(ms);
        return ms.ToArray();
    }

    private T Cached<T>(string key, Func<T> create) where T : notnull
    {
        lock (_lock)
        {
            if (_cache.TryGetValue(key, out var v)) return (T)v;
            T made = create();
            _cache[key] = made;
            return made;
        }
    }
}
