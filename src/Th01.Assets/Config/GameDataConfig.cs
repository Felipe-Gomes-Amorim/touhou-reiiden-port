using System.Text.Json;

namespace Th01.Assets.Config;

/// <summary>
/// Where the user's own copy of the game files lives. Resolution order:
/// 1) environment variable TH01_DATA_PATH, 2) "config.local.json" (gitignored) with {"gameDataPath": "..."}.
/// </summary>
public static class GameDataConfig
{
    public const string EnvVar = "TH01_DATA_PATH";
    public const string FileName = "config.local.json";

    /// <summary>Returns the configured folder, or null if nothing is configured.</summary>
    public static string? Resolve(string? searchFrom = null)
    {
        string? env = Environment.GetEnvironmentVariable(EnvVar);
        if (!string.IsNullOrWhiteSpace(env)) return env;

        foreach (string dir in CandidateDirectories(searchFrom))
        {
            string path = Path.Combine(dir, FileName);
            if (!File.Exists(path)) continue;
            try
            {
                using var doc = JsonDocument.Parse(File.ReadAllText(path));
                if (doc.RootElement.TryGetProperty("gameDataPath", out var p) && p.GetString() is { Length: > 0 } s)
                    return s;
            }
            catch (JsonException) { /* malformed file: treat as not configured */ }
        }
        return null;
    }

    static IEnumerable<string> CandidateDirectories(string? start)
    {
        // Walk up from the start dir (or cwd) so it works from bin/ output folders too.
        var dir = new DirectoryInfo(start ?? Directory.GetCurrentDirectory());
        for (; dir != null; dir = dir.Parent) yield return dir.FullName;
    }
}
