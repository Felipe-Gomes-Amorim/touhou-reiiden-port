namespace Th01.Assets;

/// <summary>Asset source backed by a plain folder. Names are matched case-insensitively (DOS heritage).</summary>
public sealed class FolderAssetSource : IAssetSource
{
    readonly string _root;
    readonly Dictionary<string, string> _files;

    public FolderAssetSource(string root)
    {
        if (!Directory.Exists(root)) throw new DirectoryNotFoundException(root);
        _root = root;
        _files = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (string f in Directory.EnumerateFiles(root))
            _files[Path.GetFileName(f)] = f;
    }

    public string Root => _root;

    public bool Exists(string name) => _files.ContainsKey(name);

    public long? GetSize(string name) => _files.TryGetValue(name, out var p) ? new FileInfo(p).Length : null;

    public Stream Open(string name) =>
        _files.TryGetValue(name, out var p)
            ? new FileStream(p, FileMode.Open, FileAccess.Read, FileShare.Read)
            : throw new FileNotFoundException($"Missing game file: {name}", name);
}
