using Th01.Audio.Playback;
using Th01.Core.Audio;
using Th01.Core.Game;
using Th01.Core.Graphics;
using Th01.Core.Input;
using Th01.Core.Sim;

namespace Th01.Platform.MonoGame;

public enum Screen { Logo, Title, InGame, GameOver, NameEntry, Ranking, Cutscene, Warning }

/// <summary>Loads a full-screen picture by file name (e.g. "REIIDEN2.GRP"). Returns false if it is not available.</summary>
public interface IScreenImages
{
    bool TryLoad(string fileName, out ArtFrame image);
}

public sealed class NoScreenImages : IScreenImages
{
    public bool TryLoad(string fileName, out ArtFrame image) { image = default; return false; }
}

/// <summary>
/// The screen state machine: logo, title/menu, stages (with their bonus), game over / continue, name entry and ranking.
/// Lives in the platform, never in the Core. One <see cref="Tick"/> = one VSync; <see cref="Render"/> only draws.
/// Deterministic: the same input per tick gives the same flow (frame_rand is the tick count when the game starts,
/// as the original uses the title screen's frame counter).
/// </summary>
public sealed class GameFlow
{
    public const int LogoTicks = 170;
    /// <summary>Photosensitivity warning: cannot be skipped for the first part, then any button, and it ends by itself.</summary>
    public const int WarningMinTicks = 90, WarningTicks = 600;
    public const int LastStage = 19;
    /// <summary>Last 0-based stage playable on the Makai route (see IsPlayable for the others).</summary>
    public const int LastPlayableStage = 8;

    /// <summary>
    /// Whether a stage can be played yet: every card stage, the stage 5 boss, and the stage 10 boss on the Jigoku route (Mima).
    /// The other bosses are not ported, so a run ends before reaching them.
    /// </summary>
    public static bool IsPlayable(int stageId, int route) =>
        stageId % StageSetup.StagesPerScene != StageSetup.BossStage
        || stageId == StageSetup.BossStage
        || (stageId == 2 * StageSetup.StagesPerScene - 1 && route == 1);
    public const int FadeTicks = 15;
    const string NameChars = " ABCDEFGHIJKLMNOPQRSTUVWXYZ0123456789-.";
    static readonly string[] RankNames = ["EASY", "NORMAL", "HARD", "LUNATIC"];
    const GameButtons Dirs = GameButtons.Up | GameButtons.Down | GameButtons.Left | GameButtons.Right;

    readonly IStageProvider _stages;
    readonly IScreenImages _images;
    readonly IFontSource _font;
    readonly Action<GameSettings>? _saveSettings;
    readonly HiScoreTable?[] _tables = new HiScoreTable?[4];
    readonly IScoreStore _scores;
    readonly ISoundSink? _sound;
    Slideshow? _show;
    /// <summary>What to do when the running slideshow ends (default: back to the title). The ending goes on to the ranking.</summary>
    Action? _afterShow;
    /// <summary>Continues used in each scene (shrine, then the three later scenes) of the current run; the ending's verdict shows them.</summary>
    readonly int[] _continues = new int[4];
    string? _endNote;
    readonly ButtonRepeater _repeat = new(delayTicks: 14, intervalTicks: 4);
    readonly PaletteFader _fader = new();

    GameButtons _prevInput;
    long _tick;
    long _screenStart;

    // title
    bool _inOptions;
    int _menuSel;
    ArtFrame _titleBase, _titleTop;
    bool _hasTitle;

    // in game
    GameStart? _start;
    GameSession? _game;
    int _stageId;
    int _route;
    int _missTicks;
    GameOverResult? _continue;
    SessionCarryOver? _lastCarry;

    // game over / name entry / ranking
    int _gameOverSel;
    NameEntry? _entry;
    int _charIdx;
    int _entryPlace;
    uint _entryScore;
    int _entryStage;
    ScoreRoute _entryRoute;
    int _rankingHighlight = -1;
    HiScoreTable? _rankingTable;

    public GameSettings Settings { get; }
    public Screen Screen { get; private set; } = Screen.Logo;
    public GameSession? Game => _game;
    public bool QuitRequested { get; private set; }
    public long Ticks => _tick;

    /// <summary>True where Esc should close the window (logo, main title menu); elsewhere Esc is only "cancel".</summary>
    public bool EscapeQuits => Screen is Screen.Logo or Screen.Warning || (Screen == Screen.Title && !_inOptions);

    /// <param name="startStage">If set (0-based), skips logo and title and starts that stage directly.</param>
    public GameFlow(IStageProvider stages, IScreenImages images, IFontSource font, GameSettings settings,
        Action<GameSettings>? saveSettings = null, int? startStage = null, int route = 0,
        IScoreStore? scores = null, int? startCutscene = null, ISoundSink? sound = null, bool showWarning = false)
    {
        _sound = sound;
        _scores = scores ?? new MemoryScoreStore();
        _stages = stages;
        _images = images;
        _font = font;
        Settings = settings;
        _saveSettings = saveSettings;
        _route = route;
        if (startCutscene is int c)
        {
            // 1 = ED1A..E, 2/3 = boss slides Makai/Jigoku, 4/5 = whole good ending Makai/Jigoku, 6/7 = whole bad ending Makai/Jigoku (with the verdict).
            if (c >= 4) { _continues[1] = c >= 6 ? 3 : 0; StartEnding(c is 5 or 7 ? 1 : 0, 0); }
            else StartCutscene(c == 1 ? Cutscenes.EndingOne() : Cutscenes.BossSlides(c == 3 ? 1 : 0));
        }
        else if (startStage is int s) StartNewGame(s);
        else if (showWarning) GoTo(Screen.Warning);
    }

    public HiScoreTable TableFor(Rank rank) => _tables[(int)rank] ??= _scores.Load(rank);

    /// <summary>Plays a picture sequence, then returns to the title screen. Any button skips it.</summary>
    public void StartCutscene(IReadOnlyList<SlideStep> steps)
    {
        _show = new Slideshow(_images, steps, _font, _images as ICaptionSource);
        _afterShow = null;
        GoTo(Screen.Cutscene);
    }

    /// <summary>
    /// The ending of a cleared game: the pictures (good ending without continues, bad one with them), the boss slides, the verdict screen,
    /// then the name entry / ranking. <paramref name="route"/> 0 = Makai, 1 = Jigoku.
    /// </summary>
    public void StartEnding(int route, uint score)
    {
        var steps = new List<SlideStep>(Cutscenes.Ending(route, _continues.Sum()));
        if ((_images as IVerdictSource)?.Verdict is { } verdict)
        {
            uint highest = Math.Max(score, (uint)Math.Max(0, TableFor(Settings.Rank).Entries[0].Score));
            steps.AddRange(VerdictScreen.Steps(verdict, RankNames[(int)Settings.Rank], score, highest, _continues, route, _font));
        }
        StartCutscene(steps);
        _afterShow = () => FinishRun(score, route == 1 ? HiScoreTable.ClearedJigoku : HiScoreTable.ClearedMakai, ScoreRoute.Clear);
    }

    void EndCutscene()
    {
        var after = _afterShow;
        _afterShow = null;
        if (after is not null) after(); else GoTo(Screen.Title);
    }

    // ---------------------------------------------------------------- ticks

    public void Tick(GameButtons input)
    {
        _tick++;
        var pressed = input & ~_prevInput;
        var dirs = _repeat.Update(input) & Dirs;
        _prevInput = input;
        bool ok = (pressed & (GameButtons.Ok | GameButtons.Shot)) != 0;
        bool cancel = (pressed & (GameButtons.Cancel | GameButtons.Bomb)) != 0;

        switch (Screen)
        {
            case Screen.Warning:
                {
                    long shown = _tick - _screenStart;
                    if (shown >= WarningTicks || (shown >= WarningMinTicks && (ok || cancel))) GoTo(Screen.Logo);
                    break;
                }
            case Screen.Logo:
                if (_tick - _screenStart >= LogoTicks || ok || cancel) GoTo(Screen.Title);
                break;
            case Screen.Title: TickTitle(dirs, ok, cancel); break;
            case Screen.InGame: TickGame(input); break;
            case Screen.GameOver: TickGameOver(dirs, ok); break;
            case Screen.NameEntry: TickNameEntry(dirs, ok, cancel); break;
            case Screen.Ranking:
                if (ok || cancel) GoTo(Screen.Title);
                break;
            case Screen.Cutscene:
                _show!.Tick();
                if (_show.Finished || ok || cancel) EndCutscene();
                break;
        }
    }

    void GoTo(Screen s)
    {
        Screen = s;
        _screenStart = _tick;
        // Like the original: title and ending have their own track, a game over keeps the stage music playing,
        // everything else stops it. Stage music is (re)started by LoadStage and ignores a request for the track already playing.
        if (s == Screen.Title) { if (Settings.Music) _sound?.PlayBgm(BgmTracks.Title); }
        else if (s == Screen.Cutscene) { if (Settings.Music) _sound?.PlayBgm(BgmTracks.Ending); }
        else if (s != Screen.InGame && s != Screen.GameOver) _sound?.StopBgm();
        if (s == Screen.Title)
        {
            _inOptions = false;
            _menuSel = 0;
            _hasTitle = _images.TryLoad("REIIDEN2.GRP", out _titleBase);
            if (_hasTitle) _images.TryLoad("REIIDEN3.GRP", out _titleTop);
        }
    }

    // ---- title / menu. ref: th01/op_01.cpp (MAIN_CHOICES, OPTION_CHOICES)
    const int MainStart = 0, MainContinue = 1, MainOption = 2, MainQuit = 3;
    const int OptRank = 0, OptMusic = 1, OptPlayer = 2, OptQuit = 3;

    void TickTitle(GameButtons dirs, bool ok, bool cancel)
    {
        int count = _inOptions ? 4 : 4;
        if ((dirs & GameButtons.Up) != 0) _menuSel = (_menuSel + count - 1) % count;
        if ((dirs & GameButtons.Down) != 0) _menuSel = (_menuSel + 1) % count;

        if (_inOptions)
        {
            int step = (dirs & GameButtons.Right) != 0 ? 1 : (dirs & GameButtons.Left) != 0 ? -1 : 0;
            if (step != 0) ChangeOption(step);
            if (cancel || (ok && _menuSel == OptQuit))
            {
                _inOptions = false;
                _menuSel = MainOption;
                _saveSettings?.Invoke(Settings);
            }
            return;
        }
        if (cancel) { _menuSel = MainQuit; return; }
        if (!ok) return;
        switch (_menuSel)
        {
            case MainStart: StartNewGame(0); break;
            case MainContinue:
                if (_continue is not null) StartContinue();
                break;
            case MainOption: _inOptions = true; _menuSel = 0; break;
            case MainQuit: QuitRequested = true; break;
        }
    }

    void ChangeOption(int step)
    {
        switch (_menuSel)
        {
            case OptRank: Settings.Rank = (Rank)(((int)Settings.Rank + step + 4) % 4); break;
            case OptMusic:
                Settings.Music = !Settings.Music;
                if (Settings.Music) _sound?.PlayBgm(BgmTracks.Title); else _sound?.StopBgm();
                break;
            case OptPlayer:
                Settings.LivesExtra = (Settings.LivesExtra + step + GameSettings.LivesExtraMax + 1) % (GameSettings.LivesExtraMax + 1);
                break;
        }
    }

    // ---- stages
    void StartNewGame(int stageId)
    {
        Array.Clear(_continues);
        _continue = null;
        _lastCarry = null;
        _endNote = null;
        uint frameRand = (uint)_tick;
        LoadStage(stageId, setup => new GameOptions
        {
            Stage = setup, Rank = Settings.Rank, Lives = Settings.LivesExtra + 2, Bombs = Settings.Bombs,
            FrameRand = frameRand, Entrance = true,
        });
    }

    void StartContinue()
    {
        var r = _continue!;
        int stage = r.StageNum - 1;
        _continues[Math.Clamp(stage / StageSetup.StagesPerScene, 0, 3)]++;
        int route = r.Route == ScoreRoute.Jigoku ? 1 : 0;
        _route = route;
        _lastCarry = null;
        LoadStage(stage, setup => r.ContinueOptions(setup, Settings.LivesExtra, entrance: true), route);
    }

    void LoadStage(int stageId, Func<StageSetup, GameOptions> make, int? route = null)
    {
        _stageId = stageId;
        if (route is int rt) _route = rt;
        _start = _stages.Create(stageId, _route, make);
        _game = _start.Session as GameSession
            ?? throw new InvalidOperationException("The stage provider must return a GameSession.");
        _missTicks = 0;
        GoTo(Screen.InGame);
        // One track per scene (ST0..ST6.MDT); bosses have their own. The music only restarts when the track changes.
        if (Settings.Music) _sound?.PlayBgm(BgmTracks.ForStage(stageId, _route, SessionFactory.SceneId(stageId, _route)));
    }

    void TickGame(GameButtons input)
    {
        var g = _game!;
        g.Step(input);
        _missTicks = g.Phase == SessionPhase.MissAnimation ? _missTicks + 1 : 0;

        if (g.Outcome == SessionOutcome.Cleared)
        {
            var carry = g.CarryOver!;
            // The menu after the stage 5 boss decides the route of every later scene.
            if (_stageId == StageSetup.BossStage) _route = carry.RouteChosen;
            if (_stageId >= LastStage) { StartEnding(_route, carry.Score); return; }
            if (!IsPlayable(_stageId + 1, _route))
            {
                _endNote = "END OF THE STAGES PORTED SO FAR (THE NEXT BOSS IS NOT DONE YET)";
                FinishRun(carry.Score, _stageId + 2, _route == 1 ? ScoreRoute.Jigoku : ScoreRoute.Makai, cleared: false);
                return;
            }
            _lastCarry = carry;
            int next = _stageId + 1;
            LoadStage(next, setup => new GameOptions { Stage = setup, Rank = Settings.Rank, CarryOver = carry, Entrance = true });
        }
        else if (g.Outcome == SessionOutcome.GameOver)
        {
            _continue = g.GameOverResult;
            _gameOverSel = 0;
            GoTo(Screen.GameOver);
        }
    }

    // ---- game over: CONTINUE or END
    void TickGameOver(GameButtons dirs, bool ok)
    {
        if ((dirs & (GameButtons.Up | GameButtons.Down)) != 0) _gameOverSel ^= 1;
        if (!ok) return;
        if (_gameOverSel == 0) StartContinue();
        else
        {
            var r = _continue!;
            FinishRun(r.Score, r.RegistStage, r.Route, cleared: false);
        }
    }

    /// <summary>Ends a run: if the score makes the table, ask for a name; otherwise show the ranking.</summary>
    void FinishRun(uint score, int stage, ScoreRoute route, bool cleared = true)
    {
        var table = TableFor(Settings.Rank);
        _rankingTable = table;
        _entryScore = score;
        _entryStage = stage;
        _entryRoute = route;
        _entryPlace = table.FindPlace((int)Math.Min(score, int.MaxValue));
        if (_entryPlace >= HiScoreTable.Places)
        {
            _rankingHighlight = -1;
            GoTo(Screen.Ranking);
            return;
        }
        _entry = new NameEntry(cleared);
        _charIdx = 1;
        GoTo(Screen.NameEntry);
    }

    void TickNameEntry(GameButtons dirs, bool ok, bool cancel)
    {
        var e = _entry!;
        if ((dirs & GameButtons.Up) != 0) _charIdx = (_charIdx + 1) % NameChars.Length;
        if ((dirs & GameButtons.Down) != 0) _charIdx = (_charIdx + NameChars.Length - 1) % NameChars.Length;
        if ((dirs & GameButtons.Left) != 0) e.CursorLeft();
        if ((dirs & GameButtons.Right) != 0) e.CursorRight();
        if (ok)
        {
            char c = NameChars[_charIdx];
            if (e.Put(ScoreConvert.EncodeNameChar(c))) e.Confirm();
        }
        if (cancel) e.Confirm();
        if (e.Confirmed || e.TimedOut)
        {
            var table = _rankingTable!;
            table.Insert(_entryPlace, e.Name, (int)Math.Min(_entryScore, int.MaxValue), _entryStage, _entryRoute);
            _scores.Save(Settings.Rank, table);
            _rankingHighlight = _entryPlace;
            _entry = null;
            GoTo(Screen.Ranking);
        }
    }

    /// <summary>Name cell to text. TODO(Japanese font): the table stores Shift-JIS kanji codes; only letters, digits and a few signs are shown until a font is chosen.</summary>
    public static char DecodeNameCell(ushort code) => ScoreConvert.DecodeNameCell(code);

    // ---------------------------------------------------------------- drawing

    public void Render(Framebuffer fb)
    {
        fb.ScrollY = 0;
        switch (Screen)
        {
            case Screen.Warning: RenderWarning(fb); break;
            case Screen.Logo: RenderLogo(fb); break;
            case Screen.Title: RenderTitle(fb); break;
            case Screen.InGame: RenderGame(fb); break;
            case Screen.GameOver: RenderGame(fb); RenderGameOver(fb); break;
            case Screen.NameEntry: RenderNameEntry(fb); break;
            case Screen.Ranking: RenderRanking(fb); break;
            case Screen.Cutscene: _show!.Render(fb); break;
        }
        if (Screen != Screen.Cutscene) ApplyFadeIn(fb); // cutscenes carry their own tone
        if (Screen == Screen.InGame) fb.ScrollY = ShakeLine();
    }

    /// <summary>Palette fade from black at the start of each screen: replays the fader for the ticks elapsed, so it needs no state.</summary>
    void ApplyFadeIn(Framebuffer fb)
    {
        long elapsed = _tick - _screenStart;
        if (elapsed >= FadeTicks) return;
        _fader.FadeFrom(fb, 0, 0, 0);
        for (long i = 0; i < elapsed; i++) _fader.Step();
    }

    /// <summary>
    /// Screen shake of the miss animation: 16 iterations of 2 ticks, odd iterations scrolled to line 392 (RES_Y - 8), then 0.
    /// The session does not expose the shake, so it is derived from the miss phase length.
    /// ref: th01/main/player/player.cpp player_miss_animate_and_update()
    /// </summary>
    int ShakeLine()
    {
        if (_game is null || _game.Phase != SessionPhase.MissAnimation || _missTicks >= GameSession.MissShakeTicks) return 0;
        return (_missTicks / 2) % 2 == 1 ? Framebuffer.Height - 8 : 0;
    }

    void Grayscale(Framebuffer fb)
    {
        for (int i = 0; i < Framebuffer.ColorCount; i++) fb.SetPalette4(i, i, i, i);
    }

    (byte Dark, byte Dim, byte Light) Colors(Framebuffer fb) => (
        (byte)SceneCompositor.NearestColor(fb, 0, 0, 0),
        (byte)SceneCompositor.NearestColor(fb, 7, 7, 7),
        (byte)SceneCompositor.NearestColor(fb, 15, 15, 15));

    int Text(Framebuffer fb, string s, int x, int y, byte color) => TextRenderer.DrawString(fb, _font, s, x, y, color);

    void TextCentered(Framebuffer fb, string s, int y, byte color) =>
        Text(fb, s, (Framebuffer.Width - TextRenderer.Measure(_font, s)) / 2, y, color);

    void RenderWarning(Framebuffer fb)
    {
        Grayscale(fb);
        fb.Clear(0);
        TextCentered(fb, "PHOTOSENSITIVITY WARNING", 96, 15);
        string[] lines =
        [
            "This game contains flashing lights and intense visual",
            "effects that may trigger seizures in people with",
            "photosensitive epilepsy.",
            "",
            "If you feel dizzy, disoriented or unwell,",
            "stop playing and consult a doctor.",
        ];
        for (int i = 0; i < lines.Length; i++) TextCentered(fb, lines[i], 148 + i * 24, 12);
        if (_tick - _screenStart >= WarningMinTicks) TextCentered(fb, "PRESS Z OR SPACE TO CONTINUE", 332, 8);
    }

    void RenderLogo(Framebuffer fb)
    {
        Grayscale(fb);
        fb.Clear(0);
        TextCentered(fb, "TH01 - UNOFFICIAL FAN PORT", 168, 15);
        TextCentered(fb, "REQUIRES YOUR OWN COPY OF THE ORIGINAL GAME", 196, 8);
    }

    void RenderTitle(Framebuffer fb)
    {
        if (_hasTitle)
        {
            fb.Clear(0);
            fb.ApplyPalette(_titleBase);
            fb.Draw(_titleBase, 0, 0);
            fb.Draw(_titleTop, 0, 0);
        }
        else { Grayscale(fb); fb.Clear(0); TextCentered(fb, "TH01", 60, 15); }

        var (dark, dim, light) = Colors(fb);
        string[] main = ["START", _continue is null ? "CONTINUE (NO GAME)" : "CONTINUE", "OPTION", "QUIT"];
        string[] opts =
        [
            $"RANK    {RankNames[(int)Settings.Rank]}",
            $"MUSIC   {(Settings.Music ? "ON" : "OFF")}",
            $"PLAYER  {Settings.LivesExtra + 2} LIVES",
            "QUIT",
        ];
        var items = _inOptions ? opts : main;
        int w = 0;
        foreach (var s in items) w = Math.Max(w, TextRenderer.Measure(_font, s));
        int boxW = w + 48, boxX = (Framebuffer.Width - boxW) / 2, boxY = 280;
        fb.FillRect(boxX - 2, boxY - 2, boxW + 4, items.Length * 22 + 20, light);
        fb.FillRect(boxX, boxY, boxW, items.Length * 22 + 16, dark);
        for (int i = 0; i < items.Length; i++)
        {
            byte col = i == _menuSel ? light : dim;
            if (i == _menuSel) Text(fb, ">", boxX + 12, boxY + 8 + i * 22, light);
            Text(fb, items[i], boxX + 32, boxY + 8 + i * 22, col);
        }
        if (_inOptions) TextCentered(fb, "LEFT/RIGHT: CHANGE   CANCEL: BACK", boxY + items.Length * 22 + 28, dim);
    }

    void RenderGame(Framebuffer fb)
    {
        // The session draws everything itself: background and sprites from its art, the HUD and the bonus box.
        _game?.Render(fb);
    }

    void RenderGameOver(Framebuffer fb)
    {
        var r = _continue;
        string[] lines = ["GAME OVER", $"SCORE {r?.Score ?? 0}", "", _gameOverSel == 0 ? "> CONTINUE" : "  CONTINUE", _gameOverSel == 1 ? "> END" : "  END"];
        SceneCompositor.DrawMessage(fb, _font, lines);
    }

    void RenderNameEntry(Framebuffer fb)
    {
        Grayscale(fb);
        fb.Clear(0);
        TextCentered(fb, $"RANK {RankNames[(int)Settings.Rank]}   PLACE {_entryPlace + 1}", 90, 15);
        TextCentered(fb, $"SCORE {_entryScore}", 120, 15);
        TextCentered(fb, "ENTER YOUR NAME", 160, 8);
        var e = _entry!;
        int x0 = (Framebuffer.Width - HiScoreTable.NameKanji * 24) / 2;
        for (int i = 0; i < HiScoreTable.NameKanji; i++)
        {
            char c = i == e.Cursor ? NameChars[_charIdx] : DecodeNameCell(e.Name[i]);
            if (i == e.Cursor) fb.FillRect(x0 + i * 24 - 4, 196, 20, 24, 8);
            Text(fb, c.ToString(), x0 + i * 24, 200, 15);
        }
        TextCentered(fb, "UP/DOWN: LETTER   LEFT/RIGHT: MOVE   OK: PUT   CANCEL: DONE", 260, 8);
    }

    void RenderRanking(Framebuffer fb)
    {
        Grayscale(fb);
        fb.Clear(0);
        var table = _rankingTable ?? TableFor(Settings.Rank);
        TextCentered(fb, $"HIGH SCORES - {RankNames[(int)Settings.Rank]}", 40, 15);
        for (int i = 0; i < HiScoreTable.Places; i++)
        {
            var e = table.Entries[i];
            var name = new string(e.Name.Select(DecodeNameCell).ToArray());
            string stage = e.Stage >= HiScoreTable.Cleared ? "CLEAR" : e.Stage > 0 ? $"STAGE {e.Stage}" : "";
            string route = e.Route switch { ScoreRoute.Shrine => "SHRINE", ScoreRoute.Makai => "MAKAI", ScoreRoute.Jigoku => "JIGOKU", ScoreRoute.Clear => "", _ => "" };
            string line = $"{i + 1,2}  {name}  {e.Score,9}  {stage,-8} {route}";
            Text(fb, line, 100, 90 + i * 24, (byte)(i == _rankingHighlight ? 15 : 9));
        }
        if (_endNote is not null) TextCentered(fb, _endNote, 340, 8);
        TextCentered(fb, "OK: BACK TO TITLE", 364, 8);
    }
}
