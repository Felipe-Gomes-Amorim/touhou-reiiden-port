using Th01.Assets.Config;
using Th01.Tools.Oracle;

namespace Th01.Parity.Tests;

/// <summary>
/// Parity against traces recorded locally from the original game (never in the repo).
/// Set TH01_TRACE_DIR to a folder holding "name.original.csv" + "name.input.txt" (see docs/ORACLE.md section 9);
/// the port trace is recorded here from the same script and needs the game data (TH01_DATA_PATH).
/// </summary>
public class LocalTraceParityTests
{
    const string EnvVar = "TH01_TRACE_DIR";

    // Nothing is ignored any more: the rng_seed/frame_rand divergences of the first oracle waves were fixed in the port
    // (docs/REQUESTS.md). Columns that only one side has (the original's *_dbl and helper counters) are skipped by the comparer.
    static readonly string[] Known = Array.Empty<string>();

    // First divergent tick per scenario, as measured against the original (docs/ORACLE.md section 17.4). null = must match.
    // When T4/T6 fix a divergence this test fails and tells you to update the number: that is the point.
    static readonly Dictionary<string, long?> ExpectedFirstDivergence = new()
    {
        ["04-cards-combo"] = null,
        ["09-items"] = null,
        // 01/02/03: the bomb_frame-after-the-miss divergence (530/420/302) no longer exists on this base; re-measured in round 4.
        ["01-orb-free"] = null,
        ["02-player-shot"] = null,
        ["03-bomb"] = null,
        ["06-game-over"] = 1430,     // rem_bombs on the last life (original 1, port 5)
        ["05-timer-harryup"] = 1692, // after game over the two sides consume the script differently
    };

    [SkippableFact]
    public void GameplayColumnsMatchTheOriginal()
    {
        string? dir = Environment.GetEnvironmentVariable(EnvVar);
        Skip.If(string.IsNullOrEmpty(dir) || !Directory.Exists(dir),
            $"{EnvVar} is not set (or not a folder): no local original traces to compare. See docs/ORACLE.md.");
        string? data = GameDataConfig.Resolve();
        Skip.If(data is null || !Directory.Exists(data), "Game data folder not configured (TH01_DATA_PATH): needed to load the stage.");

        var originals = Directory.GetFiles(dir!, "*.original.csv");
        Skip.If(originals.Length == 0, $"No *.original.csv files in {dir}.");

        foreach (var file in originals)
        {
            string name = Path.GetFileName(file)[..^".original.csv".Length];
            string scriptPath = Path.Combine(dir!, name + ".input.txt");
            Skip.IfNot(File.Exists(scriptPath), $"{name}.input.txt missing next to the original trace.");
            var sc = InputScript.Load(scriptPath);
            var original = TraceTable.Load(file).AlignByVsync();
            var port = OracleCommands.RecordPortTrace(sc, data, sc.IntDirective("stage") ?? 0, false);
            var r = TraceCompare.Compare(original, port, null, Known);
            long? actual = r.FirstDivergentTick is null ? null : long.Parse(r.FirstDivergentTick);
            ExpectedFirstDivergence.TryGetValue(name, out var expected);
            Assert.True(expected == actual,
                $"{name}: expected first divergence {(expected?.ToString() ?? "none")}, got {(actual?.ToString() ?? "none")}: " +
                string.Join(", ", r.Diffs.Select(d => $"{d.Column} {d.Original}!={d.Port}")));
        }
    }

    /// <summary>The port must not carry state from one session to the next (a hidden static would make results depend on test order).</summary>
    [SkippableFact]
    public void PortSessionsAreIndependentOfEachOther()
    {
        string? dir = Environment.GetEnvironmentVariable(EnvVar);
        string? data = GameDataConfig.Resolve();
        Skip.If(string.IsNullOrEmpty(dir) || !Directory.Exists(dir) || data is null || !Directory.Exists(data),
            $"{EnvVar} and the game data folder are needed.");
        string a = Path.Combine(dir!, "03-bomb.input.txt"), b = Path.Combine(dir!, "04-cards-combo.input.txt");
        Skip.IfNot(File.Exists(a) && File.Exists(b), "03-bomb.input.txt / 04-cards-combo.input.txt not in the trace folder.");
        string Dump(TraceTable t) { var w = new StringWriter(); t.Write(w); return w.ToString(); }
        var first = Dump(OracleCommands.RecordPortTrace(InputScript.Load(a), data, 0, false));
        _ = OracleCommands.RecordPortTrace(InputScript.Load(b), data, 0, false);
        var second = Dump(OracleCommands.RecordPortTrace(InputScript.Load(a), data, 0, false));
        Assert.Equal(first, second);
    }
}
