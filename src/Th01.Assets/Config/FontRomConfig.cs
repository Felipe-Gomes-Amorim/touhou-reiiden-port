using System.Text.Json;
using Th01.Assets.Text;

namespace Th01.Assets.Config;

/// <summary>
/// Where the user's own PC-98 font ROM image is. Resolution order: 1) environment variable TH01_FONT_ROM,
/// 2) "fontRomPath" in config.local.json (gitignored), 3) FONT.ROM inside the game data folder.
/// The file is never part of the repository.
/// </summary>
public static class FontRomConfig
{
    public const string EnvVar = "TH01_FONT_ROM";
    public const string FileName = "FONT.ROM";

    /// <summary>Returns the path of an existing ROM file, or null when none is configured or the file is missing.</summary>
    public static string? Resolve(string? searchFrom = null)
    {
        string? path = Environment.GetEnvironmentVariable(EnvVar);
        if (string.IsNullOrWhiteSpace(path)) path = FromConfigFile(searchFrom);
        if (string.IsNullOrWhiteSpace(path) && GameDataConfig.Resolve(searchFrom) is { } data)
            path = Path.Combine(data, FileName);
        return path is not null && File.Exists(path) ? path : null;
    }

    /// <summary>Loads the configured ROM with the layout of the common emulator image, or null when there is none or it is not that image.</summary>
    public static PcFontRom? TryLoad(string? searchFrom = null)
    {
        string? path = Resolve(searchFrom);
        if (path is null) return null;
        byte[] rom = File.ReadAllBytes(path);
        return PcFontRom.LooksLikeEmulatorImage(rom) ? new PcFontRom(rom, FontRomLayout.EmulatorImage) : null;
    }

    private static string? FromConfigFile(string? start)
    {
        for (var dir = new DirectoryInfo(start ?? Directory.GetCurrentDirectory()); dir != null; dir = dir.Parent)
        {
            string file = Path.Combine(dir.FullName, GameDataConfig.FileName);
            if (!File.Exists(file)) continue;
            try
            {
                using var doc = JsonDocument.Parse(File.ReadAllText(file));
                if (doc.RootElement.TryGetProperty("fontRomPath", out var p) && p.GetString() is { Length: > 0 } s) return s;
            }
            catch (JsonException) { /* malformed file: treat as not configured */ }
        }
        return null;
    }
}
