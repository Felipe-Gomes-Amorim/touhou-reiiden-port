using Th01.Core.Game;
using Th01.Core.Input;
using Th01.Core.Sim;

namespace Th01.Core.Tests.Game;

public class CarryOverTests
{
    static StageSetup Stage(int id, params (int r, int c)[] cards)
    {
        var c = new bool[StageSetup.Rows, StageSetup.Cols];
        foreach (var (r, col) in cards) c[r, col] = true;
        return new StageSetup { StageId = id, Cards = c, Obstacles = new byte[StageSetup.Rows, StageSetup.Cols] };
    }

    /// <summary>Plays a short deterministic first stage until it is cleared.</summary>
    static GameSession PlayFirst(uint seed)
    {
        var s = new GameSession(new GameOptions { FrameRand = seed, Stage = Stage(0, (0, 5)), Lives = 3, Bombs = 2 });
        s.Step(GameButtons.Shot); s.Step(GameButtons.None);
        for (int i = 0; i < 6; i++) s.Step(GameButtons.Left);
        var w = s.World;
        w.Orb.Left = 160; w.Orb.Top = 64; w.Orb.Y.Force = -3.3; w.Orb.ForceFrame = 0;
        w.Orb.Y.VelocityY = 2.5;       // leaves a non-trivial velocity behind
        int guard = 0;
        while (s.Phase != SessionPhase.StageBonus && guard++ < 500) s.Step(GameButtons.None);
        Assert.Equal(SessionPhase.StageBonus, s.Phase);
        StageTests.Dismiss(s);
        Assert.Equal(SessionOutcome.Cleared, s.Outcome);
        return s;
    }

    [Fact]
    public void Carry_over_is_only_available_once_cleared()
    {
        var s = new GameSession(new GameOptions { Stage = Stage(0, (0, 5)) });
        Assert.Null(s.CarryOver);
        Assert.NotNull(PlayFirst(1).CarryOver);
    }

    [Fact]
    public void Carry_over_captures_the_persistent_globals()
    {
        var a = PlayFirst(9);
        var w = a.World;
        var c = a.CarryOver!;
        Assert.Equal(w.Score, c.Score);
        Assert.Equal(100u + a.Bonus!.Value.Total, c.Score);   // one card, combo 0, plus the stage bonus
        Assert.Equal(3, c.RemLives);
        Assert.Equal(2, c.RemBombs);
        Assert.Equal(w.CardFlipCycle, c.CardFlipCycle);
        Assert.Equal(w.FrameRand, c.FrameRand);
        Assert.Equal(w.Player.Left, c.PlayerLeft);
        Assert.Equal(1, c.ExtendNext);
        Assert.Equal(w.Orb.Y.VelocityYBits, c.OrbVelocityYBits);
    }

    [Fact]
    public void Next_stage_starts_from_the_carried_state()
    {
        var a = PlayFirst(9);
        var c = a.CarryOver!;
        var b = new GameSession(new GameOptions { Stage = Stage(1, (3, 3)), CarryOver = c, Rank = Rank.Normal });
        var w = b.World;
        Assert.Equal(c.Score, w.Score);
        Assert.Equal(c.RemLives, w.RemLives);
        Assert.Equal(c.RemBombs, w.RemBombs);
        Assert.Equal(c.CardFlipCycle, w.CardFlipCycle);          // not redrawn
        Assert.Equal(c.FrameRand, w.FrameRand);
        Assert.Equal(c.OrbVelocityYBits, w.Orb.Y.VelocityYBits); // survives Orb.StageStart
        Assert.Equal(c.PlayerLeft, w.Player.Left);                // stage 1 is not the first of a scene
    }

    [Fact]
    public void Player_returns_to_the_start_column_on_the_first_stage_of_a_scene()
    {
        var c = new SessionCarryOver { PlayerLeft = 40 };
        var mid = new GameSession(new GameOptions { Stage = Stage(3, (3, 3)), CarryOver = c });
        Assert.Equal(40, mid.World.Player.Left);
        var first = new GameSession(new GameOptions { Stage = Stage(5, (3, 3)), CarryOver = c });
        Assert.Equal(Playfield.PlayerLeftStart, first.World.Player.Left);
    }

    static ulong TwoStages(uint seed, int ticks)
    {
        var a = PlayFirst(seed);
        var b = new GameSession(new GameOptions { Stage = Stage(1, (2, 2), (2, 9), (6, 14)), CarryOver = a.CarryOver, Lives = 99 });
        uint x = 0xC0FFEE11;
        GameButtons cur = GameButtons.None;
        for (int i = 0; i < ticks; i++)
        {
            x ^= x << 13; x ^= x >> 17; x ^= x << 5;
            if (i % 6 == 0)
            {
                cur = GameButtons.None;
                if ((x & 1) != 0) cur |= GameButtons.Left;
                if ((x & 2) != 0) cur |= GameButtons.Right;
                if ((x & 4) != 0) cur |= GameButtons.Shot;
            }
            b.Step(cur);
        }
        return b.StateHash();
    }

    [Fact]
    public void Two_stages_in_sequence_are_reproducible_and_depend_on_the_carry_over()
    {
        Assert.Equal(TwoStages(5, 1500), TwoStages(5, 1500));
        Assert.NotEqual(TwoStages(5, 1500), TwoStages(6, 1500));
    }

    [Fact]
    public void Carried_state_enters_the_state_hash()
    {
        var c = new SessionCarryOver { Score = 1000, RemLives = 3 };
        var h0 = new GameSession(new GameOptions { Stage = Stage(1, (3, 3)), CarryOver = c }).StateHash();
        foreach (var d in new[]
        {
            c with { Score = 1001 }, c with { RemLives = 2 }, c with { RemBombs = 4 }, c with { PointValue = 1000 },
            c with { ExtendNext = 3 }, c with { CardFlipCycle = 9 }, c with { OrbVelocityYBits = 1234 },
            c with { FrameRand = 77 }, c with { PlayerLeft = 100 },
        })
        {
            var h = new GameSession(new GameOptions { Stage = Stage(1, (3, 3)), CarryOver = d }).StateHash();
            Assert.NotEqual(h0, h);
        }
    }
}
