using Th01.Assets;
using Th01.Assets.Config;
using Th01.Assets.Formats;

namespace Th01.Tools.Commands;

/// <summary>verify-assets: loads every image/sprite file with its reader and prints sizes (names and numbers only).</summary>
public static class AssetsVerify
{
    public static int Run()
    {
        string? folder = GameDataConfig.Resolve();
        if (folder is null || !Directory.Exists(folder)) { Console.Error.WriteLine("Game data folder not configured."); return 2; }
        var assets = new GameAssets(new FolderAssetSource(folder));
        int ok = 0, bad = 0;

        void Report(string name, Func<string> load)
        {
            try { Console.WriteLine($"  OK    {name,-14} {load()}"); ok++; }
            catch (Exception e) when (e is InvalidDataException or FileNotFoundException or ArgumentException)
            { Console.WriteLine($"  FAIL  {name,-14} {e.Message}"); bad++; }
        }

        Console.WriteLine("Archive:");
        foreach (string n in assets.PackNames)
        {
            switch (Path.GetExtension(n).ToUpperInvariant())
            {
                case ".PTN": Report(n, () => { var s = assets.GetPtn(n); return $"{s.Frames.Count} frame(s) {s.FrameWidth}x{s.FrameHeight}"; }); break;
                case ".BOS": Report(n, () => { var s = assets.GetBos(n); return $"{s.Frames.Count} frame(s) {s.FrameWidth}x{s.FrameHeight}"; }); break;
                case ".GRC": Report(n, () => { var s = assets.GetGrc(n); return $"{s.Frames.Count} frame(s) {s.FrameWidth}x{s.FrameHeight}"; }); break;
                case ".DAT": Report(n, () => $"{assets.GetScene(int.Parse(n[5..6])).Count} stage(s)"); break;
            }
        }
        Console.WriteLine("Folder:");
        foreach (string f in Directory.GetFiles(folder).Select(p => Path.GetFileName(p)).OrderBy(x => x))
        {
            switch (Path.GetExtension(f).ToUpperInvariant())
            {
                case ".GRP": Report(f, () => { var i = assets.GetGrp(f); return $"{i.Width}x{i.Height}, palette {(i.Palette is null ? "none" : "yes")}"; }); break;
                case ".GRZ": Report(f, () => { var l = assets.GetGrz(f); return $"{l.Count} image(s), {string.Join("/", l.Select(s => s.Frames.Count))} stream(s)"; }); break;
                case ".GRF": Report(f, () => { var i = assets.GetHudMask(); return $"{i.Width}x{i.Height}"; }); break;
            }
        }
        Console.WriteLine($"{ok} ok, {bad} failed");
        return bad == 0 ? 0 : 1;
    }
}
