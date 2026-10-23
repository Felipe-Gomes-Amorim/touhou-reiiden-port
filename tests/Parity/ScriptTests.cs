using Th01.Core.Game;
using Th01.Tools.Oracle;

namespace Th01.Parity.Tests;

/// <summary>Every shipped input script must parse and run headless (sandbox: no game data needed), deterministically.</summary>
public class ScriptTests
{
    static string ScriptsDir
    {
        get
        {
            string? d = AppContext.BaseDirectory;
            while (d is not null && !Directory.Exists(Path.Combine(d, "tests", "Parity", "scripts"))) d = Path.GetDirectoryName(d);
            return Path.Combine(d ?? throw new DirectoryNotFoundException("repo root"), "tests", "Parity", "scripts");
        }
    }

    public static IEnumerable<object[]> Scripts() =>
        Directory.GetFiles(ScriptsDir, "*.input.txt").Select(f => new object[] { Path.GetFileName(f) });

    [Theory]
    [MemberData(nameof(Scripts))]
    public void ScriptRunsAndIsDeterministic(string name)
    {
        var script = InputScript.Load(Path.Combine(ScriptsDir, name));
        var a = OracleCommands.RecordPortTrace(script, null, 0, true);
        var b = OracleCommands.RecordPortTrace(script, null, 0, true);
        Assert.Equal(script.Ticks, a.Rows.Count);
        int col = a.IndexOf("state_hash");
        if (col < 0) col = a.Columns.Count - 1;
        for (int i = 0; i < a.Rows.Count; i++) Assert.Equal(a.Rows[i][col], b.Rows[i][col]);
    }
}
