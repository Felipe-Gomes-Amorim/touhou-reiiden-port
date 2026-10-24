using Th01.Assets.Model;
using Th01.Core.Graphics;
using Th01.Core.Input;
using Th01.Platform.MonoGame;

namespace Th01.Platform.Tests;

/// <summary>The ending sequences (ReC98 th01/end/end.cpp) and the verdict screen.</summary>
public class EndingTests
{
    sealed class Images : IScreenImages, IVerdictSource, ICaptionSource
    {
        public readonly List<string> Loaded = new();
        public VerdictStrings? Verdict { get; set; } = new(
            "Title", "ver", "1.0", "Rank %s", "High %7lu", "Score %7lu", "Continues", "Shrine %3lu",
            ["Gate %3lu", "Vina %3lu", "Fallen %3lu"], ["Wayside %3lu", "Cesspool %3lu", "Quiet %3lu"], "Makai %5lu", "Hell %5lu", "Thanks", "Assessment");

        public bool TryLoad(string file, out ArtFrame image)
        {
            Loaded.Add(file);
            var px = new byte[640 * 400];
            Array.Fill(px, (byte)1);
            image = new ArtFrame(640, 400, px, Array.Empty<byte>(), new byte[48]);
            return true;
        }

        public IReadOnlyList<string> Lines(string scene) => Enumerable.Range(0, 14).Select(i => $"L{i}").ToList();
    }

    static List<string> Played(IReadOnlyList<SlideStep> steps)
    {
        var images = new Images();
        var show = new Slideshow(images, steps);
        int ticks = 0;
        while (!show.Finished && ticks < 40000) { show.Tick(); ticks++; }
        Assert.True(show.Finished, "the sequence must end");
        return images.Loaded;
    }

    [Fact]
    public void GoodEnding_Makai_ShowsTheMakaiPicturesAndBossSlides()
    {
        var files = Played(Cutscenes.Ending(0, 0));
        Assert.Equal(["ED1A.GRP", "ED1B.GRP", "ED1C.GRP", "ED1D.GRP", "ED1E.GRP", "ED3A.GRP", "ED5A.GRP", "ED5B.GRP", "ED5C.GRP", "ENDB_A.GRP"], files);
    }

    [Fact]
    public void GoodEnding_Jigoku_ShowsED3BAndTheJigokuSlides()
    {
        var files = Played(Cutscenes.Ending(1, 0));
        Assert.Equal(["ED1A.GRP", "ED1B.GRP", "ED1C.GRP", "ED1D.GRP", "ED1E.GRP", "ED3A.GRP", "ED3B.GRP", "ENDB_B.GRP"], files);
    }

    [Theory]
    [InlineData(0, true)]
    [InlineData(1, false)]
    public void BadEnding_AfterAnyContinue_ShowsED2A_AndED4AOnlyOnMakai(int route, bool ed4a)
    {
        var files = Played(Cutscenes.Ending(route, 2));
        Assert.Contains("ED2A.GRP", files);
        Assert.Equal(ed4a, files.Contains("ED4A.GRP"));
        Assert.DoesNotContain("ED3A.GRP", files);
        Assert.DoesNotContain("ENDB_A.GRP", files);
    }

    [Fact]
    public void EndingOne_FollowsTheOriginalCounts()
    {
        // ReC98: ED1C shows picture 0 and 1 six times, then 0 and 2; ED1D waits 100 iterations of 2 ticks for the blinks.
        var steps = Cutscenes.EndingOne();
        int c = steps.ToList().FindIndex(s => s is LoadPics { File: "ED1C.GRP" }) + 1;
        var sweep = steps.Skip(c).TakeWhile(s => s is not LoadPics).OfType<SetPic>().Select(p => p.Quarter).ToList();
        Assert.Equal([0, 1, 0, 1, 0, 1, 0, 1, 0, 1, 0, 1, 0, 2], sweep);
        int d = steps.ToList().FindIndex(s => s is LoadPics { File: "ED1D.GRP" }) + 1;
        var orb = steps.Skip(d).TakeWhile(s => s is not LoadPics).ToList();
        Assert.Equal(100, orb.OfType<Wait>().Count(w => w.Ticks == 2));
    }

    [Fact]
    public void JigokuBadEnding_Shakes_ThenSettlesAtRowZero()
    {
        var steps = Cutscenes.Bad(1);
        var scrolls = steps.OfType<Scroll>().ToList();
        Assert.Contains(scrolls, s => s.Y == 8);
        Assert.Equal(0, scrolls[^1].Y);
        // the shake is 65 + 17 and 33 + 33 iterations, then one reset
        Assert.Equal(65 + 17 + 33 + 33 + 1, scrolls.Count);
        Assert.Contains(steps, s => s is Caption { Scene: "ED2A.GRP", Count: 2 });
    }

    [Fact]
    public void Tones_AreConvertedFromTheOriginalScale()
    {
        Assert.Equal(-15, Cutscenes.ToneOf(0));
        Assert.Equal(0, Cutscenes.ToneOf(100));
        Assert.Equal(15, Cutscenes.ToneOf(200));
    }

    [Fact]
    public void Verdict_FillsTheRunsValues_RouteSpecificLines_AndTotals()
    {
        var v = new Images().Verdict!;
        var steps = VerdictScreen.Steps(v, "NORMAL", 12345, 54321, [1, 2, 3, 4], route: 1, BdfFont.LoadSpleen8x16());
        var texts = steps.OfType<PutText>().Select(t => t.Text).ToList();
        Assert.Contains(texts, t => t.StartsWith("Rank") && t.Contains("NORMAL"));
        Assert.Contains("High   54321", texts);
        Assert.Contains("Score   12345", texts);
        Assert.Contains("Shrine   1", texts);
        Assert.Contains("Wayside   2", texts);          // the Jigoku line of the first later scene
        Assert.Contains("Cesspool   3", texts);
        Assert.Contains("Quiet   4", texts);
        Assert.Contains("Hell    10", texts);           // total 1 + 2 + 3 + 4
        Assert.DoesNotContain(texts, t => t.StartsWith("Gate"));
        Assert.Contains("Thanks", texts);
        Assert.Contains(steps, s => s is LoadPics { File: "ENDM_A.GRP" });
        Assert.Contains(steps, s => s is ShowFull);
        // lines are 32 rows apart at x = 296: the rank is line 2
        var rank = steps.OfType<PutText>().First(t => t.Text.StartsWith("Rank"));
        Assert.Equal((296, 64), (rank.X, rank.Y));
    }

    [Fact]
    public void Flow_PlayingTheEnding_ReachesTheRanking()
    {
        var images = new Images();
        // startCutscene 7 = bad ending Jigoku (3 continues in scene 1), with the verdict
        var f = new GameFlow(new FlowFakeStages(), images, BdfFont.LoadSpleen8x16(), new GameSettings(), startCutscene: 7);
        Assert.Equal(Screen.Cutscene, f.Screen);
        for (int i = 0; i < 20000 && f.Screen == Screen.Cutscene; i++) f.Tick(GameButtons.None);
        Assert.Contains("ENDM_A.GRP", images.Loaded);
        Assert.Contains("ED2A.GRP", images.Loaded);
        Assert.DoesNotContain("ED3A.GRP", images.Loaded);
        Assert.True(f.Screen is Screen.Ranking or Screen.NameEntry, f.Screen.ToString());
    }

    [Fact]
    public void Flow_WithoutVerdictText_StillEndsWithTheRanking()
    {
        var images = new Images { Verdict = null };
        var f = new GameFlow(new FlowFakeStages(), images, BdfFont.LoadSpleen8x16(), new GameSettings(), startCutscene: 4);
        for (int i = 0; i < 20000 && f.Screen == Screen.Cutscene; i++) f.Tick(GameButtons.None);
        Assert.DoesNotContain("ENDM_A.GRP", images.Loaded);
        Assert.True(f.Screen is Screen.Ranking or Screen.NameEntry, f.Screen.ToString());
    }

    [Fact]
    public void Verdict_TheLabelDecidesWhichScoreGoesWhere()
    {
        // the English build prints "Score" on the code's first score line and "Hi Score" on the second
        var v = new Images().Verdict! with { HighScore = "Score %7lu", Score = "Hi Score %7lu" };
        var texts = VerdictScreen.Steps(v, "EASY", 100, 900, [0, 0, 0, 0], route: 0, BdfFont.LoadSpleen8x16()).OfType<PutText>().Select(t => t.Text).ToList();
        Assert.Contains("Score     100", texts);
        Assert.Contains("Hi Score     900", texts);
    }
}
