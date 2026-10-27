using System.Text.Json;
using System.Text.Json.Nodes;

namespace Th01.Platform.MonoGame.Launcher;

/// <summary>
/// launcher.json is shared by the game locator (remembered game) and the launcher settings (language, options).
/// Each writer changes only its own keys and keeps the rest, so one never erases the other.
/// </summary>
static class LauncherFile
{
    public static JsonObject Read(string path)
    {
        try
        {
            if (File.Exists(path) && JsonNode.Parse(File.ReadAllText(path)) is JsonObject o) return o;
        }
        catch (Exception e) when (e is JsonException or IOException or UnauthorizedAccessException) { /* start over */ }
        return new JsonObject();
    }

    public static void Update(string path, Action<JsonObject> change)
    {
        var o = Read(path);
        change(o);
        string? dir = Path.GetDirectoryName(Path.GetFullPath(path));
        if (dir is not null) Directory.CreateDirectory(dir);
        File.WriteAllText(path, o.ToJsonString());
    }
}
