using System.Globalization;
using System.Text;

namespace Th01.Tools.Oracle;

/// <summary>
/// Per-tick trace (CSV). Lines starting with '#' are metadata ("# key=value"), the first other line is the
/// header, then one row per tick. An empty cell means "not available" and is skipped by the comparer.
/// Traces never go in the repo (docs/ORACLE.md).
/// </summary>
public sealed class TraceTable
{
    /// <summary>Canonical column order. Writers may add more columns after these.</summary>
    public static readonly string[] StandardColumns =
    {
        "tick", "input_bits", "player_x", "player_y", "orb_x", "orb_y", "orb_vx", "orb_vy", "score", "rng_seed",
    };

    public List<string> Columns { get; }
    public List<string[]> Rows { get; } = new();
    public Dictionary<string, string> Meta { get; } = new();

    public TraceTable(IEnumerable<string> columns) => Columns = columns.ToList();

    public int IndexOf(string column) => Columns.IndexOf(column);

    public string Get(int row, string column)
    {
        int i = IndexOf(column);
        return i < 0 ? "" : Rows[row][i];
    }

    public static TraceTable Parse(TextReader reader)
    {
        var meta = new Dictionary<string, string>();
        TraceTable? table = null;
        int lineNo = 0;
        string? line;
        while ((line = reader.ReadLine()) is not null)
        {
            lineNo++;
            if (line.Length == 0) continue;
            if (line[0] == '#')
            {
                int eq = line.IndexOf('=');
                if (eq > 0) meta[line[1..eq].Trim()] = line[(eq + 1)..].Trim();
                continue;
            }
            var cells = line.Split(',').Select(c => c.Trim()).ToArray();
            if (table is null)
            {
                if (cells[0] != "tick")
                    throw new FormatException($"line {lineNo}: header must start with 'tick'");
                table = new TraceTable(cells);
                continue;
            }
            if (cells.Length != table.Columns.Count)
                throw new FormatException($"line {lineNo}: {cells.Length} cells, header has {table.Columns.Count}");
            table.Rows.Add(cells);
        }
        if (table is null) throw new FormatException("trace has no header");
        foreach (var kv in meta) table.Meta[kv.Key] = kv.Value;
        return table;
    }

    public static TraceTable Load(string path)
    {
        using var r = new StreamReader(path, Encoding.UTF8);
        return Parse(r);
    }

    public void Write(TextWriter w)
    {
        foreach (var kv in Meta) w.WriteLine($"# {kv.Key}={kv.Value}");
        w.WriteLine(string.Join(',', Columns));
        foreach (var row in Rows) w.WriteLine(string.Join(',', row));
    }

    /// <summary>
    /// The original records one row per main-loop iteration, but blocking animations (miss, banners) make one
    /// iteration last many VSyncs. With the VSync counter column (16-bit, wraps), inserts blank rows (all cells
    /// empty except tick) so that row index = VSync time, which is what the port models (docs/ORACLE.md section 9).
    /// </summary>
    public TraceTable AlignByVsync(string counterColumn = "z_vsync_count2")
    {
        int ci = IndexOf(counterColumn);
        if (ci < 0) return this;
        var t = new TraceTable(Columns);
        foreach (var kv in Meta) t.Meta[kv.Key] = kv.Value;
        t.Meta["aligned_by"] = counterColumn;
        long prev = -1;
        foreach (var row in Rows)
        {
            long cur = long.Parse(row[ci], CultureInfo.InvariantCulture);
            if (prev >= 0)
                for (long gap = ((cur - prev) & 0xFFFF) - 1; gap > 0; gap--)
                {
                    var blank = new string[Columns.Count];
                    Array.Fill(blank, "");
                    blank[0] = Num(t.Rows.Count);
                    t.Rows.Add(blank);
                }
            var copy = (string[])row.Clone();
            copy[0] = Num(t.Rows.Count);
            t.Rows.Add(copy);
            prev = cur;
        }
        return t;
    }

    public static string Num(long v) => v.ToString(CultureInfo.InvariantCulture);
}
