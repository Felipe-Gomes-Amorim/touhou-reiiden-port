using Th01.Assets;
using Th01.Assets.Config;
using Th01.Platform.MonoGame;
using Th01.Platform.MonoGame.Launcher;

// Usage: ReiidenPort [--stage N] [--route 0|1] [--select] [--game <file|folder>] [--data <folder>]
//   no arguments: the first run asks for your .hdi (remembered afterwards), then logo -> title/menu.
//   --select: ask for the game file again (holding Shift at startup does the same on Windows).
//   --game: use this .hdi/folder instead of asking (remembered). --data: use this folder as is (not remembered).
//   --cutscene 1|2|3 plays ending pictures (1 = ED1A..E, 2/3 = boss slides Makai/Jigoku); 4/5 = good ending Makai/Jigoku,
//   6/7 = bad ending Makai/Jigoku (with the verdict screen). Then it goes to the title.
//   --log-missing-art prints every art lookup that fails, as it happens (F9 / closing the game write missing-art.local.txt).
//   --stage N (1..20) starts that stage directly, skipping the title; route 0 = Makai, 1 = Jigoku.
int? stage = null, cutscene = null;
bool logMissingArt = false;
int route = 0;
string? dataArg = null, gameArg = null;
bool forceSelect = UserPrompts.ShiftHeld();
for (int i = 0; i < args.Length; i++)
{
    if (args[i] == "--stage" && i + 1 < args.Length && int.TryParse(args[++i], out int s) && s is >= 1 and <= 20) stage = s - 1;
    else if (args[i] == "--cutscene" && i + 1 < args.Length && int.TryParse(args[++i], out int cs) && cs is >= 1 and <= 7) cutscene = cs;
    else if (args[i] == "--route" && i + 1 < args.Length && int.TryParse(args[++i], out int r) && r is 0 or 1) route = r;
    else if (args[i] == "--data" && i + 1 < args.Length) dataArg = args[++i];
    else if (args[i] == "--game" && i + 1 < args.Length) gameArg = args[++i];
    else if (args[i] == "--log-missing-art") logMissingArt = true;
    else if (args[i] == "--select") forceSelect = true;
    else { Console.Error.WriteLine($"Ignoring argument '{args[i]}'. Usage: [--stage 1..20] [--route 0|1] [--select] [--game <file>] [--data <folder>]"); }
}

// REIIDENPORT_HOME overrides the data folder (portable use, tests).
string userDir = Environment.GetEnvironmentVariable("REIIDENPORT_HOME") is { Length: > 0 } home
    ? home
    : Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "ReiidenPort");
var prompt = UserPrompts.Create();
// The launcher comes first unless a stage or cutscene was asked for on the command line. It picks the game itself (nothing is
// asked at startup); without a launcher the old behaviour stays: resolve the game now, asking if needed.
bool useLauncher = stage is null && cutscene is null;
LauncherGameHost? host = null;
string? fixedData = null;
if (useLauncher)
{
    host = new LauncherGameHost(userDir, prompt);
    host.Startup(dataArg, gameArg);
}
else
{
    fixedData = new GameLocator(userDir, prompt).Resolve(dataArg, gameArg, forceSelect);
    if (fixedData is null) return 1; // cancelled or invalid; the user has already been told why
}
// Settings, key bindings and input scripts are written to the current directory: keep them in the user's data folder.
if (dataArg is null && GameDataConfig.Resolve() is null)
{
    try { Directory.CreateDirectory(userDir); Directory.SetCurrentDirectory(userDir); }
    catch (Exception e) when (e is IOException or UnauthorizedAccessException) { /* keep the current directory */ }
}
string? CurrentData() => host?.DataPath ?? fixedData;
Console.WriteLine(CurrentData() is { } d0 ? $"Game data folder: {d0}" : "No game chosen yet: the launcher will ask.");
Console.WriteLine("Keys: F11 fullscreen | F10 scale mode | F5 pause | F6 step one tick | F3 debug title | F7 record input | F8 replay | F12 screenshot | F9 missing-art list | Esc quit");

var missingArt = new MissingArtLog { Realtime = logMissingArt };
var fonts = new LauncherFonts(Th01.Assets.Text.JapaneseFont.CreateDefault());
Th01.Core.Graphics.IFontSource font = fonts;
// Music and effects are rendered from the game's own .MDT/.MDE files at run time. If one is missing, that sound is just silent.
// The folder is looked up on every request, so a game chosen in the launcher is used at once.
var sound = new Th01.Audio.Playback.MdrvSoundSink(name =>
    CurrentData() is { } dir && File.Exists(Path.Combine(dir, name)) ? File.ReadAllBytes(Path.Combine(dir, name)) : null);
var audioOut = new MonoGameAudioOutput(sound.Mixer);
// The launcher's settings, read when a game starts (visual options, warning...). Filled in below once the model exists.
LauncherSettings? live = null;
Func<GameFlow?> factory = () =>
{
    var chosen = live ?? new LauncherSettings();
    if (CurrentData() is not { } dataPath) return null;
    if (!Directory.Exists(dataPath))
    {
        Console.Error.WriteLine($"The configured game data folder does not exist: {dataPath}. Showing the test pattern only.");
        return null;
    }
    var assets = new FolderAssetSource(dataPath);
    var settings = GameSettings.Load();
    // Settings are saved when leaving the options menu; a failed save must not stop the game.
    void Save(GameSettings s)
    {
        try { s.Save(); }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException) { Console.Error.WriteLine($"Could not save {GameSettings.FileName}: {e.Message}"); }
    }
    // High scores live in the user data folder (a copy of REYHI*.DAT); the game folder is only read.
    var scores = new FileScoreStore(userDir, dataPath);
    Console.WriteLine(stage is int st ? $"Starting stage {st + 1}, route {(route == 0 ? "Makai" : "Jigoku")}." : "Starting at the logo and title screen.");
    return new GameFlow(new AssetStageProvider(assets, font, missingArt, sound, chosen.ToVisualOptions()), new AssetScreenImages(assets), font, settings, Save, stage, route, scores, cutscene, sound, showWarning: stage is null && cutscene is null && chosen.PhotosensitivityWarning, Th01.Assets.Text.TitleTextLoader.Load(assets),
        screenShake: !chosen.ReduceShake, titleLines: !chosen.RemoveTitleLines);
};

try
{
    string settingsPath = Path.Combine(userDir, LauncherSettings.FileName);
    live = LauncherSettings.Load(settingsPath);
    var launcher = new LauncherModel(live, host?.Current ?? new LauncherGameInfo(fixedData, fixedData is not null), LauncherModel.AssemblyVersion(),
        ls => { try { ls.Save(settingsPath); } catch (Exception e) when (e is IOException or UnauthorizedAccessException) { Console.Error.WriteLine($"Could not save {LauncherSettings.FileName}: {e.Message}"); } });
    var services = new LauncherServices(userDir, missingArt, prompt, () => fonts.State);
    using var game = new Th01Game(factory) { Launcher = launcher, ShowLauncher = useLauncher, LauncherFont = fonts, Fonts = fonts, GameFolder = CurrentData, GameChooser = host, OpenChooserAtStart = forceSelect && host is not null, Mixer = sound.Mixer, LauncherSvc = services, AudioOutput = audioOut, ScreenshotDirectory = Path.Combine(userDir, "screenshots"), MissingArt = missingArt, MissingArtPath = Path.Combine(userDir, MissingArtLog.FileName) };
    game.Run();
    audioOut.Dispose();
    sound.Dispose();
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
