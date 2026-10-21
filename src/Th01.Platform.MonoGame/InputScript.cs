using System.Text.Json;
using System.Text.Json.Serialization;
using Th01.Core.Input;

namespace Th01.Platform.MonoGame;

/// <summary>
/// The list of <see cref="GameButtons"/> per tick, stored run-length encoded. Recorded from tick 0 of a fresh session
/// so replaying it reproduces the run (the factory uses a fixed seed). Saved as input-script.local.json (gitignored).
/// </summary>
public sealed class InputScript
{
    public const string FileName = "input-script.local.json";

    readonly List<(int Count, ushort Buttons)> _runs = new();

    public int TickCount { get; private set; }

    public void Append(GameButtons buttons)
    {
        ushort b = (ushort)buttons;
        if (_runs.Count > 0 && _runs[^1].Buttons == b) _runs[^1] = (_runs[^1].Count + 1, b);
        else _runs.Add((1, b));
        TickCount++;
    }

    public GameButtons At(int tick)
    {
        if ((uint)tick >= (uint)TickCount) return GameButtons.None;
        foreach (var (count, b) in _runs)
        {
            if (tick < count) return (GameButtons)b;
            tick -= count;
        }
        return GameButtons.None;
    }

    sealed record Model(
        [property: JsonPropertyName("version")] int Version,
        [property: JsonPropertyName("ticks")] int Ticks,
        [property: JsonPropertyName("runs")] int[][] Runs);

    public void Save(string path)
    {
        var m = new Model(1, TickCount, _runs.Select(r => new[] { r.Count, (int)r.Buttons }).ToArray());
        File.WriteAllText(path, JsonSerializer.Serialize(m));
    }

    /// <summary>Loads a script, or returns null with a short reason if the file is missing or malformed.</summary>
    public static InputScript? TryLoad(string path, out string? error)
    {
        error = null;
        if (!File.Exists(path)) { error = $"{path} does not exist (record one first)."; return null; }
        try
        {
            var m = JsonSerializer.Deserialize<Model>(File.ReadAllText(path));
            if (m is null || m.Version != 1) { error = "unsupported script version"; return null; }
            var s = new InputScript();
            foreach (var r in m.Runs)
            {
                if (r.Length != 2 || r[0] <= 0 || r[1] is < 0 or > ushort.MaxValue) { error = "bad run"; return null; }
                s._runs.Add((r[0], (ushort)r[1]));
                s.TickCount += r[0];
            }
            return s;
        }
        catch (JsonException e) { error = e.Message; return null; }
    }
}
