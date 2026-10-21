using Th01.Core.Input;

namespace Th01.Tools.Oracle;

/// <summary>
/// Text input script, shared by the port recorder and the emulator-side driver (docs/ORACLE.md).
/// <code>
/// # comment
/// seed 1234          initial irand seed (decimal or 0x hex)
/// ticks 600          total number of ticks to run
/// 0   none           from tick 0 the held set is empty
/// 10  left shot      from tick 10 Left+Shot are held (until the next line)
/// 30  none
/// </code>
/// Names: up down left right shot bomb pause ok cancel none (case-insensitive).
/// </summary>
public sealed class InputScript
{
    public uint Seed { get; private set; } = 1;
    public int Ticks { get; private set; }
    readonly List<(int Tick, GameButtons Buttons)> _changes = new();

    public static InputScript Parse(TextReader reader)
    {
        var s = new InputScript();
        string? line;
        int lineNo = 0;
        while ((line = reader.ReadLine()) is not null)
        {
            lineNo++;
            int hash = line.IndexOf('#');
            if (hash >= 0) line = line[..hash];
            var parts = line.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries);
            if (parts.Length == 0) continue;
            try
            {
                switch (parts[0].ToLowerInvariant())
                {
                    case "seed": s.Seed = ParseUInt(parts[1]); break;
                    case "ticks": s.Ticks = (int)ParseUInt(parts[1]); break;
                    default:
                        int tick = (int)ParseUInt(parts[0]);
                        if (s._changes.Count > 0 && tick <= s._changes[^1].Tick)
                            throw new FormatException("ticks must be strictly increasing");
                        s._changes.Add((tick, ParseButtons(parts.Skip(1))));
                        break;
                }
            }
            catch (Exception e) when (e is FormatException or IndexOutOfRangeException or OverflowException)
            {
                throw new FormatException($"input script line {lineNo}: {e.Message}");
            }
        }
        if (s.Ticks <= 0) throw new FormatException("input script needs a 'ticks N' line");
        return s;
    }

    public static InputScript Load(string path)
    {
        using var r = new StreamReader(path);
        return Parse(r);
    }

    /// <summary>Held buttons at <paramref name="tick"/>.</summary>
    public GameButtons At(int tick)
    {
        var cur = GameButtons.None;
        foreach (var (t, b) in _changes)
        {
            if (t > tick) break;
            cur = b;
        }
        return cur;
    }

    static uint ParseUInt(string s) =>
        s.StartsWith("0x", StringComparison.OrdinalIgnoreCase) ? Convert.ToUInt32(s[2..], 16) : uint.Parse(s);

    static GameButtons ParseButtons(IEnumerable<string> names)
    {
        var b = GameButtons.None;
        foreach (var n in names)
        {
            if (n.Equals("none", StringComparison.OrdinalIgnoreCase)) continue;
            if (!Enum.TryParse<GameButtons>(n, true, out var one) || one == GameButtons.None)
                throw new FormatException($"unknown button '{n}'");
            b |= one;
        }
        return b;
    }
}
