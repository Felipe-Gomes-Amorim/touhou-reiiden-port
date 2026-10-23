using Th01.Assets;
using Th01.Assets.Config;
using Th01.Assets.Formats;
using Th01.Assets.Model;

namespace Th01.Tools.Commands;

/// <summary>Asset-format commands (T2). Output never contains game bytes, only names, sizes and parse results.</summary>
public static class AssetsCommands
{
    public static bool Handles(string command) => command is "list-pack" or "dump-image" or "extract-pack" or "inspect-exe" or "contact-sheets" or "inspect-text";

    public static int Run(string[] args)
    {
        if (args[0] == "dump-image") return DumpImage(args);
        if (args[0] == "extract-pack") return ExtractPack(args);
        if (args[0] == "inspect-exe") return AssetsExe.Run(args);
        if (args[0] == "contact-sheets") return AssetsSheets.Run(args);
        if (args[0] == "inspect-text") return InspectText(args);
        string? folder = args.Length > 1 ? args[1] : GameDataConfig.Resolve();
        if (folder is null || !Directory.Exists(folder))
        {
            Console.Error.WriteLine("Game data folder not found or not configured.");
            return 2;
        }
        return ListPack(folder);
    }

    /// <summary>dump-image &lt;file | pack:NAME&gt; &lt;out.png&gt; [columns]. Output must go outside the repo.</summary>
    private static int DumpImage(string[] args)
    {
        if (args.Length < 3) { Console.Error.WriteLine("Usage: Th01.Tools dump-image <file | pack:NAME> <out.png> [columns]"); return 2; }
        string input = args[1];
        string name = input;
        byte[] data;
        if (input.StartsWith("pack:", StringComparison.OrdinalIgnoreCase))
        {
            string? folder = GameDataConfig.Resolve();
            if (folder is null) { Console.Error.WriteLine("Game data folder not configured."); return 2; }
            name = input[5..];
            using var fs = File.OpenRead(Path.Combine(folder, ExpectedFiles.ArchiveName));
            data = PackFile.Read(fs, name);
        }
        else data = File.ReadAllBytes(input);

        int columns = args.Length > 3 ? int.Parse(args[3]) : 8;
        Palette4? fallback = null;
        SpriteSheet? sheet = null;
        switch (Path.GetExtension(name).ToUpperInvariant())
        {
            case ".PTN": sheet = PtnSprites.Load(data); break;
            case ".BOS": sheet = BosSprites.Load(data); break;
            case ".GRP": sheet = new SpriteSheet([ToA(GrpImage.Load(data))]); columns = 1; break;
            case ".GRC": sheet = GrcSprites.Load(data); fallback = Gray(); break;
            case ".GRZ": sheet = GrzImage.LoadGrz(data)[args.Length > 3 ? columns : 0]; columns = 2; break; // 4th arg = image index
            case ".GRF": sheet = new SpriteSheet([GrfImage.LoadFirstPlane(data)]); fallback = Gray(); break;
            default: Console.Error.WriteLine($"No reader for '{Path.GetExtension(name)}'."); return 2;
        }
        var (w, h, rgba) = ImageConvert.Contact(sheet, columns, fallback);
        PngWriter.Write(args[2], w, h, rgba);
        Console.WriteLine($"Wrote {args[2]} ({w}x{h}, {sheet.Frames.Count} frame(s))");
        return 0;
    }

    /// <summary>extract-pack &lt;NAME&gt; &lt;out-file&gt;: writes one decoded entry (output must stay outside the repo).</summary>
    private static int ExtractPack(string[] args)
    {
        string? folder = GameDataConfig.Resolve();
        if (args.Length < 3 || folder is null) { Console.Error.WriteLine("Usage: Th01.Tools extract-pack <NAME> <out-file> (needs game data configured)"); return 2; }
        using var fs = File.OpenRead(Path.Combine(folder, ExpectedFiles.ArchiveName));
        File.WriteAllBytes(args[2], PackFile.Read(fs, args[1]));
        return 0;
    }

    private static IndexedImageA ToA(IndexedImage i) => new(i.Width, i.Height, i.Pixels, Enumerable.Repeat((byte)1, i.Pixels.Length).ToArray(), i.Palette);

    private static Palette4 Gray()
    {
        var p = new Palette4();
        p.Set(1, 15, 15, 15);
        return p;
    }

    /// <summary>Prints the archive contents grouped by type (used by inspect-assets). Returns false if it cannot be read.</summary>
    public static bool PrintPackSummary(string folder)
    {
        string path = Path.Combine(folder, ExpectedFiles.ArchiveName);
        if (!File.Exists(path)) return false;
        try
        {
            using var fs = File.OpenRead(path);
            var entries = PackFile.Entries(fs);
            Console.WriteLine($"Archive {ExpectedFiles.ArchiveName}: {entries.Count} file(s)");
            foreach (var g in entries.GroupBy(e => Path.GetExtension(e.Name).ToUpperInvariant()).OrderBy(g => g.Key))
                Console.WriteLine($"  {g.Key,-5} {g.Count(),2}: {string.Join(", ", g.Select(e => e.Name))}");
            var unexpected = entries.Select(e => e.Name).Except(ExpectedFiles.PackEntries, StringComparer.OrdinalIgnoreCase).ToList();
            if (unexpected.Count > 0) Console.WriteLine($"  not in the expected list: {string.Join(", ", unexpected)}");
            return true;
        }
        catch (InvalidDataException e) { Console.WriteLine($"Archive {ExpectedFiles.ArchiveName}: invalid ({e.Message})"); return false; }
    }

    /// <summary>
    /// inspect-text [folder]: per executable, how many Shift-JIS text runs it holds (total / with Japanese characters,
    /// offsets and lengths only, never the text itself), plus which cutscene images are present.
    /// </summary>
    private static int InspectText(string[] args)
    {
        string? folder = args.Length > 1 ? args[1] : GameDataConfig.Resolve();
        if (folder is null || !Directory.Exists(folder)) { Console.Error.WriteLine("Game data folder not found or not configured."); return 2; }
        PrintTextSummary(folder);
        return 0;
    }

    /// <summary>Prints the text/cutscene summary (also meant to be called from inspect-assets). Output has no game text.</summary>
    public static void PrintTextSummary(string folder)
    {
        foreach (string exe in ExpectedFiles.TextExecutables)
        {
            string path = Path.Combine(folder, exe);
            if (!File.Exists(path)) { Console.WriteLine($"Text in {exe}: file missing"); continue; }
            var text = ExeText.Scan(exe, File.ReadAllBytes(path));
            var jp = text.Japanese.ToList();
            Console.WriteLine($"Text in {exe}: {text.Runs.Count} run(s), {jp.Count} with Japanese");
            foreach (var r in jp.Take(8)) Console.WriteLine($"    @0x{r.Offset:X} {r.Length} byte(s), {r.Text.Length} char(s)");
            if (jp.Count > 8) Console.WriteLine($"    ... {jp.Count - 8} more");
        }
        var present = ExpectedFiles.CutsceneImages.Where(n => File.Exists(Path.Combine(folder, n))).ToList();
        Console.WriteLine($"Cutscene images: {present.Count} of {ExpectedFiles.CutsceneImages.Count} present");
    }

    private static int ListPack(string folder)
    {
        string path = Path.Combine(folder, ExpectedFiles.ArchiveName);
        if (!File.Exists(path)) { Console.Error.WriteLine($"Missing {ExpectedFiles.ArchiveName}"); return 1; }
        using var fs = File.OpenRead(path);
        int bad = 0;
        foreach (var e in PackFile.Entries(fs))
        {
            string status;
            try { status = Probe(e.Name, PackFile.Read(fs, e)); }
            catch (Exception ex) when (ex is InvalidDataException or ArgumentException) { status = "ERROR: " + ex.Message; bad++; }
            Console.WriteLine($"{e.Name,-14} {(e.Compressed ? "rle" : "raw"),3} {e.PackSize,8:N0} -> {e.OrgSize,8:N0}  {status}");
        }
        return bad == 0 ? 0 : 1;
    }

    private static string Probe(string name, byte[] data)
    {
        switch (Path.GetExtension(name).ToUpperInvariant())
        {
            case ".PTN": return $"PTN, {PtnSprites.Load(data).Frames.Count} image(s)";
            case ".BOS": { var s = BosSprites.Load(data); return $"BOS, {s.Frames.Count} image(s) {s.FrameWidth}x{s.FrameHeight}"; }
            case ".GRC": { var s = GrcSprites.Load(data); return $"GRC, {s.Frames.Count} image(s) {s.FrameWidth}x{s.FrameHeight}"; }
            case ".DAT" when name.StartsWith("STAGE", StringComparison.OrdinalIgnoreCase): return $"STAGE, {StageDat.Load(data).Count} stage(s)";
            default: return $"{data.Length} bytes (no reader)";
        }
    }
}
