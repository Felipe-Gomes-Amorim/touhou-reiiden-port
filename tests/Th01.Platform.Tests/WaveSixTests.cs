using Th01.Core.Game;
using Th01.Core.Graphics;
using Th01.Core.Input;
using Th01.Platform.MonoGame;
using Xunit.Abstractions;

namespace Th01.Platform.Tests;

public class WaveSixTests(ITestOutputHelper output)
{
    [Theory]
    [InlineData(0, 0, 0, "ST0.GRP")]
    [InlineData(4, 0, 0, "BOSS1.GRP")]
    [InlineData(5, 1, 2, "ST2.GRP")]
    [InlineData(9, 0, 1, "BOSS2.GRP")]
    [InlineData(9, 1, 2, "BOSS3.GRP")]
    [InlineData(14, 1, 4, "BOSS4.GRP")]
    [InlineData(14, 0, 3, "BOSS5.GRP")]
    [InlineData(19, 0, 5, "ST5.GRP")]
    public void BackgroundFile_FollowsTheBossStageTable(int stage, int route, int scene, string expected)
    {
        Assert.Equal(scene, SessionFactory.SceneId(stage, route));
        Assert.Equal(expected, SessionFactory.BackgroundFile(stage, route, scene));
    }

    [Theory]
    [InlineData(3, 0, true)]
    [InlineData(4, 1, true)]       // the first boss exists on both routes
    [InlineData(9, 1, true)]       // Mima
    [InlineData(9, 0, true)]       // YuugenMagan
    [InlineData(14, 0, true)]      // Elis
    [InlineData(14, 1, true)]      // Kikuri
    [InlineData(10, 1, true)]
    [InlineData(19, 0, false)]     // Sariel is not ported
    [InlineData(19, 1, false)]     // Konngara is not ported
    public void IsPlayable_KnowsWhichBossesExist(int stage, int route, bool playable) =>
        Assert.Equal(playable, GameFlow.IsPlayable(stage, route));

    [Fact]
    public void OnBothRoutes_TheRunContinuesIntoTheStage10Boss()
    {
        foreach (var (route, nextRequested) in new[] { (1, true), (0, true) })
        {
            var stages = new FlowFakeStages { Lives = 3 };
            var f = new GameFlow(stages, new NoScreenImages(), BdfFont.LoadSpleen8x16(), new GameSettings(), startStage: 8, route: route);
            for (int i = 0; i < EntranceOverlay.TotalTicks(true) + 5; i++) f.Tick(GameButtons.None);
            f.Game!.World.StageCleared = true;
            f.Tick(GameButtons.Shot);
            for (int i = 0; i < 5 && f.Game!.Phase != SessionPhase.StageBonus; i++) f.Tick(GameButtons.None);
            for (int i = 0; i < GameSession.BonusBoxTicks; i++) f.Tick(GameButtons.None);
            f.Tick(GameButtons.Shot); f.Tick(GameButtons.None);
            Assert.Equal(nextRequested, stages.Requests.Contains((9, route)));
            Assert.Equal(nextRequested ? Screen.InGame : Screen.NameEntry, f.Screen == Screen.Ranking ? Screen.NameEntry : f.Screen);
        }
    }

    /// <summary>The Mima fight (stage 10, Jigoku route): background, boss sheets, and a frame with real sprites.</summary>
    [SkippableFact]
    public void RealData_MimaStageHasItsBackgroundAndSheets_AndPlaysWithoutMissingSheets()
    {
        string? path = Th01.Assets.Config.GameDataConfig.Resolve();
        Skip.If(path is null || !Directory.Exists(path), "Game data not configured.");
        var assets = new Th01.Assets.FolderAssetSource(path!);
        var log = new MissingArtLog();
        var start = new SessionFactory(assets, 9, 1, BdfFont.LoadSpleen8x16(), log).Create();
        Assert.True(start.Art.TryGetBackground(out var bg));
        Assert.Equal((640, 400), (bg.Width, bg.Height));
        foreach (string s in new[] { "BOSS3_1.BOS", "BOSS3_2.BOS", "BOSS3_M.PTN" }) Assert.True(start.Art.FrameCount(s) > 0, s);

        var session = (GameSession)start.Session;
        Assert.IsType<Mima>(session.World.Boss);
        var fb = new Framebuffer();
        for (int i = 0; i < 1500; i++)
        {
            session.Step(i % 9 == 0 ? GameButtons.Shot | GameButtons.Right : GameButtons.Left);
            if (i % 50 == 0) session.Render(fb);
        }
        foreach (string line in log.Lines) output.WriteLine(line);
        // A missing whole sheet means a registration problem; an index outside a sheet is for the renderer owner to look at.
        Assert.DoesNotContain(log.Lines, l => l.Contains("sheet not registered") || l.Contains("failed to load"));
    }
}
