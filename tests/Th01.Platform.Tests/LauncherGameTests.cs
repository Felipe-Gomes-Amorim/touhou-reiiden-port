using Th01.Assets;
using Th01.Assets.Tests;
using Th01.Assets.Text;
using Th01.Platform.MonoGame.Launcher;

namespace Th01.Platform.Tests;

public class LauncherGameTests : IDisposable
{
    sealed class FakePrompt(string? answer) : IUserPrompt
    {
        public int Asked;
        public string? PickGameFile() { Asked++; return answer; }
        public void ShowError(string message) { }
    }

    readonly string _dir = Directory.CreateTempSubdirectory("th01-lg-").FullName;
    string UserDir => Path.Combine(_dir, "user");

    /// <summary>A folder GameDataCheck accepts: the four essential files, the archive with every expected entry.</summary>
    string MakeGameFolder(string name, bool translated = false, bool complete = true)
    {
        string dir = Path.Combine(_dir, name);
        Directory.CreateDirectory(dir);
        File.WriteAllBytes(Path.Combine(dir, "OP.EXE"), TitleTextLoaderTests.FakeExe());
        File.WriteAllBytes(Path.Combine(dir, "REIIDEN.EXE"), [1, 2, 3]);
        File.WriteAllBytes(Path.Combine(dir, "ST0.GRP"), [1]);
        if (complete)
            File.WriteAllBytes(Path.Combine(dir, ExpectedFiles.ArchiveName),
                PackBuilder.Build(ExpectedFiles.PackEntries.Select(n => (n, new byte[] { 1, 2, 3 }, false)).ToArray()));
        if (translated) File.WriteAllBytes(Path.Combine(dir, "ST7.MDT"), [1]);
        return dir;
    }

    LauncherGameHost Host(string? pick = null) => new(UserDir, new FakePrompt(pick), developerPath: () => null);

    [Fact]
    public void Startup_WithNothingRemembered_HasNoGame()
    {
        var host = Host();
        Assert.Null(host.Startup(null, null));
        Assert.Null(host.DataPath);
        Assert.False(host.Current.Valid);
    }

    [Fact]
    public void ChoosingAGame_AdoptsItWithItsEdition_AndTheNextRunRemembersIt()
    {
        string game = MakeGameFolder("jp");
        var host = Host(game);
        var choice = host.Choose();
        Assert.NotNull(choice?.Game);
        Assert.True(host.Current.Valid);
        Assert.Equal(GameVersion.Translated, host.Current.Version); // the fake texts are not full width: the checker says Translated
        Assert.Equal(Path.GetFullPath(game), host.Current.Origin);

        var next = Host();
        var again = next.Startup(null, null);
        Assert.True(again?.Game?.Valid);
        Assert.Equal(host.DataPath, next.DataPath);
        Assert.Equal(Path.GetFullPath(game), next.Current.Origin);
    }

    [Fact]
    public void TranslatedMarker_ShowsAsTranslated()
    {
        var host = Host();
        host.ChoosePath(MakeGameFolder("en", translated: true));
        Assert.Equal(GameVersion.Translated, host.Current.Version);
    }

    [Fact]
    public void ARefusedChoice_KeepsTheCurrentGame_AndCarriesTheReason()
    {
        var host = Host();
        host.ChoosePath(MakeGameFolder("good"));
        var before = host.Current;
        string? pathBefore = host.DataPath;

        var bad = host.ChoosePath(MakeGameFolder("broken", complete: false));
        Assert.Null(bad.Game);
        Assert.Equal(GameProblemCode.MissingFile, bad.ErrorCode);
        Assert.Equal(ExpectedFiles.ArchiveName, bad.ErrorFile);
        Assert.Equal(before, host.Current);
        Assert.Equal(pathBefore, host.DataPath);

        var missing = host.ChoosePath(Path.Combine(_dir, "nowhere"));
        Assert.Equal(GameProblemCode.NotFound, missing.ErrorCode);
    }

    [Fact]
    public void Cancelling_ChangesNothing()
    {
        var host = Host(null);
        var model = new LauncherModel(new LauncherSettings(), host.Current, "0.4.0");
        model.ApplyChoice(host.Choose());
        Assert.True(model.FirstRun);
        Assert.Null(model.Error);
    }

    [Fact]
    public void Model_AppliesChoices_GoodOnesEnablePlay_BadOnesShowAnError()
    {
        var host = Host();
        var model = new LauncherModel(new LauncherSettings(), LauncherGameInfo.None, "0.4.0");
        Assert.False(model.IsEnabled(HomeItem.Play));
        model.ApplyChoice(host.ChoosePath(MakeGameFolder("broken", complete: false)));
        Assert.NotNull(model.Error);
        Assert.True(model.FirstRun);
        model.ApplyChoice(host.ChoosePath(MakeGameFolder("good")));
        Assert.Null(model.Error);
        Assert.False(model.FirstRun);
        Assert.True(model.IsEnabled(HomeItem.Play));
        Assert.Equal(HomeItem.Play, model.FocusedItem); // the cursor goes to PLAY
    }

    [Fact]
    public void LauncherJson_KeepsLanguageAndRememberedGameTogether()
    {
        string settingsPath = Path.Combine(UserDir, LauncherSettings.FileName);
        var host = Host();
        host.ChoosePath(MakeGameFolder("good"));
        new LauncherSettings { Language = LauncherLanguage.Japanese }.Save(settingsPath); // after the game was remembered
        Assert.Equal(LauncherLanguage.Japanese, LauncherSettings.Load(settingsPath).Language);
        var next = Host();
        Assert.True(next.Startup(null, null)?.Game?.Valid);             // still remembered
        host.ChoosePath(MakeGameFolder("good2"));                        // remembering again must keep the language
        Assert.Equal(LauncherLanguage.Japanese, LauncherSettings.Load(settingsPath).Language);
    }

    [Fact]
    public void ErrorTexts_ExistInBothLanguages_WithGlyphs()
    {
        var font = JapaneseFont.CreateDefault();
        foreach (GameProblemCode code in Enum.GetValues<GameProblemCode>())
            foreach (var lang in new[] { LauncherLanguage.English, LauncherLanguage.Japanese })
            {
                var m = new LauncherModel(new LauncherSettings { Language = lang }, LauncherGameInfo.None, "0.4.0");
                string t = LauncherView.ErrorText(m, new GameError(code, "REIIDEN.DAT"));
                Assert.False(string.IsNullOrWhiteSpace(t));
                foreach (var r in t.EnumerateRunes()) Assert.True(font.TryGetGlyph(r.Value, out _), $"{code}/{lang}: U+{r.Value:X4}");
            }
    }

    [Fact]
    public void Panels_RenderForAllStates_InBothLanguages()
    {
        var font = JapaneseFont.CreateDefault();
        var canvas = new LauncherCanvas();
        var games = new[]
        {
            LauncherGameInfo.None,
            new LauncherGameInfo(@"C:\Games\Touhou\Reiiden\th01.hdi", true, GameVersion.Japanese),
            new LauncherGameInfo(new string('x', 200), true, GameVersion.Translated),
            new LauncherGameInfo("C:\\a", true, GameVersion.Unknown),
        };
        foreach (var lang in new[] { LauncherLanguage.English, LauncherLanguage.Japanese })
            foreach (var g in games)
                foreach (bool withError in new[] { false, true })
                {
                    var m = new LauncherModel(new LauncherSettings { Language = lang }, g, "0.4.0");
                    if (withError) m.ApplyChoice(new GameChoice(null, GameProblemCode.MissingFile, "REIIDEN.DAT"));
                    for (int i = 0; i < LauncherModel.HomeOrder.Length; i++) { LauncherView.Render(m, canvas, font); m.Press(LauncherKey.Down); }
                }
    }

    [SkippableFact]
    public void RealData_GameFolderIsValid_AndItsEditionIsShown()
    {
        string? data = Environment.GetEnvironmentVariable("TH01_DATA_PATH");
        Skip.If(string.IsNullOrEmpty(data) || !Directory.Exists(data), "TH01_DATA_PATH is not set");
        var host = Host();
        var choice = host.Startup(data, null);
        Assert.True(choice?.Game?.Valid);
        Assert.NotEqual(GameVersion.Unknown, host.Current.Version);
        Assert.Equal(data, host.Current.Origin);
    }

    [Fact]
    public void Ellipsize_FitsTheWidth()
    {
        var font = JapaneseFont.CreateDefault();
        string cut = LauncherView.Ellipsize(font, new string('a', 100), 200);
        Assert.True(LauncherCanvas.MeasureText(font, cut) <= 200);
        Assert.EndsWith("…", cut);
        Assert.Equal("short", LauncherView.Ellipsize(font, "short", 200));
    }

    /// <summary>LAUNCHER_DUMP=folder also writes the panel screens.</summary>
    [Fact]
    public void Dump_PanelScreens_WhenAsked()
    {
        string? dir = Environment.GetEnvironmentVariable("LAUNCHER_DUMP");
        if (string.IsNullOrEmpty(dir)) return;
        Directory.CreateDirectory(dir);
        var font = JapaneseFont.CreateDefault();
        foreach (var (lang, tag) in new[] { (LauncherLanguage.English, "en"), (LauncherLanguage.Japanese, "ja") })
        {
            var canvas = new LauncherCanvas();
            var home = new LauncherModel(new LauncherSettings { Language = lang }, new LauncherGameInfo(@"C:\Jogos\Touhou\Reiiden\th01.hdi", true, GameVersion.Japanese), "0.4.0");
            LauncherView.Render(home, canvas, font); LauncherTests.WritePng(Path.Combine(dir, $"p2-home-{tag}.png"), canvas);
            var first = new LauncherModel(new LauncherSettings { Language = lang }, LauncherGameInfo.None, "0.4.0");
            first.Press(LauncherKey.Up);
            first.ApplyChoice(new GameChoice(null, GameProblemCode.NotFound));
            LauncherView.Render(first, canvas, font); LauncherTests.WritePng(Path.Combine(dir, $"p2-first-{tag}.png"), canvas);
        }
    }

    public void Dispose()
    {
        try { Directory.Delete(_dir, true); } catch (IOException) { }
    }
}
