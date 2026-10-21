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

    // Known, documented port deviations (docs/ORACLE.md section 9): excluded until T4 fixes them.
    //  - rng_seed / frame_rand: the port increments frame_rand one tick after irand_init and misses one irand call
    //    in the first iteration after a life restart.
    //  - *_dbl, *_bits, z_vsync_count2, frame_since_start: original-only helper columns.
    static readonly string[] Known = { "rng_seed", "frame_rand" };

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
            var original = TraceTable.Load(file).AlignByVsync();
            var port = OracleCommands.RecordPortTrace(InputScript.Load(scriptPath), data, 0, false);
            var r = TraceCompare.Compare(original, port, null, Known);
            Assert.True(r.FirstDivergentTick is null,
                $"{name}: first divergence at tick {r.FirstDivergentTick}: " + string.Join(", ", r.Diffs.Select(d => $"{d.Column} {d.Original}!={d.Port}")));
        }
    }
}
