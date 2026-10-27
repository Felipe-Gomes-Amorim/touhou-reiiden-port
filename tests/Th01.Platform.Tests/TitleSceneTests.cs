using Th01.Core.Audio;
using Th01.Core.Game;
using Th01.Core.Graphics;
using Th01.Core.Input;
using Th01.Platform.MonoGame;

namespace Th01.Platform.Tests;

public class TitleSceneTests
{
    sealed class FakeImages : IScreenImages
    {
        public bool TryLoad(string file, out ArtFrame image)
        {
            var px = new byte[640 * 400];
            var pal = new byte[48];
            for (int i = 0; i < 16; i++) { pal[i * 3] = (byte)i; pal[i * 3 + 1] = (byte)(15 - i); pal[i * 3 + 2] = 3; }
            switch (file)
            {
                case "REIIDEN2.GRP": Array.Fill(px, (byte)2); break;
                case "REIIDEN3.GRP": Array.Fill(px, (byte)3); break;
                case "OP_WIN.GRP":
                    Array.Fill(px, (byte)15);                       // all transparent...
                    for (int y = 250; y < 380; y++) for (int x = 200; x < 440; x++) px[y * 640 + x] = 8; // ...except a window
                    break;
                default: image = default; return false;
            }
            image = new ArtFrame(640, 400, px, Array.Empty<byte>(), pal);
            return true;
        }
    }

    sealed class RecordingSink : ISoundSink
    {
        public readonly List<string> Calls = new();
        public void PlayBgm(string trackFileName) => Calls.Add("play " + trackFileName);
        public void StopBgm() => Calls.Add("stop");
        public void FadeOutBgm() => Calls.Add("fade");
        public void PlaySe(int effectId) { }
    }

    static readonly IFontSource Font = BdfFont.LoadSpleen8x16();

    static TitleScene New(GameSettings? s = null, RecordingSink? sink = null, IScreenImages? images = null) =>
        new(images ?? new FakeImages(), Font, s ?? new GameSettings(), TitleTexts.Default, sink);

    static void Run(TitleScene t, int n, GameButtons b = GameButtons.None) { for (int i = 0; i < n; i++) t.Tick(b); }
    static void Press(TitleScene t, GameButtons b) { t.Tick(b); t.Tick(GameButtons.None); }

    /// <summary>Ticks before the prompt phase: 100 (fade + pause), 100 line steps, 1 restore tick (measured: lines are ticks 101-200, the prompt shows at 202).</summary>
    const int IntroTicks = TitleScene.FadeInTicks + TitleScene.PauseTicks + TitleScene.WhiteLineSteps + 1;

    static TitleScene AtMenu(GameSettings? s = null, RecordingSink? sink = null)
    {
        var t = New(s, sink);
        Run(t, IntroTicks);
        Assert.Equal(TitleScene.Phase.HitKey, t.CurrentPhase);
        Press(t, GameButtons.Shot);
        Assert.Equal(TitleScene.Phase.Menu, t.CurrentPhase);
        return t;
    }

    static int RowsOfColour(Framebuffer fb, byte colour)
    {
        int rows = 0;
        for (int y = 0; y < 400; y++)
        {
            bool all = true;
            for (int x = 0; x < 640 && all; x += 8) all = fb.GetPixel(x, y) == colour;
            if (all) rows++;
        }
        return rows;
    }

    [Fact]
    public void Intro_FollowsTheOriginalTimeline()
    {
        var sink = new RecordingSink();
        var t = New(sink: sink);
        Assert.Equal(new[] { "play REIMU.MDT" }, sink.Calls);
        var fb = new Framebuffer();
        t.Render(fb);
        fb.GetPalette4(15, out int r, out _, out _);
        Assert.Equal(0, r);                                               // black at the start
        Assert.Equal(2, fb.GetPixel(100, 100));                           // REIIDEN2 shown, REIIDEN3 not yet

        Run(t, TitleScene.FadeInTicks);
        Assert.Equal(TitleScene.Phase.Pause, t.CurrentPhase);
        t.Render(fb);
        fb.GetPalette4(15, out r, out _, out _);
        Assert.Equal(15, r);                                              // fade finished
        Run(t, TitleScene.PauseTicks);
        Assert.Equal(TitleScene.Phase.WhiteLines, t.CurrentPhase);
        Run(t, TitleScene.WhiteLineSteps);
        Assert.Equal(TitleScene.Phase.Restore, t.CurrentPhase);
        Run(t, 1);
        Assert.Equal(TitleScene.Phase.HitKey, t.CurrentPhase);
        t.Render(fb);
        Assert.Equal(3, fb.GetPixel(100, 100));                           // the second picture is now all there
    }

    [Fact]
    public void WhiteLines_TwoNewRowsPerStep_PreviousPairRestoredFromTheSecondPicture_NoRowTwice()
    {
        var t = New();
        Run(t, TitleScene.FadeInTicks + TitleScene.PauseTicks);
        var fb = new Framebuffer();
        var seenWhite = new HashSet<int>();
        for (int step = 1; step <= TitleScene.WhiteLineSteps - 1; step++)
        {
            t.Tick(GameButtons.None);
            t.Render(fb);
            Assert.Equal(2, RowsOfColour(fb, 15));                        // exactly this step's two lines are white
            for (int y = 0; y < 400; y++)
                if (fb.GetPixel(0, y) == 15) Assert.True(seenWhite.Add(y), $"row {y} drawn twice");
        }
        Assert.Equal(2 * (TitleScene.WhiteLineSteps - 1), seenWhite.Count);
    }

    [Fact]
    public void Measured_LinesAreTicks101To200_AndThePromptFirstShowsOnTick202_ThenBlinks50On20Off()
    {
        var t = New();
        var fb = new Framebuffer();
        var visible = new List<bool>();
        for (int tick = 1; tick <= 202 + 140; tick++)
        {
            t.Tick(GameButtons.None);
            if (tick is 100) { t.Render(fb); Assert.Equal(0, RowsOfColour(fb, 15)); }  // still waiting: no lines yet
            if (tick is 101 or 200)
            {
                t.Render(fb);
                Assert.Equal(2, RowsOfColour(fb, 15));                                // the lines are on screen on 101..200
            }
            if (tick == 201) { t.Render(fb); Assert.Equal(0, RowsOfColour(fb, 15)); } // back to the picture without lines
            visible.Add(t.HitKeyVisibleNow);
        }
        Assert.DoesNotContain(true, visible.Take(201));                              // nothing before tick 202
        Assert.True(visible[201]);                                                   // tick 202
        Assert.Equal(Enumerable.Repeat(true, 50), visible.Skip(201).Take(50));       // 202-251 visible
        Assert.Equal(Enumerable.Repeat(false, 20), visible.Skip(251).Take(20));      // 252-271 erased
        Assert.True(visible[271]);                                                   // 272: next cycle
        Assert.Equal(202, TitleScene.FirstHitKeyTick);
    }

    [Fact]
    public void WhiteLines_AreTheSameEveryRun()
    {
        string Run1()
        {
            var t = New();
            Run(t, TitleScene.FadeInTicks + TitleScene.PauseTicks + 40);
            var fb = new Framebuffer();
            t.Render(fb);
            return string.Join(",", Enumerable.Range(0, 400).Where(y => fb.GetPixel(0, y) == 15));
        }
        Assert.Equal(Run1(), Run1());
        Assert.False(string.IsNullOrEmpty(Run1()));
    }

    [Fact]
    public void HitKey_BlinksFiftyOnTwentyOff_AndAnyKeyOpensTheMenu()
    {
        var t = New();
        Run(t, IntroTicks);
        var fb = new Framebuffer();
        var visible = new List<bool>();
        for (int i = 0; i < TitleScene.HitKeyPeriod * 2; i++)
        {
            t.Tick(GameButtons.None);
            visible.Add(t.HitKeyVisibleNow);
        }
        // the frame counter is incremented after each put, so tick i drew frame i
        int onCount = visible.Take(TitleScene.HitKeyPeriod).Count(v => v);
        Assert.InRange(onCount, TitleScene.HitKeyVisible - 1, TitleScene.HitKeyVisible);
        t.Render(fb);
        Assert.Equal(TitleScene.Phase.HitKey, t.CurrentPhase);

        // drawn text puts colour 15 on the page at the prompt's row; erased again in the off part
        var t2 = New();
        Run(t2, IntroTicks + 1);
        t2.Render(fb);
        int inkOn = Enumerable.Range(200, 240).Sum(x => Enumerable.Range(306, 16).Count(y => fb.GetPixel(x, y) == 15));
        Assert.True(inkOn > 20);
        Run(t2, TitleScene.HitKeyVisible);                                // now in the off part of the period
        t2.Render(fb);
        int inkOff = Enumerable.Range(200, 240).Sum(x => Enumerable.Range(306, 16).Count(y => fb.GetPixel(x, y) == 15));
        Assert.Equal(0, inkOff);

        Press(t2, GameButtons.Down);
        Assert.Equal(TitleScene.Phase.Menu, t2.CurrentPhase);
    }

    [Fact]
    public void Window_IsDrawnWithColour15Transparent()
    {
        var t = AtMenu();
        var fb = new Framebuffer();
        t.Render(fb);
        Assert.Equal(8, fb.GetPixel(210, 260));       // inside the window
        Assert.Equal(3, fb.GetPixel(20, 20));         // transparent part: the title picture shows through
    }

    [Fact]
    public void MainMenu_RingsOncePerPress_AndStartsWithTheTitleFrameCounter()
    {
        var t = AtMenu();
        Assert.Equal(0, t.MenuSelection);
        Press(t, GameButtons.Up);
        Assert.Equal(3, t.MenuSelection);             // up from the first wraps to the last
        Run(t, 30, GameButtons.Down);                 // held: one step only
        Assert.Equal(0, t.MenuSelection);
        t.Tick(GameButtons.None);
        long before = t.FrameRand;
        Press(t, GameButtons.Ok);
        Assert.Equal(TitleAction.Start, t.Action);
        Assert.True(t.FrameRand >= before);
    }

    [Fact]
    public void MainMenu_QuitByChoiceOrCancel()
    {
        var a = AtMenu();
        Press(a, GameButtons.Cancel);
        Assert.Equal(TitleAction.Quit, a.Action);
        var b = AtMenu();
        Press(b, GameButtons.Up);
        Press(b, GameButtons.Ok);
        Assert.Equal(TitleAction.Quit, b.Action);
        var c = AtMenu();
        Press(c, GameButtons.Down);
        Press(c, GameButtons.Shot);
        Assert.Equal(TitleAction.Continue, c.Action);
    }

    [Fact]
    public void Options_ChangeValuesInARing_WaitFifteenTicksOnTheWayOut_AndSwallowKeysMeanwhile()
    {
        var saved = new List<GameSettings>();
        var s = new GameSettings { Rank = Rank.Lunatic, LivesExtra = 4 };
        var t = new TitleScene(new FakeImages(), Font, s, TitleTexts.Default, null, saved.Add);
        Run(t, IntroTicks);
        Press(t, GameButtons.Shot);
        Press(t, GameButtons.Down); Press(t, GameButtons.Down);
        Press(t, GameButtons.Ok);                                       // OPTION
        Assert.Equal(TitleScene.Menu.Option, t.CurrentMenu);
        Assert.Equal(0, t.MenuSelection);
        Press(t, GameButtons.Right);                                    // rank: lunatic wraps to easy
        Assert.Equal(Rank.Easy, s.Rank);
        Press(t, GameButtons.Left);
        Assert.Equal(Rank.Lunatic, s.Rank);
        Press(t, GameButtons.Down); Press(t, GameButtons.Right);        // music off
        Assert.False(s.Music);
        Press(t, GameButtons.Down); Press(t, GameButtons.Right);        // lives: 4 wraps to 0
        Assert.Equal(0, s.LivesExtra);
        Press(t, GameButtons.Up); Press(t, GameButtons.Up); Press(t, GameButtons.Up);   // wraps to QUIT
        Assert.Equal(4, t.MenuSelection);

        t.Tick(GameButtons.Ok);                                         // leave
        Assert.Single(saved);
        Assert.Equal(TitleScene.Menu.LeaveOptionDelay, t.CurrentMenu);
        t.Tick(GameButtons.None);                                       // the delay pass
        Assert.Equal(TitleScene.Menu.Main, t.CurrentMenu);
        for (int i = 0; i < TitleScene.MenuSwitchDelay; i++)
        {
            t.Tick(i % 2 == 1 ? GameButtons.Down : GameButtons.None);   // presses during the wait do nothing
            Assert.Equal(2, t.MenuSelection);                           // OPTION is selected again in the main menu
        }
        Press(t, GameButtons.Down);
        Assert.Equal(3, t.MenuSelection);
    }

    [Fact]
    public void Measured_NoKeyRepeat_Defaults_AndEscapeTargets()
    {
        var s = new GameSettings();
        Assert.Equal((Rank.Normal, true, 2), (s.Rank, s.Music, s.LivesExtra));       // NORMAL, FM (on), PLAYER shown as 5
        var t = AtMenu(s);
        Run(t, 60, GameButtons.Down);                                                // held 60 ticks: moves once
        Assert.Equal(1, t.MenuSelection);
        Press(t, GameButtons.Up);
        Press(t, GameButtons.Down); Press(t, GameButtons.Down); Press(t, GameButtons.Ok);   // OPTION
        Run(t, 120, GameButtons.Right);                                              // held right: rank changes once
        Assert.Equal(Rank.Hard, s.Rank);
        Press(t, GameButtons.Down); Press(t, GameButtons.Down);
        Press(t, GameButtons.Right);                                                 // PLAYER 5 -> 6
        Assert.Equal(3, s.LivesExtra);

        // Esc in the options: back to the main menu with OPTION active, after ~17 ticks with keys ignored
        t.Tick(GameButtons.Cancel);
        Run(t, 3, GameButtons.None);
        Assert.NotEqual(TitleScene.Menu.Option, t.CurrentMenu);
        Run(t, 20);
        Assert.Equal(TitleScene.Menu.Main, t.CurrentMenu);
        Assert.Equal(2, t.MenuSelection);

        // Esc in the music test: back to the options with RANK active
        Press(t, GameButtons.Ok);
        Press(t, GameButtons.Down); Press(t, GameButtons.Down); Press(t, GameButtons.Down); Press(t, GameButtons.Ok);
        Assert.Equal(TitleScene.Menu.Music, t.CurrentMenu);
        t.Tick(GameButtons.Cancel);
        Run(t, 25);
        Assert.Equal(TitleScene.Menu.Option, t.CurrentMenu);
        Assert.Equal(0, t.MenuSelection);

        // Esc in the main menu quits
        var q = AtMenu();
        Press(q, GameButtons.Cancel);
        Assert.Equal(TitleAction.Quit, q.Action);
    }

    [Fact]
    public void MusicMode_ChangeStopsOrRestartsTheTitleMusicWhenLeavingTheOptions()
    {
        var sink = new RecordingSink();
        var s = new GameSettings { Music = true };
        var t = AtMenu(s, sink);
        sink.Calls.Clear();
        Press(t, GameButtons.Down); Press(t, GameButtons.Down); Press(t, GameButtons.Ok);   // OPTION
        Press(t, GameButtons.Down); Press(t, GameButtons.Right);                            // music off
        Assert.Empty(sink.Calls);                                                           // nothing until we leave
        Press(t, GameButtons.Cancel);
        t.Tick(GameButtons.None);
        Assert.Equal(new[] { "stop" }, sink.Calls);
        Run(t, 20);
        Press(t, GameButtons.Ok);                                                           // back in OPTION
        Press(t, GameButtons.Down); Press(t, GameButtons.Left);                             // music on
        sink.Calls.Clear();
        Press(t, GameButtons.Cancel);
        t.Tick(GameButtons.None);
        Assert.Equal(new[] { "stop", "play REIMU.MDT" }, sink.Calls);
    }

    [Fact]
    public void MusicTest_ChangesTrackInARing_PlaysItOnOk_AndReturnsToTheOptionsOnItsEntry()
    {
        var sink = new RecordingSink();
        var t = AtMenu(sink: sink);
        Press(t, GameButtons.Down); Press(t, GameButtons.Down); Press(t, GameButtons.Ok);   // OPTION
        Press(t, GameButtons.Down); Press(t, GameButtons.Down); Press(t, GameButtons.Down); // M.TEST
        Assert.Equal(3, t.MenuSelection);
        Press(t, GameButtons.Ok);
        Assert.Equal(TitleScene.Menu.Music, t.CurrentMenu);
        Assert.Equal(0, t.MenuSelection);
        Assert.Equal(0, t.MusicSelection);
        Press(t, GameButtons.Left);
        Assert.Equal(14, t.MusicSelection);                                                 // 0 wraps to the last track
        Press(t, GameButtons.Right);
        Assert.Equal(0, t.MusicSelection);
        Press(t, GameButtons.Right); Press(t, GameButtons.Right);
        sink.Calls.Clear();
        Press(t, GameButtons.Shot);
        Assert.Equal(new[] { "stop", "play st0.mdt" }, sink.Calls);                         // track 2 = ST0
        Press(t, GameButtons.Down);                                                         // QUIT line
        t.Tick(GameButtons.Ok);
        Assert.Equal(TitleScene.Menu.LeaveMusicDelay, t.CurrentMenu);
        t.Tick(GameButtons.None);
        Assert.Equal(TitleScene.Menu.Option, t.CurrentMenu);
        Run(t, TitleScene.MenuSwitchDelay);
        t.Tick(GameButtons.None);
        // The original sets the selection to M.TEST on the way out but the option menu resets it to the first entry on entry.
        Assert.Equal(0, t.MenuSelection);
    }

    [Fact]
    public void Menu_DrawsTheActiveEntryInColour15AndTheOthersInColour5()
    {
        var t = AtMenu();
        var fb = new Framebuffer();
        t.Render(fb);
        int Ink(int row, byte colour)
        {
            int top = 316 - 40 + row * 20, n = 0;
            for (int x = 220; x < 400; x++) for (int y = top; y < top + 16; y++) if (fb.GetPixel(x, y) == colour) n++;
            return n;
        }
        Assert.True(Ink(0, 15) > 20 && Ink(0, 5) == 0);
        Assert.True(Ink(1, 5) > 20 && Ink(1, 15) == 0);
        Press(t, GameButtons.Down);
        t.Render(fb);
        Assert.True(Ink(1, 15) > 20 && Ink(0, 5) > 20);
    }

    [Fact]
    public void MissingPictures_StillRunTheWholeScene()
    {
        var t = New(images: new NoScreenImages());
        Run(t, IntroTicks);
        Press(t, GameButtons.Ok);
        Press(t, GameButtons.Ok);
        Assert.Equal(TitleAction.Start, t.Action);
        t.Render(new Framebuffer());
    }
}

public class TitleSceneRealDataTests
{
    [SkippableFact]
    public void RealData_TitleWithTheGamesOwnTextsPicturesAndFont()
    {
        string? path = Th01.Assets.Config.GameDataConfig.Resolve();
        Skip.If(path is null || !Directory.Exists(path), "Game data not configured.");
        var assets = new Th01.Assets.FolderAssetSource(path!);
        Skip.IfNot(assets.Exists("OP.EXE"), "OP.EXE is not in the game folder.");
        var texts = Th01.Assets.Text.TitleTextLoader.Load(assets);
        var font = Th01.Assets.Text.JapaneseFont.CreateDefault();

        // every character of the menu has a glyph in the bundled font (otherwise it would be skipped)
        var missing = new List<string>();
        foreach (string s in texts.MainChoices.Concat(texts.OptionChoices).Concat(texts.MusicChoices).Concat(texts.MusicTitles).Concat(texts.Ranks).Concat(texts.BgmModes).Append(texts.HitKey))
            foreach (var r in s.EnumerateRunes())
                if (!font.TryGetGlyph(r.Value, out _)) missing.Add($"U+{r.Value:X4} in \"{s.Trim()}\"");
        Assert.Empty(missing);

        var t = new TitleScene(new AssetScreenImages(assets), font, new GameSettings(), texts);
        for (int i = 0; i < TitleScene.FadeInTicks + TitleScene.PauseTicks + TitleScene.WhiteLineSteps + 11; i++) t.Tick(GameButtons.None);
        var fb = new Framebuffer();
        t.Render(fb);
        int hitKeyInk = 0;
        for (int x = 0; x < 640; x++) for (int y = 306; y < 322; y++) if (fb.GetPixel(x, y) == 15) hitKeyInk++;
        Assert.True(hitKeyInk > 50);
        t.Tick(GameButtons.Shot); t.Tick(GameButtons.None);
        t.Render(fb);
        int active = 0, inactive = 0;
        for (int x = 220; x < 420; x++) for (int y = 276; y < 356; y++) { byte c = fb.GetPixel(x, y); if (c == 15) active++; else if (c == 5) inactive++; }
        Assert.True(active > 50 && inactive > 100, $"active {active}, inactive {inactive}");
    }
}
