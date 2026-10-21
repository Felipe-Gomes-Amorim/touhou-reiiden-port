using Th01.Core.Game;
using Th01.Core.Input;
using Th01.Core.Sim;

namespace Th01.Core.Tests.Game;

public class HiScoreTests
{
    static ushort[] Name(ushort c) { var n = new ushort[HiScoreTable.NameKanji]; Array.Fill(n, c); return n; }

    static HiScoreTable Table(params int[] scores)
    {
        var t = new HiScoreTable();
        for (int i = 0; i < scores.Length; i++) { t.Entries[i].Score = scores[i]; t.Entries[i].Name = Name((ushort)(0x100 + i)); t.Entries[i].Stage = i + 1; }
        return t;
    }

    [Theory]
    [InlineData(1000, 0)]
    [InlineData(900, 0)]       // a tie goes above the older entry
    [InlineData(899, 1)]
    [InlineData(100, 9)]
    [InlineData(99, 10)]       // not good enough
    public void Place_is_the_first_entry_the_score_equals_or_beats(int score, int place)
    {
        var t = Table(900, 800, 700, 600, 500, 400, 300, 200, 150, 100);
        Assert.Equal(place, t.FindPlace(score));
    }

    [Fact]
    public void Insert_shifts_everything_below_and_drops_the_last()
    {
        var t = Table(900, 800, 700, 600, 500, 400, 300, 200, 150, 100);
        t.Insert(2, Name(0xAA), 750, 9, ScoreRoute.Makai);
        Assert.Equal(new[] { 900, 800, 750, 700, 600, 500, 400, 300, 200, 150 }, t.Entries.Select(e => e.Score));
        Assert.Equal(0xAA, t.Entries[2].Name[0]);
        Assert.Equal(0x102, t.Entries[3].Name[0]);       // the old third entry moved down with its name
        Assert.Equal(3, t.Entries[3].Stage);
        Assert.Equal(ScoreRoute.Makai, t.Entries[2].Route);
        Assert.Equal(0x108, t.Entries[9].Name[0]);       // old 9th entry; the old 10th is gone
    }

    [Fact]
    public void Inserted_name_is_a_copy()
    {
        var t = Table(5);
        var n = Name(1);
        t.Insert(0, n, 10, 1, ScoreRoute.Shrine);
        n[0] = 99;
        Assert.Equal(1, t.Entries[0].Name[0]);
    }

    [Theory]
    [InlineData(5, false)]
    [InlineData(29, false)]
    [InlineData(30, true)]
    [InlineData(HiScoreTable.ClearedMakai, true)]
    [InlineData(HiScoreTable.ClearedJigoku, true)]
    public void Cleared_threshold(int stage, bool cleared) => Assert.Equal(cleared, HiScoreTable.IsClearedStage(stage));

    [Fact]
    public void Name_entry_types_moves_and_clamps()
    {
        var e = new NameEntry(cleared: true);
        Assert.All(e.Name, c => Assert.Equal(NameEntry.Blank, c));
        Assert.False(e.Put(0x8260));
        Assert.Equal(1, e.Cursor);
        e.CursorLeft(); e.CursorLeft();
        Assert.Equal(0, e.Cursor);
        for (int i = 0; i < 20; i++) e.CursorRight();
        Assert.Equal(7, e.Cursor);
        Assert.True(e.Put(0x8261));                      // last cell: stays there and says so
        Assert.Equal(7, e.Cursor);
        Assert.Equal(0x8261, e.Name[7]);
        e.Put(0x8262);                                   // overwrites the last cell
        Assert.Equal(0x8262, e.Name[7]);
    }

    [Fact]
    public void Game_over_name_entry_times_out_after_1000_inputs_but_a_cleared_one_does_not()
    {
        var g = new NameEntry(cleared: false);
        for (int i = 0; i < 1000; i++) g.CursorRight();
        Assert.False(g.TimedOut);
        g.CursorLeft();
        Assert.True(g.TimedOut);
        var c = new NameEntry(cleared: true);
        for (int i = 0; i < 5000; i++) c.CursorRight();
        Assert.False(c.TimedOut);
    }
}

public class GameOverFlowTests
{
    static StageSetup Stage(int id, int route = 0)
    {
        var c = new bool[StageSetup.Rows, StageSetup.Cols]; c[9, 19] = true;
        return new StageSetup { StageId = id, Route = route, Cards = c, Obstacles = new byte[StageSetup.Rows, StageSetup.Cols] };
    }

    static GameSession Dying(StageSetup st, Rank rank = Rank.Hard, uint score = 12345)
    {
        var g = new GameSession(new GameOptions { FrameRand = 77, Stage = st, Rank = rank, Lives = 0, Score = score, PelletSpeed = 3 });
        g.Step(GameButtons.Shot); g.Step(GameButtons.None);
        g.World.Orb.Left = g.World.Player.Left; g.World.Orb.Top = Playfield.PlayerTop - 8;
        g.World.Orb.Y.Force = 0; g.World.Orb.ForceFrame = 0;
        g.Step(GameButtons.None);
        return g;
    }

    [Fact]
    public void Game_over_exposes_the_data_for_the_ranking_screen()
    {
        Assert.Null(new GameSession(new GameOptions { Stage = Stage(0) }).GameOverResult);
        var g = Dying(Stage(2));
        Assert.Equal(SessionOutcome.GameOver, g.Outcome);
        var r = g.GameOverResult!;
        Assert.Equal(12345u, r.Score);
        Assert.Equal(3, r.StageNum);
        Assert.Equal(ScoreRoute.Shrine, r.Route);
        Assert.Equal(Rank.Hard, r.Rank);
    }

    [Theory]
    [InlineData(3, 0, ScoreRoute.Shrine)]
    [InlineData(5, 0, ScoreRoute.Makai)]
    [InlineData(7, 1, ScoreRoute.Jigoku)]
    public void Route_column_follows_stage_and_route(int stageId, int route, ScoreRoute expected)
    {
        var g = Dying(Stage(stageId, route));
        Assert.Equal(expected, g.GameOverResult!.Route);
    }

    [Fact]
    public void Game_over_is_final_and_frozen()
    {
        var g = Dying(Stage(0));
        ulong h = g.StateHash();
        for (int i = 0; i < 300; i++)
        {
            g.Step(i % 2 == 0 ? GameButtons.Shot : GameButtons.Left);
            Assert.Equal(SessionOutcome.GameOver, g.Outcome);
        }
        Assert.Equal(h, g.StateHash());
        Assert.Null(g.CarryOver);
    }

    [Fact]
    public void Continue_restarts_the_stage_with_fresh_credits_and_a_slower_pellet_speed()
    {
        var g = Dying(Stage(6, 1));
        var o = g.GameOverResult!.ContinueOptions(Stage(6, 1), creditLivesExtra: 2);
        Assert.Equal(0u, o.Score);
        Assert.Equal(4, o.Lives);
        Assert.Equal(1, o.Bombs);
        Assert.Equal(0, o.PointValue);
        Assert.Equal(-2, o.PelletSpeed);                  // 3 > -2: clamped to -2, no delta
        Assert.Equal(Rank.Hard, o.Rank);
        Assert.Equal(g.World.FrameRand, o.FrameRand);
        var again = new GameSession(o);
        Assert.Equal(SessionOutcome.Running, again.Outcome);
        Assert.Equal(0u, again.World.Score);

        // At the maximum itself the delta applies; the lower bound clamps.
        Assert.Equal(-7, new GameOverResult(0, 1, ScoreRoute.Shrine, Rank.Normal, -2, 1).ContinueOptions(Stage(0), 2).PelletSpeed);
        Assert.Equal(-15, new GameOverResult(0, 1, ScoreRoute.Shrine, Rank.Normal, -14, 1).ContinueOptions(Stage(0), 2).PelletSpeed);
    }
}
