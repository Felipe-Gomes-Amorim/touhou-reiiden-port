using Th01.Core.Game;
using Th01.Core.Graphics;
using Th01.Core.Input;
using Th01.Platform.MonoGame;

namespace Th01.Platform.Tests;

/// <summary>Every boss that is reachable by playing must really be created with its sprites, on both routes.</summary>
public class BossRoutesTests
{
    [SkippableTheory]
    [InlineData(4, 0, typeof(SinGyoku))]
    [InlineData(4, 1, typeof(SinGyoku))]
    [InlineData(9, 0, typeof(YuugenMagan))]
    [InlineData(9, 1, typeof(Mima))]
    [InlineData(14, 0, typeof(Elis))]
    [InlineData(14, 1, typeof(Kikuri))]
    public void RealData_EachReachableBossStage_HasItsBoss_AndItsSheetsAreRegistered(int stage, int route, Type boss)
    {
        string? path = Th01.Assets.Config.GameDataConfig.Resolve();
        Skip.If(path is null || !Directory.Exists(path), "Game data not configured.");
        var assets = new Th01.Assets.FolderAssetSource(path!);
        var log = new MissingArtLog();
        var start = new SessionFactory(assets, stage, route, Th01.Assets.Text.JapaneseFont.CreateDefault(), log).Create();

        var session = (GameSession)start.Session;
        Assert.IsType(boss, session.World.Boss);
        Assert.True(start.Art.TryGetBackground(out _), "boss background missing");

        var fb = new Framebuffer();
        for (int i = 0; i < 2000; i++)
        {
            session.Step(i % 9 == 0 ? GameButtons.Shot | GameButtons.Right : GameButtons.Left);
            if (i % 50 == 0) session.Render(fb);
        }
        // A whole sheet that is not registered, or one that fails to load, would draw flat boxes instead of the boss.
        Assert.DoesNotContain(log.Lines, l => l.Contains("sheet not registered") || l.Contains("failed to load"));
    }

    [Theory]
    [InlineData(8, 0)]
    [InlineData(8, 1)]
    [InlineData(13, 0)]
    [InlineData(13, 1)]
    public void ClearingTheStageBeforeABoss_LoadsTheBoss_OnBothRoutes(int lastCardStage, int route)
    {
        var stages = new FlowFakeStages { Lives = 3 };
        var f = new GameFlow(stages, new NoScreenImages(), BdfFont.LoadSpleen8x16(), new GameSettings(), startStage: lastCardStage, route: route);
        for (int i = 0; i < EntranceOverlay.TotalTicks(true) + 5; i++) f.Tick(GameButtons.None);
        f.Game!.World.StageCleared = true;
        f.Tick(GameButtons.Shot);
        for (int i = 0; i < 5 && f.Game!.Phase != SessionPhase.StageBonus; i++) f.Tick(GameButtons.None);
        for (int i = 0; i < GameSession.BonusBoxTicks; i++) f.Tick(GameButtons.None);
        f.Tick(GameButtons.Shot); f.Tick(GameButtons.None);
        Assert.Contains((lastCardStage + 1, route), stages.Requests);
        Assert.Equal(Screen.InGame, f.Screen);
    }

    [Fact]
    public void TheRunStillEndsBeforeStage20_WhichIsNotPortedYet()
    {
        foreach (int route in new[] { 0, 1 })
        {
            var stages = new FlowFakeStages { Lives = 3 };
            var f = new GameFlow(stages, new NoScreenImages(), BdfFont.LoadSpleen8x16(), new GameSettings(), startStage: 18, route: route);
            for (int i = 0; i < EntranceOverlay.TotalTicks(true) + 5; i++) f.Tick(GameButtons.None);
            f.Game!.World.StageCleared = true;
            f.Tick(GameButtons.Shot);
            for (int i = 0; i < 5 && f.Game!.Phase != SessionPhase.StageBonus; i++) f.Tick(GameButtons.None);
            for (int i = 0; i < GameSession.BonusBoxTicks; i++) f.Tick(GameButtons.None);
            f.Tick(GameButtons.Shot); f.Tick(GameButtons.None);
            Assert.DoesNotContain((19, route), stages.Requests);
        }
    }
}
