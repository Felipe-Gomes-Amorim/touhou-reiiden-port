using Th01.Core.Game;
using Th01.Core.Input;

namespace Th01.Core.Tests.Game;

/// <summary>Sariel inside a GameSession: selection, entrance, the blocking form 1 defeat, the final defeat bonus and the cleared game.</summary>
public class SarielSessionTests
{
    static StageSetup Stage() => new()
    {
        StageId = Sariel.Id, Route = 0,
        Cards = new bool[StageSetup.Rows, StageSetup.Cols],
        Obstacles = new byte[StageSetup.Rows, StageSetup.Cols],
    };

    static GameSession Playing()
    {
        var g = new GameSession(new GameOptions { FrameRand = 12, Stage = Stage(), Lives = 3, Bombs = 1 });
        Assert.IsType<Sariel>(g.World.Boss);
        Assert.Equal(SessionPhase.BossEntrance, g.Phase);          // no stage banner for Sariel
        for (int i = 0; i < Sariel.EntranceTotal + 5; i++) g.Step(GameButtons.None);
        Assert.Equal(SessionPhase.Playing, g.Phase);
        return g;
    }

    [Fact]
    public void The_fight_begins_in_phase_1_after_the_entrance()
    {
        var g = Playing();
        var s = (Sariel)g.World.Boss!;
        for (int i = 0; i < 5; i++) g.Step(GameButtons.None);
        Assert.Equal(1, s.Phase);
        Assert.Equal(18, s.Hp);
    }

    [Fact]
    public void The_form_1_defeat_blocks_the_world_then_form_2_starts_with_6_hp()
    {
        var g = Playing();
        var s = (Sariel)g.World.Boss!;
        g.Step(GameButtons.None);
        s.Phase = 5; s.Hp = 0;
        g.Step(GameButtons.None);
        Assert.Equal(Sariel.PhaseForm1Defeated, s.Phase);
        int frameRand = (int)g.World.FrameRand;
        int ticks = 0;
        while (s.Phase == Sariel.PhaseForm1Defeated && ticks++ < 2000)
        {
            g.Step(GameButtons.Left | GameButtons.Shot);
            Assert.Equal((uint)frameRand, g.World.FrameRand);       // nothing else ran: not even the frame counter
        }
        Assert.Equal((Sariel.PhaseForm2, 6), (s.Phase, s.Hp));
        g.Step(GameButtons.None);
        Assert.NotEqual((uint)frameRand, g.World.FrameRand);       // the world runs again
        Assert.Equal(SessionPhase.Playing, g.Phase);
    }

    [Fact]
    public void The_final_defeat_grants_50000_points_once_and_clears_the_game()
    {
        var g = Playing();
        var s = (Sariel)g.World.Boss!;
        g.Step(GameButtons.None);
        s.Phase = Sariel.PhaseForm2; s.Hp = 0;
        g.Step(GameButtons.None);                                   // form 2 notices the zero HP and moves to phase 101
        g.Step(GameButtons.None);
        Assert.Equal(SessionPhase.BossDefeat, g.Phase);
        uint before = g.World.Score;
        Assert.False(g.World.GameCleared);
        int guard = 0;
        while (g.Phase == SessionPhase.BossDefeat && guard++ < 5000) g.Step(GameButtons.None);
        Assert.Equal(SessionPhase.StageBonus, g.Phase);
        Assert.True(g.World.GameCleared);
        Assert.Equal(before + 50000u + g.Bonus!.Value.Total, g.World.Score);
    }

    [Fact]
    public void A_whole_fight_with_no_input_is_deterministic()
    {
        ulong Run()
        {
            var g = new GameSession(new GameOptions { FrameRand = 5, Stage = Stage(), Lives = 3, Bombs = 3, Rank = Rank.Lunatic });
            for (int i = 0; i < Sariel.EntranceTotal + 6000; i++) g.Step(i % 40 < 20 ? GameButtons.Left : GameButtons.Right);
            return g.StateHash();
        }
        Assert.Equal(Run(), Run());
    }
}
