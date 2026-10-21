using Th01.Assets;
using Th01.Assets.Config;
using Th01.Tools.Commands;
using Th01.Assets.Disk;
using Th01.Tools.Oracle;

static int Usage()
{
    Console.Error.WriteLine("Usage:");
    Console.Error.WriteLine("  Th01.Tools inspect-assets [folder]");
    Console.Error.WriteLine($"      (folder defaults to ${GameDataConfig.EnvVar} or {GameDataConfig.FileName})");
    Console.Error.WriteLine("  Th01.Tools list-pack [folder]   (lists the archive and checks that every entry parses)");
    Console.Error.WriteLine("  Th01.Tools dump-image <file | pack:NAME> <out.png> [columns]   (out.png must be outside the repo)");
    Console.Error.WriteLine("  Th01.Tools extract-pack <NAME> <out-file>");
    Console.Error.WriteLine("  Th01.Tools contact-sheets <out-dir>   (PNG sheets for visual checks; keep out of the repo)");
    Console.Error.WriteLine("  Th01.Tools inspect-exe <file.exe> <folder with reference .bmp files>");
    Console.Error.WriteLine("  Th01.Tools list-hdi <image.hdi>");
    Console.Error.WriteLine("  Th01.Tools extract-hdi <image.hdi> <out-folder> [--all]");
    Console.Error.WriteLine("      extracts the game files of the image root (--all: include DOS system files and subfolders)");
    OracleCommands.PrintUsage(Console.Error);
    return 2;
}

Console.OutputEncoding = System.Text.Encoding.UTF8;

if (args.Length == 0) return Usage();

if (OracleCommands.Handles(args[0])) return OracleCommands.Run(args[0], args);

switch (args[0])
{
    case "inspect-assets":
        string? folder = args.Length > 1 ? args[1] : GameDataConfig.Resolve();
        if (folder is null || !Directory.Exists(folder))
        {
            Console.Error.WriteLine("Game data folder not found or not configured.");
            return Usage();
        }
        return InspectAssets(folder);
    case "list-hdi" when args.Length == 2:
        return Guard(() => ListHdi(args[1]));
    case "extract-hdi" when args.Length is 3 or 4:
        return Guard(() => ExtractHdi(args[1], args[2], args.Length == 4 && args[3] == "--all"));
    case var c when AssetsCommands.Handles(c):
        return Guard(() => AssetsCommands.Run(args));
    default:
        return Usage();
}

static int Guard(Func<int> action)
{
    try { return action(); }
    catch (Exception e) when (e is IOException or InvalidDataException or UnauthorizedAccessException)
    {
        Console.Error.WriteLine($"Error: {e.Message}");
        return 1;
    }
}

static int InspectAssets(string folder)
{
    var src = new FolderAssetSource(folder);
    Console.WriteLine($"Folder: {folder}");
    int missingRequired = 0;
    foreach (var f in ExpectedFiles.All)
    {
        long? size = src.GetSize(f.Name);
        string status = size is null ? (f.Required ? "MISSING (required)" : "missing") : $"{size,10:N0} bytes";
        if (size is null && f.Required) missingRequired++;
        Console.WriteLine($"  {f.Name,-16} {status,-22} {f.Note}");
    }
    AssetsCommands.PrintPackSummary(folder);
    Console.WriteLine(missingRequired == 0 ? "All required files present." : $"{missingRequired} required file(s) missing.");
    return missingRequired == 0 ? 0 : 1;
}

static int ListHdi(string path)
{
    var img = new HdiImage(File.ReadAllBytes(path));
    foreach (var e in img.Enumerate())
        Console.WriteLine(e.IsDirectory ? $"{"<dir>",12}  {e.Path}/" : $"{e.Size,12:N0}  {e.Path}");
    return 0;
}

static int ExtractHdi(string path, string outDir, bool all)
{
    int count = GameInstaller.ExtractRoot(new HdiImage(File.ReadAllBytes(path)), outDir, all);
    Console.WriteLine($"Extracted {count} file(s) to {Path.GetFullPath(outDir)}");
    return 0;
}
