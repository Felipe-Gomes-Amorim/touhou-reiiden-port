using Th01.Assets.Formats;
using Th01.Core.Game;
using Th01.Core.Graphics;
using Th01.Core.Input;
using Th01.Platform.MonoGame;

namespace Th01.Platform.Tests;

public class WaveFourTests : IDisposable
{
    readonly string _dir = Directory.CreateTempSubdirectory("th01-w4-").FullName;
    public void Dispose() { try { Directory.Delete(_dir, true); } catch (IOException) { } }

    // ---- score store
    [Fact]
    public void ScoreConvert_RoundTripsRoutesAndNames()
    {
        var f = ScoreFileIO.CreateDefault();
        f.Routes[0] = ScoreFileIO.RouteJigoku;
        f.Routes[1] = ScoreFileIO.RouteClear;
        f.Stages[1] = 50;
        var t = ScoreConvert.FromFile(f);
        Assert.Equal(ScoreRoute.Jigoku, t.Entries[0].Route);
        Assert.Equal(ScoreRoute.Clear, t.Entries[1].Route);
        Assert.Equal(ScoreRoute.None, t.Entries[2].Route);
        Assert.Equal(1000, t.Entries[0].Score);
        var back = ScoreConvert.ToFile(t);
        Assert.Equal(ScoreFileIO.Serialize(f), ScoreFileIO.Serialize(back));
    }

    [Fact]
    public void NameChars_EncodeToFullWidthAndBack()
    {
        foreach (char c in "AZ09-. ") Assert.Equal(c, ScoreConvert.DecodeNameCell(ScoreConvert.EncodeNameChar(c)));
        Assert.Equal(0x8260, ScoreConvert.EncodeNameChar('A'));
        Assert.True(ScoreFileIO.IsValidKanji(ScoreConvert.EncodeNameChar('Q')));
        Assert.Equal('東', ScoreConvert.DecodeNameCell(0x938C)); // a kanji now decodes (the bundled font draws it)
        Assert.Equal(' ', ScoreConvert.DecodeNameCell(0x2020));
    }

    [Fact]
    public void FileScoreStore_SavesWithBackup_AndLoadsItBack()
    {
        string user = Path.Combine(_dir, "user");
        var store = new FileScoreStore(user);
        var t = store.Load(Rank.Hard);               // no file: the default table of the game
        Assert.Equal(1000, t.Entries[0].Score);
        t.Insert(0, [ScoreConvert.EncodeNameChar('X'), 0x8140, 0x8140, 0x8140, 0x8140, 0x8140, 0x8140, 0x8140], 5000, 3, ScoreRoute.Shrine);
        store.Save(Rank.Hard, t);
        store.Save(Rank.Hard, t);                    // second save: file exists, backup made once
        string file = Path.Combine(user, "REYHIHA.DAT");
        Assert.True(File.Exists(file));
        Assert.True(File.Exists(file + ".bak"));
        var again = new FileScoreStore(user).Load(Rank.Hard);
        Assert.Equal(5000, again.Entries[0].Score);
        Assert.Equal('X', ScoreConvert.DecodeNameCell(again.Entries[0].Name[0]));
        Assert.Equal(1000, new FileScoreStore(user).Load(Rank.Easy).Entries[0].Score); // other ranks untouched
    }

    [Fact]
    public void FileScoreStore_CopiesTheUsersFileFromTheGameFolder_NeverWritesThere()
    {
        string game = Path.Combine(_dir, "game"), user = Path.Combine(_dir, "user");
        Directory.CreateDirectory(game);
        var f = ScoreFileIO.CreateDefault();
        f.Scores[0] = 777777;
        byte[] original = ScoreFileIO.Serialize(f);
        File.WriteAllBytes(Path.Combine(game, "REYHINO.DAT"), original);
        var store = new FileScoreStore(user, game);
        var t = store.Load(Rank.Normal);
        Assert.Equal(777777, t.Entries[0].Score);
        store.Save(Rank.Normal, t);
        Assert.Equal(original, File.ReadAllBytes(Path.Combine(game, "REYHINO.DAT")));
        Assert.True(File.Exists(Path.Combine(user, "REYHINO.DAT")));
    }

    [Fact]
    public void FileScoreStore_SurvivesGarbageAndUnwritableFolders()
    {
        string user = Path.Combine(_dir, "user");
        Directory.CreateDirectory(user);
        File.WriteAllText(Path.Combine(user, "REYHILU.DAT"), "garbage");
        var store = new FileScoreStore(user);
        Assert.Equal(1000, store.Load(Rank.Lunatic).Entries[0].Score);

        string blocked = Path.Combine(_dir, "afile");
        File.WriteAllText(blocked, "x");              // a file where a folder is needed
        new FileScoreStore(Path.Combine(blocked, "sub")).Save(Rank.Easy, new HiScoreTable()); // must not throw
    }

    [Fact]
    public void Ranking_IsSavedWhenANameIsEntered()
    {
        string user = Path.Combine(_dir, "user");
        var f = new GameFlow(new FlowFakeStages(), new NoScreenImages(), BdfFont.LoadSpleen8x16(), new GameSettings(), startStage: 0, scores: new FileScoreStore(user));
        FlowFakeStages.Die(f);
        f.Tick(GameButtons.Down); f.Tick(GameButtons.None); f.Tick(GameButtons.Ok); f.Tick(GameButtons.None); // END
        Assert.Equal(Screen.NameEntry, f.Screen);
        f.Tick(GameButtons.Cancel); f.Tick(GameButtons.None);
        Assert.Equal(Screen.Ranking, f.Screen);
        Assert.True(File.Exists(Path.Combine(user, "REYHINO.DAT")));
        Assert.Equal(12345, new FileScoreStore(user).Load(Rank.Normal).Entries[0].Score);
    }

    // ---- slideshow
    sealed class FakeImages : IScreenImages
    {
        public readonly List<string> Loaded = new();
        public bool TryLoad(string file, out ArtFrame image)
        {
            Loaded.Add(file);
            var px = new byte[640 * 400];
            for (int y = 0; y < 400; y++) for (int x = 0; x < 640; x++) px[y * 640 + x] = (byte)((x / 320) + 2 * (y / 200) + 1); // quarter q => colour q+1
            var pal = new byte[48];
            for (int i = 0; i < 16; i++) { pal[i * 3] = (byte)i; pal[i * 3 + 1] = 8; pal[i * 3 + 2] = 0; }
            image = new ArtFrame(640, 400, px, Array.Empty<byte>(), pal);
            return true;
        }
    }

    [Fact]
    public void Slideshow_ShowsTheRequestedQuarterInTheMiddle_AndFinishes()
    {
        var show = new Slideshow(new FakeImages(), [new LoadPics("A.GRP"), new ShowPic(3, 5), new ShowPic(1, 5)]);
        var fb = new Framebuffer();
        show.Tone = 0;
        show.Render(fb);
        Assert.Equal(4, fb.GetPixel(320, 200));   // quarter 3 -> colour 4
        Assert.Equal(0, fb.GetPixel(10, 10));     // outside the picture
        for (int i = 0; i < 5; i++) show.Tick();
        show.Render(fb);
        Assert.Equal(2, fb.GetPixel(320, 200));
        Assert.False(show.Finished);
        for (int i = 0; i < 5; i++) show.Tick();
        Assert.True(show.Finished);
    }

    [Fact]
    public void Slideshow_ToneMovesLinearlyBetweenBlackAndWhite()
    {
        var show = new Slideshow(new FakeImages(), [new LoadPics("A.GRP"), new ShowPic(0, 1), new ToneTo(15, 15), new Wait(2)]);
        Assert.Equal(-15, show.Tone);
        var fb = new Framebuffer();
        show.Render(fb);
        fb.GetPalette4(15, out int r, out _, out _);
        Assert.Equal(0, r);                       // fully black at the start
        for (int i = 0; i < 1 + 15; i++) show.Tick();
        Assert.Equal(15, show.Tone);
        show.Render(fb);
        fb.GetPalette4(3, out r, out int g, out int b);
        Assert.Equal((15, 15, 15), (r, g, b));    // fully white
    }

    [Fact]
    public void Cutscenes_UseTheDocumentedFiles_AndTheFlowReturnsToTitle()
    {
        var images = new FakeImages();
        var f = new GameFlow(new FlowFakeStages(), images, BdfFont.LoadSpleen8x16(), new GameSettings(), startCutscene: 2);
        Assert.Equal(Screen.Cutscene, f.Screen);
        Assert.Contains("ENDB_A.GRP", images.Loaded);
        f.Render(new Framebuffer());
        for (int i = 0; i < 2000 && f.Screen == Screen.Cutscene; i++) f.Tick(GameButtons.None);
        Assert.Equal(Screen.Title, f.Screen);

        var g = new GameFlow(new FlowFakeStages(), images, BdfFont.LoadSpleen8x16(), new GameSettings(), startCutscene: 3);
        Assert.Contains("ENDB_B.GRP", images.Loaded);
        g.Tick(GameButtons.Ok);                   // a button skips it
        Assert.Equal(Screen.Title, g.Screen);

        var show = new Slideshow(images, Cutscenes.EndingOne());
        for (int i = 0; i < 5000 && !show.Finished; i++) show.Tick();
        Assert.True(show.Finished);
        foreach (string file in new[] { "ED1A.GRP", "ED1B.GRP", "ED1C.GRP", "ED1D.GRP", "ED1E.GRP" }) Assert.Contains(file, images.Loaded);
    }

    [Theory]
    [InlineData(0, 0)]
    [InlineData(1, 1)]
    public void AfterTheStageFiveBoss_TheChosenRouteLoadsTheNextScenes(int pickDowns, int expectedRoute)
    {
        var stages = new FlowFakeStages { Lives = 3 };
        var f = new GameFlow(stages, new NoScreenImages(), BdfFont.LoadSpleen8x16(), new GameSettings(), startStage: StageSetup.BossStage);
        for (int i = 0; i < 400 && f.Game!.Phase != SessionPhase.Playing; i++) f.Tick(GameButtons.None);
        Assert.Equal(SessionPhase.Playing, f.Game!.Phase);
        var b = f.Game.World.Boss!;
        b.Phase = 2; b.Hp = 1; b.Invincible = false;
        var o = f.Game.World.Orb;
        o.Left = b.Left + 32; o.Top = b.Top + 32; o.Y.Force = 0; o.ForceFrame = 0; o.PrevLeft = o.Left; o.PrevTop = o.Top;
        for (int i = 0; i < 3000 && f.Game.Phase != SessionPhase.RouteSelect; i++) f.Tick(GameButtons.None);
        Assert.Equal(SessionPhase.RouteSelect, f.Game.Phase);
        for (int i = 0; i < GameSession.RouteSelectDelayTicks; i++) f.Tick(GameButtons.None);
        for (int i = 0; i < pickDowns; i++) { f.Tick(GameButtons.Down); f.Tick(GameButtons.None); }
        f.Tick(GameButtons.Shot);                                   // confirm
        Assert.Equal(SessionPhase.StageBonus, f.Game.Phase);
        for (int i = 0; i < TotleTimeline.InputAt; i++) f.Tick(GameButtons.None);       // the results picture slides in first
        f.Tick(GameButtons.Shot); f.Tick(GameButtons.None);                             // release edge closes the bonus
        Assert.Equal(2, stages.Requests.Count);
        Assert.Equal((StageSetup.BossStage + 1, expectedRoute), stages.Requests[1]);
    }

    [Fact]
    public void TheRunEndsAfterTheLastStageThatIsPorted_WithANoteOnTheRanking()
    {
        var stages = new FlowFakeStages { Lives = 3 };
        var f = new GameFlow(stages, new NoScreenImages(), BdfFont.LoadSpleen8x16(), new GameSettings(), startStage: GameFlow.LastPlayableStage);
        for (int i = 0; i < EntranceOverlay.TotalTicks(true) + 5; i++) f.Tick(GameButtons.None);
        f.Game!.World.StageCleared = true;
        f.Tick(GameButtons.Shot);
        for (int i = 0; i < 5 && f.Game!.Phase != SessionPhase.StageBonus; i++) f.Tick(GameButtons.None);
        for (int i = 0; i < GameSession.BonusBoxTicks; i++) f.Tick(GameButtons.None);
        f.Tick(GameButtons.Shot); f.Tick(GameButtons.None);
        Assert.Contains(f.Screen, new[] { Screen.NameEntry, Screen.Ranking });
        Assert.Single(stages.Requests);                       // no stage 20 was requested
    }

    [Fact]
    public void OnTheJigokuRoute_ClearingStage20LeadsToTheEnding()
    {
        var stages = new FlowFakeStages { Lives = 3 };
        var f = new GameFlow(stages, new NoScreenImages(), BdfFont.LoadSpleen8x16(), new GameSettings(), startStage: GameFlow.LastStage, route: 1);
        Assert.Equal(SessionPhase.BossEntrance, f.Game!.Phase);   // stage_entrance() is not run for the stage 20 bosses: no banner first
        for (int i = 0; i < 20000 && f.Game!.Phase != SessionPhase.Playing; i++) f.Tick(GameButtons.None);   // banner, then the boss entrance
        Assert.IsType<Konngara>(f.Game!.World.Boss);
        f.Tick(GameButtons.Shot);
        f.Game!.World.StageCleared = true;
        f.Tick(GameButtons.None);
        for (int i = 0; i < 5 && f.Game!.Phase != SessionPhase.StageBonus; i++) f.Tick(GameButtons.None);
        for (int i = 0; i < TotleTimeline.InputAt; i++) f.Tick(GameButtons.None);
        f.Tick(GameButtons.Shot); f.Tick(GameButtons.None);
        Assert.Equal(Screen.Cutscene, f.Screen);              // the ending, not the "stages ported so far" note
        Assert.Single(stages.Requests);
    }

    // ---- real data: the problem found in review (sprites were never wired)
    [SkippableFact]
    public void RealData_SessionHasItsSpritesAndDrawsThemInsteadOfBoxes()
    {
        string? path = Th01.Assets.Config.GameDataConfig.Resolve();
        Skip.If(path is null || !Directory.Exists(path), "Game data not configured.");
        var assets = new Th01.Assets.FolderAssetSource(path!);
        var font = BdfFont.LoadSpleen8x16();
        var start = new SessionFactory(assets, 0, 0, font).Create();
        Assert.Equal(34, start.Art.FrameCount("STG.PTN"));
        Assert.True(start.Art.FrameCount("MIKO.PTN") > 0);
        Assert.True(start.Art.FrameCount("NUMB.PTN") > 0);
        Assert.True(start.Art.FrameCount("MIKO_AC.BOS") > 0);
        Assert.True(start.Art.FrameCount("MIKO_AC2.BOS") > 0);
        Assert.Equal(0, start.Art.FrameCount("NOT_A_SHEET.PTN"));

        var withArt = new Framebuffer();
        start.Session.Render(withArt);
        var plain = new Framebuffer();
        new GameSession(new Th01.Core.Game.GameOptions()).Render(plain);
        Assert.False(withArt.Pixels.SequenceEqual(plain.Pixels)); // a real frame is not the box-and-black fallback

        var boss = new SessionFactory(assets, StageSetup.BossStage, 0, font).Create();
        foreach (string s in new[] { "BOSS1.BOS", "BOSS1_2.BOS", "BOSS1_3.BOS" }) Assert.True(boss.Art.FrameCount(s) > 0, s);
        Assert.True(boss.Art.TryGetBackground(out _));
    }
}
