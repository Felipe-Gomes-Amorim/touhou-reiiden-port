using Th01.Core.Input;
using Th01.Core.Rng;
using Th01.Core.Sim;
using Th01.Tools.Oracle;

namespace Th01.Parity.Tests;

public class OracleToolTests
{
    static TraceTable Make(params string[] rows)
    {
        var t = new TraceTable(new[] { "tick", "player_x", "score" });
        foreach (var r in rows) t.Rows.Add(r.Split(','));
        return t;
    }

    [Fact]
    public void Compare_EqualTraces_Match()
    {
        var r = TraceCompare.Compare(Make("0,10,0", "1,12,5"), Make("0,10,0", "1,12,5"));
        Assert.True(r.Equal);
    }

    [Fact]
    public void Compare_ReportsFirstDivergentTickAndColumns()
    {
        var a = Make("0,10,0", "1,12,5", "2,14,5", "3,16,5");
        var b = Make("0,10,0", "1,12,5", "2,15,6", "3,99,5");
        var r = TraceCompare.Compare(a, b);
        Assert.Equal("2", r.FirstDivergentTick);
        Assert.Equal(new[] { "player_x", "score" }, r.Diffs.Select(d => d.Column));
        string text = TraceCompare.Format(a, b, r, 1);
        Assert.Contains("DIVERGENCE at tick 2", text);
    }

    [Fact]
    public void Compare_EmptyCellMeansNotAvailable()
    {
        var r = TraceCompare.Compare(Make("0,10,"), Make("0,10,77"));
        Assert.True(r.Equal);
    }

    [Fact]
    public void Compare_LengthMismatchIsReported()
    {
        var r = TraceCompare.Compare(Make("0,1,1", "1,1,1"), Make("0,1,1"));
        Assert.False(r.Equal);
        Assert.NotNull(r.LengthNote);
    }

    [Fact]
    public void Trace_RoundTripsThroughCsv()
    {
        var t = Make("0,10,0");
        t.Meta["source"] = "original";
        var w = new StringWriter();
        t.Write(w);
        var back = TraceTable.Parse(new StringReader(w.ToString()));
        Assert.Equal("original", back.Meta["source"]);
        Assert.Equal("10", back.Get(0, "player_x"));
    }

    [Fact]
    public void InputScript_ResolvesHeldButtonsPerTick()
    {
        var s = InputScript.Parse(new StringReader("seed 0x10\nticks 40\n0 none\n10 left shot # hold\n30 none\n"));
        Assert.Equal(16u, s.Seed);
        Assert.Equal(GameButtons.None, s.At(9));
        Assert.Equal(GameButtons.Left | GameButtons.Shot, s.At(10));
        Assert.Equal(GameButtons.Left | GameButtons.Shot, s.At(29));
        Assert.Equal(GameButtons.None, s.At(30));
    }

    [Fact]
    public void InputScript_RejectsUnknownButton()
    {
        Assert.Throws<FormatException>(() => InputScript.Parse(new StringReader("ticks 5\n0 jump\n")));
    }

    sealed class Session : IGameSession
    {
        readonly DummyState _s;
        public Session(uint seed) => _s = new DummyState(seed);
        public void Step(GameButtons input) => _s.Step(input);
        public void Render(Th01.Core.Graphics.Framebuffer target) { }
        public ulong StateHash() => _s.Hash();
    }

    [Fact]
    public void PortRecorder_IsDeterministicAndComparable()
    {
        var script = InputScript.Parse(new StringReader("seed 7\nticks 50\n0 none\n5 right shot\n25 up\n"));
        var a = PortRecorder.Record(new Session(7), script);
        var b = PortRecorder.Record(new Session(7), script);
        Assert.Equal(50, a.Rows.Count);
        Assert.True(TraceCompare.Compare(a, b).Equal);

        // A different seed changes the state hash from the very first row.
        var c = PortRecorder.Record(new Session(8), script);
        var r = TraceCompare.Compare(a, c);
        Assert.Equal("0", r.FirstDivergentTick);
        Assert.Contains(r.Diffs, d => d.Column == "state_hash");
    }

    // Partial oracle (docs/ORACLE.md section 7): irand against values computed independently.
    // Borland C rand() with srand(1): first value 346. Source: the Borland LCG 22695477*s+1, >>16, & 0x7FFF.
    [Fact]
    public void IRand_FirstValueFromSeed1_Is346()
    {
        var rng = new IRand(1);
        Assert.Equal(346, rng.Next());
    }

    [Fact]
    public void IRand_MatchesIndependentLcgForManySeeds()
    {
        foreach (uint seed in new uint[] { 0, 1, 0xFFFFFFFF, 12345, 0x80000000 })
        {
            var rng = new IRand(seed);
            ulong s = seed;
            for (int i = 0; i < 1000; i++)
            {
                s = (s * 22695477UL + 1) & 0xFFFFFFFFUL;
                Assert.Equal((int)((s >> 16) & 0x7FFF), rng.Next());
            }
        }
    }

    [Fact]
    public void AlignByVsync_InsertsBlankRowsForBlockingIterations()
    {
        var t = new TraceTable(new[] { "tick", "z_vsync_count2", "score" });
        t.Rows.Add(new[] { "0", "65534", "1" });
        t.Rows.Add(new[] { "1", "65535", "2" });
        t.Rows.Add(new[] { "2", "2", "3" });        // wraps: 3 VSyncs in one iteration -> 2 blank rows
        var a = t.AlignByVsync();
        Assert.Equal(5, a.Rows.Count);
        Assert.Equal("", a.Get(2, "score"));
        Assert.Equal("", a.Get(3, "score"));
        Assert.Equal("3", a.Get(4, "score"));
        Assert.Equal("4", a.Get(4, "tick"));
    }
}
