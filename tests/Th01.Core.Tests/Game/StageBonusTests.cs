using Th01.Core.Game;
using Th01.Core.Input;
using Th01.Core.Sim;

namespace Th01.Core.Tests.Game;

public class StageBonusTests
{
    [Fact]
    public void Bonus_metrics_are_clamped_and_each_counted_ten_times()
    {
        var b = StageBonus.Compute(stageNum: 3, timerValue: 1000, cardComboMax: 12, lives: 4, bombs: 2);
        Assert.Equal(3000, b.Time);
        Assert.Equal(1200, b.CardCombo);
        Assert.Equal(1000, b.Resources);       // 4*200 + 2*100
        Assert.Equal(400, b.StageNumber);      // (3 - 1) * 200
        Assert.Equal((3000u + 1200 + 1000 + 400) * 10, b.Total);
    }

    [Theory]
    [InlineData(2500, 6553)]    // 7500 -> clamped
    [InlineData(2184, 6552)]    // 6552 just below the cap
    [InlineData(2185, 6553)]    // 6555 -> 6553
    public void Time_clamp_boundary(int timer, int expectedTime) =>
        Assert.Equal(expectedTime, StageBonus.Compute(1, timer, 0, 0, 0).Time);

    [Fact]
    public void Other_metrics_clamp_at_6553_too()
    {
        var b = StageBonus.Compute(40, 0, 100, 6, 5);
        Assert.Equal(6553, b.CardCombo);        // 10000 -> 6553
        Assert.Equal(1700, b.Resources);
        Assert.Equal(6553, b.StageNumber);      // 7800 -> 6553
    }

    static StageSetup OneCard(int stageId = 4 - 4) 
    {
        var c = new bool[StageSetup.Rows, StageSetup.Cols]; c[0, 5] = true;
        return new StageSetup { StageId = stageId, Cards = c, Obstacles = new byte[StageSetup.Rows, StageSetup.Cols] };
    }

    static GameSession Cleared(GameOptions o)
    {
        var g = new GameSession(o);
        g.Step(GameButtons.Shot); g.Step(GameButtons.None);
        g.World.Orb.Left = 160; g.World.Orb.Top = 64;
        int guard = 0;
        while (g.Phase != SessionPhase.StageBonus && guard++ < 500) g.Step(GameButtons.None);
        return g;
    }

    [Fact]
    public void Clearing_grants_the_bonus_records_it_and_waits_for_a_key_release()
    {
        var g = Cleared(new GameOptions { Stage = OneCard(7), Lives = 3, Bombs = 2 });
        var w = g.World;
        Assert.Equal(SessionPhase.StageBonus, g.Phase);
        var b = g.Bonus!.Value;
        Assert.Equal(StageBonus.Compute(8, w.Timer.Value, 1, 3, 2), b);
        Assert.Equal(200u + b.Total, w.Score);   // card on stage 7: (7 / 5) * 100 + 100
        Assert.Equal(b.Total, w.BonusPerStage[7 % 5]);
        Assert.Equal(SessionOutcome.Running, g.Outcome);
        Assert.Null(g.CarryOver);

        for (int i = 0; i < 30; i++) g.Step(GameButtons.None);   // nothing happens on its own
        Assert.Equal(SessionPhase.StageBonus, g.Phase);
        g.Step(GameButtons.Shot);                                 // pressing is not enough
        g.Step(GameButtons.Shot);
        Assert.Equal(SessionPhase.StageBonus, g.Phase);
        g.Step(GameButtons.None);                                 // release closes the box
        Assert.Equal(SessionOutcome.Cleared, g.Outcome);
        Assert.NotNull(g.CarryOver);
        Assert.Equal(w.Score, g.CarryOver!.Score);
        Assert.Equal(b.Total, g.CarryOver.BonusPerStage![2]);
    }

    [Fact]
    public void Ok_also_closes_the_box_and_a_key_held_on_entry_counts()
    {
        var g = Cleared(new GameOptions { Stage = OneCard() });
        g.Step(GameButtons.Ok);
        Assert.Equal(SessionPhase.StageBonus, g.Phase);
        g.Step(GameButtons.None);
        Assert.Equal(SessionOutcome.Cleared, g.Outcome);
    }

    [Fact]
    public void Bonus_can_trigger_an_extend()
    {
        var g = new GameSession(new GameOptions { Stage = OneCard(), Lives = 3, Score = 399_990 });
        g.Step(GameButtons.Shot); g.Step(GameButtons.None);
        g.World.Orb.Left = 160; g.World.Orb.Top = 64;
        g.World.Score = 399_990;
        int guard = 0;
        while (g.Phase != SessionPhase.StageBonus && guard++ < 500) g.Step(GameButtons.None);
        Assert.Equal(4, g.World.RemLives);
    }

    [Fact]
    public void Scene_bonuses_are_carried_per_stage_of_the_scene()
    {
        var g = Cleared(new GameOptions { Stage = OneCard(1) });
        StageTests.Dismiss(g);
        var next = new GameSession(new GameOptions { Stage = OneCard(2), CarryOver = g.CarryOver });
        Assert.Equal(g.Bonus!.Value.Total, next.World.BonusPerStage[1]);
        Assert.Equal(0u, next.World.BonusPerStage[2]);
    }

    // ---- entrance ----

    [Fact]
    public void Entrance_freezes_everything_for_the_banner_then_hands_over_to_the_wait_for_shot()
    {
        var g = new GameSession(new GameOptions { Stage = OneCard(), Entrance = true });
        Assert.Equal(SessionPhase.Entrance, g.Phase);
        int left = g.World.Player.Left;
        int total = EntranceOverlay.TotalTicks(withTitle: true);    // stage 0 is the first of its scene: title + banner
        for (int i = 0; i < total - 1; i++)
        {
            g.Step(GameButtons.Left | GameButtons.Shot);        // input is ignored meanwhile
            Assert.Equal(SessionPhase.Entrance, g.Phase);
        }
        g.Step(GameButtons.None);
        Assert.Equal(SessionPhase.WaitForShot, g.Phase);
        Assert.Equal(left, g.World.Player.Left);
        Assert.False(g.World.Input.Shot);
        Assert.Equal(70, GameSession.EntranceTicks);            // 2 x frame_delay(35); not yet measured on the original (T5)
        Assert.Equal(48, GameSession.EntranceTitleTicks);       // 6 characters x frame_delay(8)
        Assert.Equal(118, total);
    }

    [Fact]
    public void Entrance_is_off_by_default()
    {
        Assert.Equal(SessionPhase.WaitForShot, new GameSession(new GameOptions { Stage = OneCard() }).Phase);
    }

    [Fact]
    public void Two_stage_sequence_with_bonus_is_reproducible()
    {
        ulong Run()
        {
            var a = Cleared(new GameOptions { FrameRand = 4, Stage = OneCard(0), Lives = 3, Bombs = 1, Entrance = true });
            StageTests.Dismiss(a);
            var b = new GameSession(new GameOptions { Stage = OneCard(1), CarryOver = a.CarryOver, Entrance = true });
            for (int i = 0; i < 300; i++) b.Step(i % 40 < 20 ? GameButtons.Left : GameButtons.Right);
            return b.StateHash();
        }
        Assert.Equal(Run(), Run());
    }
}
