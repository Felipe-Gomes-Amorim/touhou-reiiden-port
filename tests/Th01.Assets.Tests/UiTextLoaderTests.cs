using Th01.Assets.Text;
using Th01.Core.Game;

namespace Th01.Assets.Tests;

public class UiTextLoaderTests
{
    static byte[] Strings(params string[] items)
    {
        var bytes = new List<byte>();
        foreach (var s in items) { bytes.AddRange(TextCodec.Encode(s)); bytes.Add(0); }
        return bytes.ToArray();
    }

    /// <summary>A fake executable: code-like noise, then the two string tables in the layout the real one has.</summary>
    static byte[] FakeExe() => new byte[] { 1, 2, 3, 0, 0x90, 0x90, 0 }
        .Concat(Strings("ＲＯＵＮＤ　７　ＤＯＮＥ", "Ｗｅｌｌ！！", "Ｐｉｃｋ　ｏｎｅ", "魔界へ", "地獄へ"))
        .Concat(new byte[] { 0xCD, 0x21, 0 })
        // an unrelated "STAGE" string with another length must be skipped
        .Concat(Strings("ＳＴＡＧＥ"))
        .Concat(Strings("ＳＴＡＧＥ　　　　ＤＯＮＥＤＯＮＥ", "　　　　０　", "　　ＳＵＢ", "   　Ｔ　", "Ｃｏｍｂｏ", "Ｒｅｓ＆Ｒｅｓ", "　　ＳＴ", "ＴＯＴＡＬ", "Ｚ　Ｋｅｙ"))
        .ToArray();

    [Fact]
    public void Reads_the_route_menu_and_bonus_labels_from_the_executable()
    {
        var t = UiTextLoader.FromExe(FakeExe());
        Assert.Equal("ＲＯＵＮＤ　７　ＤＯＮＥ", t.RouteRound);
        Assert.Equal("Ｗｅｌｌ！！", t.RouteCongrats);
        Assert.Equal("Ｐｉｃｋ　ｏｎｅ", t.RouteSelect);
        Assert.Equal("魔界へ", t.RouteMakai);
        Assert.Equal("地獄へ", t.RouteJigoku);
        Assert.Equal("ＳＴＡＧＥ　　　　ＤＯＮＥＤＯＮＥ", t.BonusTitle);
        Assert.Equal("　　ＳＵＢ", t.BonusSubtitle);
        Assert.Equal("   　Ｔ　", t.BonusTime);
        Assert.Equal("Ｃｏｍｂｏ", t.BonusCardCombo);
        Assert.Equal("Ｒｅｓ＆Ｒｅｓ", t.BonusResources);
        Assert.Equal("　　ＳＴ", t.BonusStage);
        Assert.Equal("ＴＯＴＡＬ", t.BonusTotal);
        Assert.Equal("Ｚ　Ｋｅｙ", t.BonusHitKey);
        Assert.True(t.FullwidthNumerals);
    }

    [Fact]
    public void Keeps_the_defaults_when_the_tables_are_not_there()
    {
        Assert.Equal(UiTexts.Default, UiTextLoader.FromExe(new byte[] { 1, 2, 3, 4, 0, 5, 6 }));
        Assert.Equal(UiTexts.Default, UiTextLoader.FromExe(ReadOnlySpan<byte>.Empty));
    }

    [Fact]
    public void A_string_that_only_looks_like_the_start_of_a_table_is_ignored()
    {
        // "ROUND" with nothing after it, and a "STAGE" title of the wrong length, leave everything at the defaults.
        var exe = Strings("ＲＯＵＮＤ").Concat(Strings("ＳＴＡＧＥ　ＣＯＭ", "　　　　０　")).ToArray();
        var t = UiTextLoader.FromExe(exe);
        Assert.Equal(UiTexts.Default.BonusTitle, t.BonusTitle);
        Assert.False(t.FullwidthNumerals);
    }

    [Fact]
    public void A_missing_executable_gives_the_defaults()
    {
        string dir = Directory.CreateTempSubdirectory("th01-ui-").FullName;
        try { Assert.Equal(UiTexts.Default, UiTextLoader.Load(new FolderAssetSource(dir))); }
        finally { Directory.Delete(dir, true); }
    }

    [SkippableFact]
    public void RealData_JapaneseCopy_ProvidesTheOriginalLabels()
    {
        string? path = Th01.Assets.Config.GameDataConfig.Resolve();
        Skip.If(path is null || !Directory.Exists(path), "Game data not configured.");
        var t = UiTextLoader.Load(new FolderAssetSource(path!));
        Skip.If(!t.FullwidthNumerals, "This copy of the game does not have the Japanese string tables.");
        Assert.NotEqual(UiTexts.Default.RouteMakai, t.RouteMakai);
        Assert.Equal(34, TextCodec.Encode(t.BonusTitle).Length);   // 17 fullwidth characters
        Assert.All(new[] { t.RouteRound, t.RouteCongrats, t.RouteSelect, t.RouteMakai, t.RouteJigoku, t.BonusTime, t.BonusTotal, t.BonusHitKey },
            s => Assert.False(string.IsNullOrWhiteSpace(s)));
    }
}
