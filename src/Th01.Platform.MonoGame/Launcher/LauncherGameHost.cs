using Th01.Assets;
using Th01.Assets.Config;
using Th01.Assets.Disk;

namespace Th01.Platform.MonoGame.Launcher;

/// <summary>Outcome of choosing a game in the launcher: a usable game, or the reason the choice was refused.</summary>
public sealed record GameChoice(LauncherGameInfo? Game, GameProblemCode? ErrorCode = null, string? ErrorFile = null);

/// <summary>What the launcher calls for the DIRECTORY / "Change…" / "Select game…" actions. Null = the user cancelled.</summary>
public interface IGameChooser
{
    GameChoice? Choose();
}

/// <summary>
/// The game copy the launcher works with: where it came from, the folder with its files, what <see cref="GameDataCheck"/> says
/// about it. Remembers the choice in launcher.json (next to the language, never in the game folder).
/// </summary>
public sealed class LauncherGameHost(string userDir, IUserPrompt prompt, Func<string?>? developerPath = null) : IGameChooser
{
    readonly GameLocator _locator = new(userDir, prompt, developerPath);
    readonly Func<string?> _developerPath = developerPath ?? (() => GameDataConfig.Resolve());

    /// <summary>Folder with the game's files, null while no valid game is chosen.</summary>
    public string? DataPath { get; private set; }
    public LauncherGameInfo Current { get; private set; } = LauncherGameInfo.None;

    /// <summary>Picks the game without asking anything: --data, --game, the developer folder, then the remembered choice.</summary>
    public GameChoice? Startup(string? dataArg, string? gameArg)
    {
        if (dataArg is not null) return Adopt(dataArg, dataArg);
        if (gameArg is not null) return ChoosePath(gameArg);
        string? dev = _developerPath();
        if (dev is not null && Directory.Exists(dev)) return Adopt(dev, dev);
        var s = _locator.LoadState();
        if (s is not null && Directory.Exists(s.DataPath))
        {
            var choice = Adopt(s.DataPath, s.Source);
            if (choice.Game is not null) return choice;
        }
        return null;
    }

    public GameChoice? Choose()
    {
        string? picked = prompt.PickGameFile();
        return picked is null ? null : ChoosePath(picked);
    }

    /// <summary>Checks and, when the game is usable, adopts and remembers what the user picked (a .hdi, a folder, or a file in one).</summary>
    public GameChoice ChoosePath(string picked)
    {
        var report = GameDataCheck.Inspect(picked);
        if (!report.Valid) return Refuse(report);
        string folder;
        try { folder = GameInstaller.Prepare(picked, userDir); }
        catch (GameFileException) { return new GameChoice(null, GameProblemCode.UnreadableFile, null); }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException) { return new GameChoice(null, GameProblemCode.UnreadableFile, null); }
        string source = Path.GetFullPath(picked);
        _locator.SaveState(new LauncherState(source, folder));
        return Adopt(folder, source, report);
    }

    static GameChoice Refuse(GameDataReport report)
    {
        var first = report.Problems.FirstOrDefault(p => p.IsError);
        return new GameChoice(null, first?.Code ?? GameProblemCode.NotFound, first?.File);
    }

    GameChoice Adopt(string folder, string origin, GameDataReport? report = null)
    {
        report ??= GameDataCheck.Inspect(folder);
        if (!report.Valid) return Refuse(report);
        DataPath = folder;
        Current = new LauncherGameInfo(origin, true, report.Edition switch
        {
            GameEdition.Japanese => GameVersion.Japanese,
            GameEdition.Translated => GameVersion.Translated,
            _ => GameVersion.Unknown,
        });
        return new GameChoice(Current);
    }
}
