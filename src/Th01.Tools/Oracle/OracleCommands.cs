using Th01.Assets;
using Th01.Assets.Config;
using Th01.Core.Game;
using Th01.Core.Graphics;
using Th01.Core.Input;
using Th01.Core.Sim;

namespace Th01.Tools.Oracle;

/// <summary>CLI entry points for oracle-compare and oracle-record-port.</summary>
public static class OracleCommands
{
    public static bool Handles(string command) => command is "oracle-compare" or "oracle-record-port";

    public static int Run(string command, string[] args) =>
        command == "oracle-compare" ? Compare(args) : RecordPort(args);

    public static void PrintUsage(TextWriter w)
    {
        w.WriteLine("       Th01.Tools oracle-compare <trace-original> <trace-port> [--context N] [--only a,b] [--ignore a,b] [--from-tick N] [--no-vsync-align]");
        w.WriteLine("       Th01.Tools oracle-record-port <input-script> <output-trace.csv> [--data <game folder>] [--stage N] [--sandbox]");
    }

    static int Compare(string[] args)
    {
        var files = new List<string>();
        int context = 3;
        string[]? only = null, ignore = null; long from = 0; bool align = true;
        for (int i = 1; i < args.Length; i++)
        {
            switch (args[i])
            {
                case "--context" when i + 1 < args.Length: context = int.Parse(args[++i]); break;
                case "--only" when i + 1 < args.Length: only = args[++i].Split(','); break;
                case "--ignore" when i + 1 < args.Length: ignore = args[++i].Split(','); break;
                case "--no-vsync-align": align = false; break;
                case "--from-tick" when i + 1 < args.Length: from = long.Parse(args[++i]); break;
                default: files.Add(args[i]); break;
            }
        }
        if (files.Count != 2) { PrintUsage(Console.Error); return 2; }
        try
        {
            var original = TraceTable.Load(files[0]);
            var port = TraceTable.Load(files[1]);
            if (align) original = original.AlignByVsync();
            var result = TraceCompare.Compare(original, port, only, ignore, from);
            Console.Write(TraceCompare.Format(original, port, result, context));
            return result.Equal ? 0 : 1;
        }
        catch (Exception e) when (e is IOException or FormatException)
        {
            Console.Error.WriteLine($"error: {e.Message}");
            return 2;
        }
    }

    static int RecordPort(string[] args)
    {
        var pos = new List<string>();
        string? data = null; int stage = 0; bool sandbox = false;
        for (int i = 1; i < args.Length; i++)
        {
            switch (args[i])
            {
                case "--data" when i + 1 < args.Length: data = args[++i]; break;
                case "--stage" when i + 1 < args.Length: stage = int.Parse(args[++i]); break;
                case "--sandbox": sandbox = true; break;
                default: pos.Add(args[i]); break;
            }
        }
        if (pos.Count != 2) { PrintUsage(Console.Error); return 2; }
        try
        {
            var script = InputScript.Load(pos[0]);
            var trace = RecordPortTrace(script, data ?? GameDataConfig.Resolve(), stage, sandbox);
            using var w = new StreamWriter(pos[1]);
            trace.Write(w);
            Console.WriteLine($"Wrote {trace.Rows.Count} rows to {pos[1]} ({(sandbox ? "sandbox: no cards/obstacles" : $"stage {stage + 1}")}, frame_rand={script.Seed}).");
            return 0;
        }
        catch (Exception e) when (e is IOException or FormatException or InvalidDataException or ArgumentException)
        {
            Console.Error.WriteLine($"error: {e.Message}");
            return 2;
        }
    }

    /// <summary>Runs the real gameplay session headless on the script (stage = 0-based, sandbox = no cards/obstacles).</summary>
    public static TraceTable RecordPortTrace(InputScript script, string? dataFolder, int stage, bool sandbox)
    {
        IGameSession session = CreateSession(script.Seed, sandbox ? null : LoadStage(dataFolder, stage));
        return PortRecorder.Record(session, script);
    }

    // The script's "seed" is frame_rand: the original hands it to irand_init() when the stage starts.
    static GameSession CreateSession(uint frameRand, StageSetup? stage) =>
        new(new GameOptions { FrameRand = frameRand, Stage = stage });

    static StageSetup LoadStage(string? dataFolder, int stageId)
    {
        if (dataFolder is null || !Directory.Exists(dataFolder))
            throw new ArgumentException($"game data folder not found; pass --data or set {GameDataConfig.EnvVar} (or use --sandbox)");
        var assets = new GameAssets(new FolderAssetSource(dataFolder));
        var layout = assets.GetStage(stageId);   // scene 0, route-independent for the first scene
        var cards = new bool[StageSetup.Rows, StageSetup.Cols];
        var obstacles = new byte[StageSetup.Rows, StageSetup.Cols];
        Array.Copy(layout.Cards, cards, cards.Length);
        Array.Copy(layout.Obstacles, obstacles, obstacles.Length);
        return new StageSetup { StageId = stageId, Route = 0, Cards = cards, Obstacles = obstacles };
    }

}
