using Th01.Core.Game;
using Th01.Core.Graphics;
using Th01.Core.Input;
using Th01.Platform.MonoGame;

namespace Th01.Platform.Tests;

public class GameFlowTests
{
    sealed class FakeStages : IStageProvider
    {
        public readonly List<(int Stage, int Route, GameOptions Options)> Requests = new();
        public int Lives = -1;
        public uint Score;

        public GameStart Create(int stageId, int route, Func<StageSetup, GameOptions> makeOptions)
        {
            var cards = new bool[StageSetup.Rows, StageSetup.Cols];
            cards[9, 19] = true;
            var setup = new StageSetup { StageId = stageId, Route = route, Cards = cards, Obstacles = new byte[StageSetup.Rows, StageSetup.Cols] };
            var o = makeOptions(setup);
            if (Lives >= 0 && Requests.Count == 0) o = o with { Lives = Lives, Score = Score };
            Requests.Add((stageId, route, o));
            return new GameStart(new GameSession(o), Th01.Core.Graphics.NullGameArt.Instance);
        }
    }

    static readonly IFontSource Font = BdfFont.LoadSpleen8x16();

    static GameFlow NewFlow(FakeStages stages, GameSettings? settings = null, Action<GameSettings>? save = null, int? startStage = null) =>
        new(stages, new NoScreenImages(), Font, settings ?? new GameSettings(), save, startStage);

    static void Run(GameFlow f, int ticks, GameButtons b = GameButtons.None) { for (int i = 0; i < ticks; i++) f.Tick(b); }
    static void Press(GameFlow f, GameButtons b) { f.Tick(b); f.Tick(GameButtons.None); }

    static GameFlow AtTitle(FakeStages stages, GameSettings? settings = null, Action<GameSettings>? save = null)
    {
        var f = NewFlow(stages, settings, save);
        Run(f, GameFlow.LogoTicks);
        Assert.Equal(Screen.Title, f.Screen);
        return f;
    }

    /// <summary>Plays through the entrance banner and the first shot, then puts the Orb on Reimu so she is hit.</summary>
    static void Die(GameFlow f)
    {
        Run(f, EntranceOverlay.TotalTicks(true) + 5);
        f.Tick(GameButtons.Shot); f.Tick(GameButtons.None);
        var w = f.Game!.World;
        w.Orb.Left = w.Player.Left; w.Orb.Top = Playfield.PlayerTop - 8;
        w.Orb.Y.Force = 0; w.Orb.ForceFrame = 0;
        f.Tick(GameButtons.None);
    }

    [Fact]
    public void Warning_ComesFirst_CanBeSkippedOnlyAfterAShortWhile_AndEndsOnItsOwn()
    {
        GameFlow New() => new(new FakeStages(), new NoScreenImages(), Font, new GameSettings(), showWarning: true);
        var f = New();
        Assert.Equal(Screen.Warning, f.Screen);
        Press(f, GameButtons.Ok);
        Assert.Equal(Screen.Warning, f.Screen); // too early to skip
        Run(f, GameFlow.WarningMinTicks);
        Press(f, GameButtons.Ok);
        Assert.Equal(Screen.Logo, f.Screen);

        var g = New();
        Run(g, GameFlow.WarningTicks + 1);
        Assert.Equal(Screen.Logo, g.Screen);
        g.Render(new Framebuffer());
        Assert.Equal(Screen.Warning, New().Screen);
    }

    [Fact]
    public void Warning_IsNotShown_WhenAStageOrCutsceneIsRequested()
    {
        var f = new GameFlow(new FakeStages(), new NoScreenImages(), Font, new GameSettings(), startStage: 0, showWarning: true);
        Assert.Equal(Screen.InGame, f.Screen);
    }

    [Fact]
    public void Logo_ThenTitle_AfterTimeout_OrOnPress()
    {
        var f = NewFlow(new FakeStages());
        Assert.Equal(Screen.Logo, f.Screen);
        Run(f, GameFlow.LogoTicks - 2);
        Assert.Equal(Screen.Logo, f.Screen);
        Run(f, 3);
        Assert.Equal(Screen.Title, f.Screen);

        var g = NewFlow(new FakeStages());
        Press(g, GameButtons.Ok);
        Assert.Equal(Screen.Title, g.Screen);
    }

    [Fact]
    public void Start_BuildsStageOneWithSettingsAndFrameRandFromTheTickCount()
    {
        var stages = new FakeStages();
        var s = new GameSettings { Rank = Rank.Hard, LivesExtra = 3, Bombs = 2 };
        var f = AtTitle(stages, s);
        long before = f.Ticks;
        Press(f, GameButtons.Ok);
        Assert.Equal(Screen.InGame, f.Screen);
        var (stage, route, o) = Assert.Single(stages.Requests);
        Assert.Equal((0, 0), (stage, route));
        Assert.Equal((Rank.Hard, 5, 2), (o.Rank, o.Lives, o.Bombs));
        Assert.Equal((uint)(before + 1), o.FrameRand);
        Assert.True(o.Entrance);
    }

    [Fact]
    public void Options_ChangeAndSaveWhenLeaving()
    {
        var saved = new List<GameSettings>();
        var s = new GameSettings();
        var f = AtTitle(new FakeStages(), s, saved.Add);
        Press(f, GameButtons.Down); Press(f, GameButtons.Down); // OPTION
        Press(f, GameButtons.Ok);
        Press(f, GameButtons.Right);                              // RANK: Normal -> Hard
        Assert.Equal(Rank.Hard, s.Rank);
        Press(f, GameButtons.Down); Press(f, GameButtons.Right); // MUSIC off
        Assert.False(s.Music);
        Press(f, GameButtons.Down); Press(f, GameButtons.Left);  // PLAYER 2 -> 1 extra
        Assert.Equal(1, s.LivesExtra);
        Assert.Empty(saved);
        Press(f, GameButtons.Cancel);
        Assert.Single(saved);
        Assert.Equal(Screen.Title, f.Screen);
    }

    [Fact]
    public void Continue_IsDisabledUntilAGameEnds_QuitRequestsExit()
    {
        var stages = new FakeStages();
        var f = AtTitle(stages);
        Press(f, GameButtons.Down); // CONTINUE
        Press(f, GameButtons.Ok);
        Assert.Equal(Screen.Title, f.Screen);
        Assert.Empty(stages.Requests);
        Press(f, GameButtons.Down); Press(f, GameButtons.Down); // QUIT
        Assert.False(f.QuitRequested);
        Press(f, GameButtons.Ok);
        Assert.True(f.QuitRequested);
    }

    [Fact]
    public void ClearingAStage_ShowsTheBonus_ThenLoadsTheNextWithCarryOver()
    {
        var stages = new FakeStages();
        var f = NewFlow(stages, startStage: 0);
        Assert.Equal(Screen.InGame, f.Screen);
        Run(f, EntranceOverlay.TotalTicks(true) + 5);
        f.Game!.World.StageCleared = true;
        f.Tick(GameButtons.Shot); // Shot held: starts the stage, then the clear is processed
        for (int i = 0; i < 5 && f.Game!.Phase != SessionPhase.StageBonus; i++) f.Tick(GameButtons.None);
        for (int i = 0; i < GameSession.BonusBoxTicks; i++) f.Tick(GameButtons.None);
        Assert.Equal(SessionPhase.StageBonus, f.Game!.Phase);
        f.Render(new Framebuffer()); // bonus panel must draw
        f.Tick(GameButtons.Shot); f.Tick(GameButtons.None); // release edge dismisses it
        Assert.Equal(2, stages.Requests.Count);
        var (stage, _, o) = stages.Requests[1];
        Assert.Equal(1, stage);
        Assert.NotNull(o.CarryOver);
        Assert.Equal(Screen.InGame, f.Screen);
    }

    [Fact]
    public void GameOver_Continue_RestartsTheSameStageWithContinueOptions()
    {
        var stages = new FakeStages { Lives = 0, Score = 12345 };
        var f = NewFlow(stages, new GameSettings { LivesExtra = 2 }, startStage: 2);
        Die(f);
        Assert.Equal(Screen.GameOver, f.Screen);
        f.Render(new Framebuffer());
        Press(f, GameButtons.Ok); // CONTINUE
        Assert.Equal(Screen.InGame, f.Screen);
        var (stage, _, o) = stages.Requests[1];
        Assert.Equal(2, stage);
        Assert.Equal((0u, 4), (o.Score, o.Lives)); // score reset, credit lives + 2
    }

    [Fact]
    public void GameOver_End_EntersAName_ThenRanking_ThenTitle()
    {
        var stages = new FakeStages { Lives = 0, Score = 12345 };
        var settings = new GameSettings { Rank = Rank.Easy };
        var f = NewFlow(stages, settings, startStage: 6);
        Die(f);
        Press(f, GameButtons.Down); Press(f, GameButtons.Ok); // END
        Assert.Equal(Screen.NameEntry, f.Screen);
        f.Render(new Framebuffer());
        Press(f, GameButtons.Up);   // ' ' -> 'A'... starts at 'A' already (index 1); Up gives 'B'
        Press(f, GameButtons.Ok);   // put 'B', cursor -> 1
        Press(f, GameButtons.Cancel); // done
        Assert.Equal(Screen.Ranking, f.Screen);
        var e = f.TableFor(Rank.Easy).Entries[0];
        Assert.Equal(12345, e.Score);
        Assert.Equal(7, e.Stage);
        Assert.Equal(ScoreRoute.Makai, e.Route);
        Assert.Equal('B', GameFlow.DecodeNameCell(e.Name[0]));
        Assert.Equal(' ', GameFlow.DecodeNameCell(e.Name[1]));
        Assert.Equal(1000, f.TableFor(Rank.Hard).Entries[0].Score); // tables are per rank: Hard untouched
        f.Render(new Framebuffer());
        Press(f, GameButtons.Ok);
        Assert.Equal(Screen.Title, f.Screen);
    }

    [Fact]
    public void GameOver_WithAScoreThatMissesTheTable_GoesStraightToRanking()
    {
        var stages = new FakeStages { Lives = 0, Score = 5 };
        var f = NewFlow(stages, startStage: 0);
        // fill the Normal table with higher scores
        var t = f.TableFor(Rank.Normal);
        for (int i = 0; i < HiScoreTable.Places; i++) t.Entries[i].Score = 1000;
        Die(f);
        Press(f, GameButtons.Down); Press(f, GameButtons.Ok);
        Assert.Equal(Screen.Ranking, f.Screen);
    }

    [Fact]
    public void MissAnimation_ShakesTheScreen_AndEveryScreenRenders()
    {
        var stages = new FakeStages { Lives = 3 };
        var f = NewFlow(stages, startStage: 0);
        Die(f);
        Assert.Equal(SessionPhase.MissAnimation, f.Game!.Phase);
        var seen = new HashSet<int>();
        var fb = new Framebuffer();
        for (int i = 0; i < GameSession.MissTotalTicks + 5; i++)
        {
            f.Tick(GameButtons.None);
            f.Render(fb);
            seen.Add(fb.ScrollY);
        }
        Assert.Contains(0, seen);
        Assert.Contains(392, seen);
        Assert.Equal(2, seen.Count);

        var title = AtTitle(new FakeStages());
        title.Render(fb);
        Press(title, GameButtons.Down); Press(title, GameButtons.Down); Press(title, GameButtons.Ok);
        title.Render(fb); // options
        NewFlow(new FakeStages()).Render(fb); // logo
    }

    [Fact]
    public void ScreenStart_FadesInFromBlack()
    {
        var f = NewFlow(new FakeStages());
        var fb = new Framebuffer();
        f.Render(fb); // first frame of the logo
        fb.GetPalette4(15, out int r0, out _, out _);
        Run(f, GameFlow.FadeTicks);
        f.Render(fb);
        fb.GetPalette4(15, out int r1, out _, out _);
        Assert.Equal(0, r0);
        Assert.Equal(15, r1);
    }

    [Fact]
    public void SameInputs_SameFlow()
    {
        string Trace()
        {
            var stages = new FakeStages();
            var f = AtTitle(stages);
            Press(f, GameButtons.Ok);
            Run(f, 120);
            for (int i = 0; i < 200; i++) f.Tick(i % 9 == 0 ? GameButtons.Shot | GameButtons.Left : GameButtons.Right);
            return $"{f.Screen}:{f.Game!.StateHash()}:{stages.Requests[0].Options.FrameRand}";
        }
        Assert.Equal(Trace(), Trace());
    }

    [Fact]
    public void Settings_RoundTripAndClamp()
    {
        string path = Path.Combine(Path.GetTempPath(), "th01-settings-" + Guid.NewGuid().ToString("N") + ".json");
        try
        {
            new GameSettings { Rank = Rank.Lunatic, LivesExtra = 4, Bombs = 3, Music = false }.Save(path);
            var l = GameSettings.Load(path);
            Assert.Equal((Rank.Lunatic, 4, 3, false), (l.Rank, l.LivesExtra, l.Bombs, l.Music));
            File.WriteAllText(path, "{\"rank\":99,\"livesExtra\":-5,\"bombs\":1,\"music\":true}");
            l = GameSettings.Load(path);
            Assert.Equal((Rank.Lunatic, 0), (l.Rank, l.LivesExtra));
            File.WriteAllText(path, "garbage");
            Assert.Equal(Rank.Normal, GameSettings.Load(path).Rank);
        }
        finally { File.Delete(path); }
    }
}

public class GameFlowRealDataTests
{
    [SkippableFact]
    public void RealData_TitleImagesLoad_AndTheWholeFlowRenders()
    {
        string? path = Th01.Assets.Config.GameDataConfig.Resolve();
        Skip.If(path is null || !Directory.Exists(path), "Game data not configured.");
        var assets = new Th01.Assets.FolderAssetSource(path!);
        var images = new AssetScreenImages(assets);
        Assert.True(images.TryLoad("REIIDEN2.GRP", out var a));
        Assert.True(images.TryLoad("reiiden3.grp", out var b));
        Assert.Equal(48, a.Palette.Length);
        Assert.Equal(a.Width * a.Height, a.Pixels.Length);
        Assert.False(images.TryLoad("NOPE.GRP", out _));
        // dimensions are reported by the test output below if they are not full screen
        Assert.Equal((640, 400), (a.Width, a.Height));
        Assert.Equal((640, 400), (b.Width, b.Height));

        var f = new GameFlow(new AssetStageProvider(assets), images, BdfFont.LoadSpleen8x16(), new GameSettings());
        var fb = new Framebuffer();
        for (int i = 0; i < GameFlow.LogoTicks + 30; i++) { f.Tick(GameButtons.None); f.Render(fb); }
        Assert.Equal(Screen.Title, f.Screen);
        f.Tick(GameButtons.Ok); f.Tick(GameButtons.None);
        Assert.Equal(Screen.InGame, f.Screen);
        for (int i = 0; i < 200; i++) { f.Tick(i % 11 == 0 ? GameButtons.Shot : GameButtons.Left); f.Render(fb); }
    }
}
