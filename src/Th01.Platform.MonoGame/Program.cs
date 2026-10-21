using Th01.Assets;
using Th01.Assets.Config;
using Th01.Platform.MonoGame;
using Th01.Platform.MonoGame.Launcher;

// Usage: ReiidenPort [--stage N] [--route 0|1] [--select] [--game <file|folder>] [--data <folder>]
//   no arguments: the first run asks for your .hdi (remembered afterwards), then logo -> title/menu.
//   --select: ask for the game file again (holding Shift at startup does the same on Windows).
//   --game: use this .hdi/folder instead of asking (remembered). --data: use this folder as is (not remembered).
//   --stage N (1..20) starts that stage directly, skipping the title; route 0 = Makai, 1 = Jigoku.
int? stage = null;
int route = 0;
string? dataArg = null, gameArg = null;
bool forceSelect = UserPrompts.ShiftHeld();
for (int i = 0; i < args.Length; i++)
{
    if (args[i] == "--stage" && i + 1 < args.Length && int.TryParse(args[++i], out int s) && s is >= 1 and <= 20) stage = s - 1;
    else if (args[i] == "--route" && i + 1 < args.Length && int.TryParse(args[++i], out int r) && r is 0 or 1) route = r;
    else if (args[i] == "--data" && i + 1 < args.Length) dataArg = args[++i];
    else if (args[i] == "--game" && i + 1 < args.Length) gameArg = args[++i];
    else if (args[i] == "--select") forceSelect = true;
    else { Console.Error.WriteLine($"Ignoring argument '{args[i]}'. Usage: [--stage 1..20] [--route 0|1] [--select] [--game <file>] [--data <folder>]"); }
}

// REIIDENPORT_HOME overrides the data folder (portable use, tests).
string userDir = Environment.GetEnvironmentVariable("REIIDENPORT_HOME") is { Length: > 0 } home
    ? home
    : Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "ReiidenPort");
var prompt = UserPrompts.Create();
string? dataPath = new GameLocator(userDir, prompt).Resolve(dataArg, gameArg, forceSelect);
if (dataPath is null) return 1; // cancelled or invalid; the user has already been told why
// Settings, key bindings and input scripts are written to the current directory: keep them in the user's data folder.
if (dataArg is null && GameDataConfig.Resolve() is null)
{
    try { Directory.CreateDirectory(userDir); Directory.SetCurrentDirectory(userDir); }
    catch (Exception e) when (e is IOException or UnauthorizedAccessException) { /* keep the current directory */ }
}
Console.WriteLine($"Game data folder: {dataPath}");
Console.WriteLine("Keys: F11 fullscreen | F10 scale mode | F5 pause | F6 step one tick | F3 debug title | F7 record input | F8 replay | Esc quit");

Func<GameFlow?>? factory = null;
if (dataPath is not null)
{
    if (!Directory.Exists(dataPath))
        Console.Error.WriteLine($"The configured game data folder does not exist: {dataPath}. Showing the test pattern only.");
    else
    {
        var assets = new FolderAssetSource(dataPath);
        var font = Th01.Core.Graphics.BdfFont.LoadSpleen8x16();
        var settings = GameSettings.Load();
        // Settings are saved when leaving the options menu; a failed save must not stop the game.
        void Save(GameSettings s)
        {
            try { s.Save(); }
            catch (Exception e) when (e is IOException or UnauthorizedAccessException) { Console.Error.WriteLine($"Could not save {GameSettings.FileName}: {e.Message}"); }
        }
        factory = () => new GameFlow(new AssetStageProvider(assets), new AssetScreenImages(assets), font, settings, Save, stage, route);
        Console.WriteLine(stage is int st ? $"Starting stage {st + 1}, route {(route == 0 ? "Makai" : "Jigoku")}." : "Starting at the logo and title screen.");
    }
}

try
{
    using var game = new Th01Game(factory);
    game.Run();
    if (game.Fatal is not null) return ReportCrash(game.Fatal);
}
catch (Exception e)
{
    return ReportCrash(e);
}
return 0;

int ReportCrash(Exception e)
{
    string log = Path.Combine(userDir, "crash.local.log");
    string text = $"{e.GetType().Name}: {e.Message}{Environment.NewLine}{e}";
    try { Directory.CreateDirectory(userDir); File.WriteAllText(log, text); } catch (IOException) { /* best effort */ } catch (UnauthorizedAccessException) { }
    string hint = e is Microsoft.Xna.Framework.Graphics.NoSuitableGraphicsDeviceException
        ? "\nNo suitable OpenGL device was found. Update the graphics driver; OpenGL 3.0+ is required."
        : "";
    Console.Error.WriteLine($"Th01 stopped because of an error: {e.Message}{hint}");
    prompt.ShowError($"The game stopped because of an error:\n{e.Message}{hint}\n\nDetails were written to:\n{log}");
    return 1;
}
