using Th01.Core.Game;

namespace Th01.Core.Tests.Game;

public class TitleTextsTests
{
    [Fact]
    public void DefaultHasTheExpectedShape()
    {
        var t = TitleTexts.Default;
        Assert.Equal(TitleTexts.MainChoiceCount, t.MainChoices.Length);
        Assert.Equal(TitleTexts.OptionChoiceCount, t.OptionChoices.Length);
        Assert.Equal(TitleTexts.RankCount, t.Ranks.Length);
        Assert.Equal(TitleTexts.BgmModeCount, t.BgmModes.Length);
        Assert.Equal(TitleTexts.MusicChoiceCount, t.MusicChoices.Length);
        Assert.Equal(TitleTexts.MusicTrackCount, t.MusicTitles.Length);
        Assert.Equal(TitleTexts.MusicTrackCount, t.MusicFiles.Length);
        Assert.All(t.MusicFiles, f => Assert.EndsWith(".mdt", f, StringComparison.OrdinalIgnoreCase));
        Assert.False(t.FullwidthText);
        Assert.Equal("START", t.MainChoices[0]);
        Assert.Equal("LUNATIC", t.Ranks[3]);
    }
}
