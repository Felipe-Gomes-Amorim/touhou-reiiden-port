namespace Th01.Platform.MonoGame;

/// <summary>
/// Remembers every art lookup that failed (sheet not registered, frame index outside the sheet, sheet that failed to load),
/// once per sheet+index+reason, so the user can send the list and we stop guessing which frames fall back to plain shapes.
/// </summary>
public sealed class MissingArtLog
{
    public const string FileName = "missing-art.local.txt";

    readonly HashSet<string> _seen = new();
    readonly List<string> _lines = new();
    readonly object _gate = new();

    /// <summary>Print each new entry to the console as it happens (--log-missing-art).</summary>
    public bool Realtime { get; set; }

    public int Count { get { lock (_gate) return _lines.Count; } }

    public IReadOnlyList<string> Lines { get { lock (_gate) return _lines.ToArray(); } }

    /// <param name="index">Frame index asked for, or -1 when the whole sheet (or the background) was asked for.</param>
    public void Note(string sheet, int index, string reason)
    {
        string line = index < 0 ? $"{sheet}: {reason}" : $"{sheet}[{index}]: {reason}";
        lock (_gate)
        {
            if (!_seen.Add(line)) return;
            _lines.Add(line);
        }
        if (Realtime) Console.WriteLine($"[missing art] {line}");
    }

    /// <summary>Writes the list (sorted). Returns false if there is nothing to write or the write failed.</summary>
    public bool Save(string path)
    {
        string[] lines;
        lock (_gate) lines = _lines.Order(StringComparer.OrdinalIgnoreCase).ToArray();
        if (lines.Length == 0) return false;
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(path))!);
            File.WriteAllLines(path, lines);
            return true;
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            Console.Error.WriteLine($"Could not write {path}: {e.Message}");
            return false;
        }
    }
}
