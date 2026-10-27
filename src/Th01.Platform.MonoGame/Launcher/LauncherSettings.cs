using System.Text.Json;
using System.Text.Json.Serialization;

namespace Th01.Platform.MonoGame.Launcher;

/// <summary>
/// What the player chose in the launcher, kept in launcher.json in the user's data folder (never in the repository).
/// A missing or damaged file gives the defaults.
/// </summary>
public sealed class LauncherSettings
{
    public const string FileName = "launcher.json";

    public LauncherLanguage Language { get; set; } = LauncherLanguage.English;

    sealed record Model([property: JsonPropertyName("language")] string? Language);

    public static LauncherSettings Load(string? path = null)
    {
        path ??= Path.Combine(Directory.GetCurrentDirectory(), FileName);
        var s = new LauncherSettings();
        try
        {
            if (!File.Exists(path)) return s;
            var m = JsonSerializer.Deserialize<Model>(File.ReadAllText(path));
            if (m is null) return s;
            if (string.Equals(m.Language, "ja", StringComparison.OrdinalIgnoreCase)) s.Language = LauncherLanguage.Japanese;
        }
        catch (Exception e) when (e is JsonException or IOException or UnauthorizedAccessException) { /* keep defaults */ }
        return s;
    }

    public void Save(string? path = null)
    {
        path ??= Path.Combine(Directory.GetCurrentDirectory(), FileName);
        File.WriteAllText(path, JsonSerializer.Serialize(new Model(Language == LauncherLanguage.Japanese ? "ja" : "en")));
    }
}
