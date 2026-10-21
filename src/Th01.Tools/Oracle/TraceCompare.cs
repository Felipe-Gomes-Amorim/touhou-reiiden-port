using System.Globalization;
using System.Text;

namespace Th01.Tools.Oracle;

public sealed record TraceDiff(string Column, string Original, string Port);

public sealed class CompareResult
{
    public bool Equal => FirstDivergentTick is null && LengthNote is null;
    public string? FirstDivergentTick { get; init; }
    public int DivergentRowIndex { get; init; } = -1;
    public List<TraceDiff> Diffs { get; init; } = new();
    public List<string> ComparedColumns { get; init; } = new();
    public string? LengthNote { get; init; }
}

public static class TraceCompare
{
    /// <param name="only">Restrict to these columns (null = every column both traces have).</param>
    /// <param name="ignore">Columns to skip.</param>
    /// <param name="fromTick">Skip rows before this tick (e.g. rng_seed is undefined before irand_init).</param>
    public static CompareResult Compare(TraceTable original, TraceTable port,
        IReadOnlyCollection<string>? only = null, IReadOnlyCollection<string>? ignore = null, long fromTick = 0)
    {
        var cols = original.Columns
            .Where(c => c != "tick" && port.IndexOf(c) >= 0)
            .Where(c => only is null || only.Contains(c))
            .Where(c => ignore is null || !ignore.Contains(c))
            .ToList();

        int n = Math.Min(original.Rows.Count, port.Rows.Count);
        for (int i = 0; i < n; i++)
        {
            string to = original.Get(i, "tick"), tp = port.Get(i, "tick");
            if (long.TryParse(to, out var tn) && tn < fromTick) continue;
            var diffs = new List<TraceDiff>();
            if (!SameValue(to, tp)) diffs.Add(new TraceDiff("tick", to, tp));
            foreach (var c in cols)
            {
                string a = original.Get(i, c), b = port.Get(i, c);
                if (a.Length == 0 || b.Length == 0) continue; // not available on one side
                if (!SameValue(a, b)) diffs.Add(new TraceDiff(c, a, b));
            }
            if (diffs.Count > 0)
                return new CompareResult { FirstDivergentTick = to, DivergentRowIndex = i, Diffs = diffs, ComparedColumns = cols };
        }
        string? note = original.Rows.Count == port.Rows.Count ? null
            : $"traces differ in length: original {original.Rows.Count} ticks, port {port.Rows.Count} ticks (first {n} match)";
        return new CompareResult { LengthNote = note, ComparedColumns = cols };
    }

    static bool SameValue(string a, string b)
    {
        if (long.TryParse(a, NumberStyles.Integer, CultureInfo.InvariantCulture, out var x) &&
            long.TryParse(b, NumberStyles.Integer, CultureInfo.InvariantCulture, out var y)) return x == y;
        return a == b;
    }

    public static string Format(TraceTable original, TraceTable port, CompareResult r, int context)
    {
        var sb = new StringBuilder();
        sb.AppendLine($"Compared columns: {string.Join(", ", r.ComparedColumns)}");
        if (r.Equal) { sb.AppendLine("OK: traces match on every compared tick."); return sb.ToString(); }
        if (r.FirstDivergentTick is not null)
        {
            sb.AppendLine($"DIVERGENCE at tick {r.FirstDivergentTick} (row {r.DivergentRowIndex}).");
            foreach (var d in r.Diffs)
                sb.AppendLine($"  {d.Column,-12} original={d.Original,-12} port={d.Port}");
            sb.AppendLine();
            int from = Math.Max(0, r.DivergentRowIndex - context);
            int to = Math.Min(Math.Min(original.Rows.Count, port.Rows.Count) - 1, r.DivergentRowIndex + context);
            var cols = new[] { "tick" }.Concat(r.ComparedColumns).ToList();
            sb.AppendLine("   side     " + string.Join(' ', cols.Select(c => c.PadLeft(10))));
            for (int i = from; i <= to; i++)
            {
                string mark = i == r.DivergentRowIndex ? ">>" : "  ";
                sb.AppendLine($"{mark} original " + string.Join(' ', cols.Select(c => original.Get(i, c).PadLeft(10))));
                sb.AppendLine($"{mark} port     " + string.Join(' ', cols.Select(c => port.Get(i, c).PadLeft(10))));
            }
        }
        if (r.LengthNote is not null) sb.AppendLine(r.LengthNote);
        return sb.ToString();
    }
}
