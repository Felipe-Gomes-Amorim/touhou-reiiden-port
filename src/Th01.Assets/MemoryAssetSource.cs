namespace Th01.Assets;

/// <summary>Asset source backed by byte arrays held in memory (for example the files of a disk image). Names match case-insensitively.</summary>
public sealed class MemoryAssetSource : IAssetSource
{
    readonly Dictionary<string, byte[]> _files = new(StringComparer.OrdinalIgnoreCase);

    public void Add(string name, byte[] data) => _files[name] = data;

    public bool Exists(string name) => _files.ContainsKey(name);

    public long? GetSize(string name) => _files.TryGetValue(name, out var d) ? d.Length : null;

    public Stream Open(string name) =>
        _files.TryGetValue(name, out var d)
            ? new MemoryStream(d, writable: false)
            : throw new FileNotFoundException($"Missing game file: {name}", name);
}
