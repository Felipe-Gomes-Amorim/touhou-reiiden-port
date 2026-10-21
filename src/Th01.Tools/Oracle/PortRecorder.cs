using Th01.Core.Diagnostics;
using Th01.Core.Input;
using Th01.Core.Sim;

namespace Th01.Tools.Oracle;

public static class PortRecorder
{
    public static readonly string[] Columns = TraceTable.StandardColumns.Append("state_hash").ToArray();

    /// <summary>Standard columns, then any extra keys the session exposes, then state_hash.</summary>
    static string[] ColumnsFor(ITraceSource? tracer)
    {
        var extra = tracer?.TraceValues().Keys.Where(k => !TraceTable.StandardColumns.Contains(k)).OrderBy(k => k, StringComparer.Ordinal) ?? Enumerable.Empty<string>();
        return TraceTable.StandardColumns.Concat(extra).Append("state_hash").ToArray();
    }

    /// <summary>
    /// Runs the session headless. Row N is the state before Step number N, with the input that Step N will
    /// receive (row 0 = initial state; Ticks rows). The emulator probe records the same way (docs/ORACLE.md section 5).
    /// </summary>
    public static TraceTable Record(IGameSession session, InputScript script)
    {
        var tracer = session as ITraceSource;
        var columns = ColumnsFor(tracer);
        var t = new TraceTable(columns);
        t.Meta["source"] = "port";
        t.Meta["seed"] = script.Seed.ToString();
        for (int tick = 0; tick < script.Ticks; tick++)
        {
            GameButtons held = script.At(tick);
            var vals = tracer?.TraceValues();
            var row = new string[columns.Length];
            for (int c = 0; c < row.Length; c++)
            {
                string name = columns[c];
                row[c] = name switch
                {
                    "tick" => TraceTable.Num(tick),
                    "input_bits" => TraceTable.Num((int)held),
                    "state_hash" => session.StateHash().ToString("x16"),
                    _ => vals is not null && vals.TryGetValue(name, out var v) ? TraceTable.Num(v) : "",
                };
            }
            t.Rows.Add(row);
            session.Step(held);
        }
        return t;
    }
}
