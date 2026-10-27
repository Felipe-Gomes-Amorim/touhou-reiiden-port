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
    /// <summary>Last 0-based stage playable on either route: the stage 20 bosses (Konngara, Sariel) are not ported yet.</summary>
    public const int LastPlayableStage = 18;

    /// <summary>
    /// Whether a stage can be played yet: every card stage and the bosses that exist on the given route: stage 5 (SinGyoku),
    /// stage 10 (Mima on Jigoku, YuugenMagan on Makai) and stage 15 (Kikuri on Jigoku, Elis on Makai). The stage 20 bosses
    /// (Konngara, Sariel) are not ported, so a run ends before reaching them.
    /// </summary>
    public static bool IsPlayable(int stageId, int route) =>
        stageId % StageSetup.StagesPerScene != StageSetup.BossStage || stageId < LastStage;
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
    TitleScene? _title;
    readonly TitleTexts _titleTexts;

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

    /// <summary>True where Esc should close the window (logo, warning). On the title screen Esc is "cancel" and the menu itself quits.</summary>
    public bool EscapeQuits => Screen is Screen.Logo or Screen.Warning;

    /// <param name="startStage">If set (0-based), skips logo and title and starts that stage directly.</param>
    public GameFlow(IStageProvider stages, IScreenImages images, IFontSource font, GameSettings settings,
        Action<GameSettings>? saveSettings = null, int? startStage = null, int route = 0,
        IScoreStore? scores = null, int? startCutscene = null, ISoundSink? sound = null, bool showWarning = false, TitleTexts? titleTexts = null)
    {
        _titleTexts = titleTexts ?? TitleTexts.Default;
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
            case Screen.Title: TickTitle(input); break;
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
        if (s == Screen.Title) { /* the title scene starts its own music */ }
        else if (s == Screen.Cutscene) { if (Settings.Music) _sound?.PlayBgm(BgmTracks.Ending); }
        else if (s != Screen.InGame && s != Screen.GameOver) _sound?.StopBgm();
        if (s == Screen.Title) _title = new TitleScene(_images, _font, Settings, _titleTexts, _sound, _saveSettings);
    }

    // ---- title / menu: the screen itself is TitleScene (ref: th01/op_01.cpp); here we only act on what it chose
    void TickTitle(GameButtons input)
    {
        var t = _title!;
        t.Tick(input);
        switch (t.Action)
        {
            case TitleAction.Start: StartNewGame(0, t.FrameRand); break;
            case TitleAction.Continue:
                if (_continue is not null) StartContinue();
                else t.ClearAction();      // nothing to continue yet: stay in the menu
                break;
            case TitleAction.Quit: QuitRequested = true; break;
        }
    }

    // ---- stages
    void StartNewGame(int stageId, uint? titleFrameRand = null)
    {
        Array.Clear(_continues);
        _continue = null;
        _lastCarry = null;
        _endNote = null;
        uint frameRand = titleFrameRand ?? (uint)_tick;
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
        fb.ClearTextLayer(); // a stage banner abandoned half-way must not stay on top of another screen
        switch (Screen)
        {
            case Screen.Warning: RenderWarning(fb); break;
            case Screen.Logo: RenderLogo(fb); break;
            case Screen.Title: _title!.Render(fb); break;
            case Screen.InGame: RenderGame(fb); break;
            case Screen.GameOver: RenderGame(fb); RenderGameOver(fb); break;
            case Screen.NameEntry: RenderNameEntry(fb); break;
            case Screen.Ranking: RenderRanking(fb); break;
            case Screen.Cutscene: _show!.Render(fb); break;
        }
        if (Screen is not (Screen.Cutscene or Screen.Title)) ApplyFadeIn(fb); // cutscenes and the title carry their own fades
        if (Screen == Screen.InGame && ShakeLine() is var shake and not 0) fb.ScrollY = shake;   // the bomb sets its own shake while drawing
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
