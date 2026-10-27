using System.Text.Json;
using Th01.Assets.Config;
using Th01.Assets.Disk;

namespace Th01.Platform.MonoGame.Launcher;

/// <summary>What the launcher remembers between runs (in the user's data folder, never in the game folder).</summary>
public sealed record LauncherState(string Source, string DataPath);

/// <summary>Asks the user things. A native dialog on Windows, the console elsewhere; a fake in tests.</summary>
public interface IUserPrompt
{
    /// <summary>Lets the user pick the disk image / game file. Null = cancelled.</summary>
    string? PickGameFile();

    void ShowError(string message);
}

/// <summary>
/// Finds the folder with the user's game files. Order: --data folder, developer config (TH01_DATA_PATH / config.local.json),
/// the remembered choice, and finally asking the user once. The choice is then remembered.
/// </summary>
public sealed class GameLocator(string userDir, IUserPrompt prompt, Func<string?>? developerPath = null)
{
    readonly Func<string?> _developerPath = developerPath ?? (() => GameDataConfig.Resolve());

    public const string StateFile = "launcher.json";

    string StatePath => Path.Combine(userDir, StateFile);

    public LauncherState? LoadState()
    {
        try
        {
            if (!File.Exists(StatePath)) return null;
            return JsonSerializer.Deserialize<LauncherState>(File.ReadAllText(StatePath));
        }
        catch (Exception e) when (e is JsonException or IOException or UnauthorizedAccessException) { return null; }
    }

    public void SaveState(LauncherState state)
    {
        try
        {
            // Only our two keys: the launcher settings live in the same file.
            LauncherFile.Update(StatePath, o => { o["Source"] = state.Source; o["DataPath"] = state.DataPath; });
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            Console.Error.WriteLine($"Could not remember the chosen game file: {e.Message}");
        }
    }

    /// <summary>The remembered game folder if it is still valid.</summary>
    string? Remembered()
    {
        var s = LoadState();
        if (s is null || !Directory.Exists(s.DataPath)) return null;
        return GameInstaller.MissingRequired(s.DataPath).Count == 0 ? s.DataPath : null;
    }

    /// <param name="dataArg">--data folder (used as is, nothing remembered).</param>
    /// <param name="gameArg">--game file or folder (prepared and remembered, no dialog).</param>
    /// <param name="forceSelect">Ignore what is remembered and ask again.</param>
    /// <returns>The game folder, or null when the user cancelled or the choice was invalid (an error was already shown).</returns>
    public string? Resolve(string? dataArg, string? gameArg, bool forceSelect)
    {
        if (dataArg is not null) return dataArg;

        if (gameArg is null && !forceSelect)
        {
            string? dev = _developerPath();
            if (dev is not null && Directory.Exists(dev)) return dev;
            string? remembered = Remembered();
            if (remembered is not null) return remembered;
        }

        string? picked = gameArg ?? prompt.PickGameFile();
        if (picked is null) return null;
        try
        {
            string folder = GameInstaller.Prepare(picked, userDir);
            SaveState(new LauncherState(Path.GetFullPath(picked), folder));
            return folder;
        }
        catch (GameFileException e)
        {
            prompt.ShowError(e.Message);
            return null;
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            prompt.ShowError("The game files could not be read or saved:\n" + e.Message);
            return null;
        }
    }
}
