namespace Th01.Platform.MonoGame.Launcher;

[Flags]
public enum LauncherKey
{
    None = 0, Up = 1, Down = 2, Left = 4, Right = 8, Select = 16, Back = 32, TabPrev = 64, TabNext = 128,
}

public enum LauncherScreen { Home, Options, About }

/// <summary>What the host should do after a key press.</summary>
public enum LauncherResult { None, Play, Quit, ChooseGame }

public enum GameVersion { Unknown, Japanese, Translated }

/// <summary>The game copy in use: where it came from, whether it is usable, which edition it is.</summary>
public sealed record LauncherGameInfo(string? Origin, bool Valid, GameVersion Version = GameVersion.Unknown)
{
    public static readonly LauncherGameInfo None = new(null, false);
}

/// <summary>Why the last choice of game was refused (shown in the game panel until a choice works).</summary>
public sealed record GameError(Th01.Assets.GameProblemCode Code, string? File);

public enum HomeItem { Play, Options, Directory, Quit, Change, Language, About }

public enum OptionsTab { Accessibility, Video, Audio, Font, Controls, Game, Data, Diagnostics }

/// <summary>
/// The launcher's screens and navigation, with no drawing and no window: arrows move, Select acts, Back returns, TabPrev/TabNext
/// change the options tab. One <see cref="Press"/> per key press (the host does the edge detection and key repeat).
/// <see cref="Step"/> advances the background animation by one tick.
/// </summary>
public sealed class LauncherModel
{
    public static readonly HomeItem[] HomeOrder = [HomeItem.Play, HomeItem.Options, HomeItem.Directory, HomeItem.Quit, HomeItem.Change, HomeItem.Language, HomeItem.About];

    readonly LauncherSettings _settings;
    readonly Action<LauncherSettings>? _save;

    public LauncherModel(LauncherSettings settings, LauncherGameInfo game, string version, Action<LauncherSettings>? save = null)
    {
        _settings = settings;
        Game = game;
        Version = version;
        _save = save;
        Focus = FirstEnabled();
    }

    public LauncherScreen Screen { get; private set; } = LauncherScreen.Home;
    public LauncherGameInfo Game { get; set; }
    public string Version { get; }
    public LauncherLanguage Language => _settings.Language;

    /// <summary>Background animation clock, in ticks (the dotted bands move one pixel per tick).</summary>
    public int Tick { get; private set; }

    /// <summary>Focused item of the home screen (an index into <see cref="HomeOrder"/>).</summary>
    public int Focus { get; private set; }
    public HomeItem FocusedItem => HomeOrder[Focus];

    public OptionsTab Tab { get; private set; }

    /// <summary>True while no valid game is chosen: the first-run layout, with PLAY switched off.</summary>
    public bool FirstRun => !Game.Valid;

    /// <summary>Set when the last choice of game was refused; cleared by a good choice.</summary>
    public GameError? Error { get; private set; }

    public bool IsEnabled(HomeItem item) => item != HomeItem.Play || Game.Valid;

    public void Step() => Tick++;

    public string Text(Str key) => LauncherStrings.Get(key, Language);

    public LauncherResult Press(LauncherKey key)
    {
        switch (Screen)
        {
            case LauncherScreen.Home: return PressHome(key);
            case LauncherScreen.Options:
                if (key == LauncherKey.Back) Screen = LauncherScreen.Home;
                else if (key == LauncherKey.TabPrev) Tab = (OptionsTab)(((int)Tab + 7) % 8);
                else if (key == LauncherKey.TabNext) Tab = (OptionsTab)(((int)Tab + 1) % 8);
                return LauncherResult.None;
            case LauncherScreen.About:
                if (key is LauncherKey.Back or LauncherKey.Select) Screen = LauncherScreen.Home;
                return LauncherResult.None;
        }
        return LauncherResult.None;
    }

    LauncherResult PressHome(LauncherKey key)
    {
        switch (key)
        {
            case LauncherKey.Up: MoveFocus(-1); break;
            case LauncherKey.Down: MoveFocus(1); break;
            case LauncherKey.Left or LauncherKey.Right when FocusedItem == HomeItem.Language: ToggleLanguage(); break;
            case LauncherKey.Select: return Activate(FocusedItem);
        }
        return LauncherResult.None;
    }

    LauncherResult Activate(HomeItem item)
    {
        switch (item)
        {
            case HomeItem.Play: return IsEnabled(item) ? LauncherResult.Play : LauncherResult.None;
            case HomeItem.Options: Screen = LauncherScreen.Options; break;
            case HomeItem.Directory or HomeItem.Change: return LauncherResult.ChooseGame;
            case HomeItem.Quit: return LauncherResult.Quit;
            case HomeItem.Language: ToggleLanguage(); break;
            case HomeItem.About: Screen = LauncherScreen.About; break;
        }
        return LauncherResult.None;
    }

    public void SetLanguage(LauncherLanguage language)
    {
        if (_settings.Language == language) return;
        _settings.Language = language;
        _save?.Invoke(_settings);
    }

    void ToggleLanguage() => SetLanguage(Language == LauncherLanguage.English ? LauncherLanguage.Japanese : LauncherLanguage.English);

    /// <summary>Moves the home focus, skipping items that are switched off, wrapping at the ends.</summary>
    void MoveFocus(int dir)
    {
        for (int i = 0; i < HomeOrder.Length; i++)
        {
            Focus = (Focus + dir + HomeOrder.Length) % HomeOrder.Length;
            if (IsEnabled(HomeOrder[Focus])) return;
        }
    }

    int FirstEnabled()
    {
        // First run starts on DIRECTORY (PLAY is off); otherwise on PLAY.
        if (!Game.Valid) return Array.IndexOf(HomeOrder, HomeItem.Directory);
        for (int i = 0; i < HomeOrder.Length; i++) if (IsEnabled(HomeOrder[i])) return i;
        return 0;
    }

    /// <summary>The game copy changed (chosen in the directory dialog): refresh, and move the focus off PLAY if it is now off.</summary>
    public void SetGame(LauncherGameInfo game)
    {
        bool wasValid = Game.Valid;
        Game = game;
        Error = null;
        if (game.Valid && !wasValid) Focus = FirstEnabled(); // the first good choice puts the cursor on PLAY
        else if (!IsEnabled(FocusedItem)) Focus = FirstEnabled();
    }

    /// <summary>Applies the outcome of the game chooser: a usable game replaces the current one, a refusal keeps it and shows why.</summary>
    public void ApplyChoice(GameChoice? choice)
    {
        if (choice is null) return;
        if (choice.Game is not null) SetGame(choice.Game);
        else Error = new GameError(choice.ErrorCode ?? Th01.Assets.GameProblemCode.NotFound, choice.ErrorFile);
    }

    /// <summary>Real version of this program (assembly informational version without the commit suffix).</summary>
    public static string AssemblyVersion()
    {
        var asm = typeof(LauncherModel).Assembly;
        string? v = asm.GetCustomAttributes(typeof(System.Reflection.AssemblyInformationalVersionAttribute), false)
            .OfType<System.Reflection.AssemblyInformationalVersionAttribute>().FirstOrDefault()?.InformationalVersion;
        if (v is null) return asm.GetName().Version?.ToString(3) ?? "0.0.0";
        int plus = v.IndexOf('+');
        return plus >= 0 ? v[..plus] : v;
    }
}

/// <summary>Turns "keys held this tick" into key presses: one on each new press, and repeats of the arrows while held.</summary>
public sealed class LauncherInputState
{
    public const int RepeatDelay = 24, RepeatEvery = 5;
    LauncherKey _prev;
    int _heldTicks;

    /// <summary>Feeds one tick of held keys; returns the presses to act on (possibly several different keys).</summary>
    public LauncherKey Step(LauncherKey held)
    {
        var pressed = held & ~_prev;
        var arrows = LauncherKey.Up | LauncherKey.Down | LauncherKey.Left | LauncherKey.Right;
        var heldArrows = held & arrows;
        if (heldArrows != LauncherKey.None && heldArrows == (_prev & arrows) && pressed == LauncherKey.None)
        {
            _heldTicks++;
            if (_heldTicks >= RepeatDelay && (_heldTicks - RepeatDelay) % RepeatEvery == 0) pressed |= heldArrows;
        }
        else _heldTicks = 0;
        _prev = held;
        return pressed;
    }
}
