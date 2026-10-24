using Th01.Assets;
using Th01.Assets.Config;
using Th01.Assets.Formats;

namespace Th01.Tools.Commands;

/// <summary>
/// inspect-text [folder]: per executable, the text runs it holds and the story screens its code shows
/// (image name, code offset, number of text lines). Offsets, sizes and file names only: the game's text is never printed.
/// </summary>
public static class AssetsScenes
{
    public static int Run(string[] args)
    {
        string? folder = args.Length > 1 ? args[1] : GameDataConfig.Resolve();
        if (folder is null || !Directory.Exists(folder)) { Console.Error.WriteLine("Game data folder not found or not configured."); return 2; }
        foreach (string exe in ExpectedFiles.TextExecutables)
        {
            string path = Path.Combine(folder, exe);
            if (!File.Exists(path)) { Console.WriteLine($"{exe}: file missing"); continue; }
            byte[] bytes = File.ReadAllBytes(path);
            var text = ExeText.Scan(exe, bytes);
            Console.WriteLine($"{exe}: {text.Runs.Count} text run(s), {text.Japanese.Count()} with Japanese");
            var refs = ExeStringRefs.Scan(bytes, text.Runs);
            if (refs is null) { Console.WriteLine("    data segment not located"); continue; }
            var scenes = ExeStringRefs.Scenes(refs.Refs);
            Console.WriteLine($"    {refs.Refs.Count} string reference(s) in code, data base 0x{refs.DataBase:X}, {scenes.Count} scene(s)");
            foreach (var s in scenes) Console.WriteLine($"    scene {s.Image,-12} code 0x{s.CodeOffset:X}, {s.Lines.Count} line(s)");
        }
        int present = ExpectedFiles.CutsceneImages.Count(n => File.Exists(Path.Combine(folder, n)));
        Console.WriteLine($"Cutscene images: {present} of {ExpectedFiles.CutsceneImages.Count} present");
        return 0;
    }
}
