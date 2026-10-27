using Th01.Assets.Text;
using Th01.Platform.MonoGame.Launcher;

namespace Th01.Platform.Tests;

public class LauncherMouseTests
{
    static readonly Th01.Core.Graphics.IFontSource Font = JapaneseFont.CreateDefault();
    static readonly LauncherGameInfo Valid = new("C:\\g", true, GameVersion.Japanese);

    sealed class Rig
    {
        public readonly LauncherModel Model;
        public readonly LauncherHitMap Hits = new();
        readonly LauncherCanvas _canvas = new();

        public Rig(LauncherGameInfo? game = null, LauncherSettings? settings = null)
        {
            Model = new LauncherModel(settings ?? new LauncherSettings(), game ?? Valid, "0.4.0");
            Draw();
        }

        public void Draw() => LauncherView.Render(Model, _canvas, Font, Hits);

        /// <summary>A point inside the first region of this meaning, found by scanning what was drawn (no layout numbers in the tests).</summary>
        public (int X, int Y) Find(HitKind kind, int a = 0, int b = 0)
        {
            for (int y = 0; y < LauncherCanvas.Height; y += 2)
                for (int x = 0; x < LauncherCanvas.Width; x += 2)
                    if (Hits.At(x, y) is { } t && t.Kind == kind && t.A == a && t.B == b) return (x, y);
            throw new InvalidOperationException($"nothing on screen means {kind} {a} {b}");
        }

        public HitTarget? At((int X, int Y) p) => Hits.At(p.X, p.Y);

        public LauncherResult Click(HitKind kind, int a = 0, int b = 0, bool right = false)
        {
            var r = Model.Click(At(Find(kind, a, b)), right);
            Draw();
            return r;
        }

        public void Hover(HitKind kind, int a = 0, int b = 0) { Model.Hover(At(Find(kind, a, b))); Draw(); }

        public void ToOptions(OptionsTab tab = OptionsTab.Accessibility)
        {
            if (Model.Screen == LauncherScreen.Home) Click(HitKind.HomeItem, (int)HomeItem.Options);
            Click(HitKind.Tab, (int)tab);
        }
    }

    // ---- coordinates -----------------------------------------------------------------------------------------------------

    [Fact]
    public void Viewport_FitsTheLargestCenteredBox()
    {
        Assert.Equal((0, 0, 1280, 720), LauncherViewport.Fit(1280, 720));
        Assert.Equal((0, 0, 1920, 1080), LauncherViewport.Fit(1920, 1080));
        Assert.Equal((0, 40, 1280, 720), LauncherViewport.Fit(1280, 800));          // 2x window of the game: bars above and below
        Assert.Equal((0, 140, 1920, 1080), LauncherViewport.Fit(1920, 1360));
        var (x, y, w, h) = LauncherViewport.Fit(2560, 1080);                          // wide screen: bars at the sides
        Assert.Equal(1080, h);
        Assert.Equal(1920, w);
        Assert.Equal(320, x);
        Assert.Equal(0, y);
    }

    [Theory]
    [InlineData(1280, 720, 640, 360, 640, 360)]
    [InlineData(1280, 800, 640, 400, 640, 360)]        // letterboxed: window centre is the picture centre
    [InlineData(1280, 800, 0, 40, 0, 0)]               // first picture pixel
    [InlineData(1920, 1080, 960, 540, 640, 360)]       // 1.5x
    [InlineData(1920, 1080, 1500, 300, 1000, 200)]
    [InlineData(2560, 1080, 1280, 540, 640, 360)]      // side bars
    [InlineData(2560, 1440, 2559, 1439, 1279, 719)]    // exactly 2x, last pixel
    public void Viewport_ToCanvas_UndoesLetterboxAndScale(int ww, int wh, int mx, int my, int cx, int cy)
    {
        var p = LauncherViewport.ToCanvas(mx, my, ww, wh);
        Assert.NotNull(p);
        Assert.Equal((cx, cy), p!.Value);
    }

    [Theory]
    [InlineData(1280, 800, 10, 39)]       // bar above
    [InlineData(1280, 800, 10, 760)]      // bar below
    [InlineData(2560, 1080, 100, 500)]    // left bar
    [InlineData(2560, 1080, 2500, 500)]   // right bar
    [InlineData(1280, 720, -1, 5)]
    [InlineData(1280, 720, 1280, 5)]
    public void Viewport_ToCanvas_IsNullOverTheBars(int ww, int wh, int mx, int my) =>
        Assert.Null(LauncherViewport.ToCanvas(mx, my, ww, wh));

    // ---- hit-testing every kind of item ----------------------------------------------------------------------------------

    [Fact]
    public void Home_EveryItemHasAHitRegion_AndFooterWidgetsToo()
    {
        var r = new Rig();
        foreach (var item in new[] { HomeItem.Play, HomeItem.Options, HomeItem.Quit, HomeItem.Change, HomeItem.About })
            Assert.Equal(new HitTarget(HitKind.HomeItem, (int)item), r.At(r.Find(HitKind.HomeItem, (int)item)));
        Assert.NotNull(r.At(r.Find(HitKind.Language, 0)));
        Assert.NotNull(r.At(r.Find(HitKind.Language, 1)));
        Assert.Null(r.Hits.At(5, 5));                                     // empty corner
        Assert.Null(r.Hits.At(700, 120));
    }

    [Fact]
    public void Home_HoverFocusesTheItem_AndIgnoresTheSwitchedOffPlay()
    {
        var r = new Rig();
        r.Hover(HitKind.HomeItem, (int)HomeItem.Quit);
        Assert.Equal(HomeItem.Quit, r.Model.FocusedItem);
        r.Hover(HitKind.HomeItem, (int)HomeItem.About);
        Assert.Equal(HomeItem.About, r.Model.FocusedItem);
        r.Hover(HitKind.Language, 1);
        Assert.Equal(HomeItem.Language, r.Model.FocusedItem);
        r.Hover(HitKind.HomeItem, (int)HomeItem.Change);
        Assert.Equal(HomeItem.Change, r.Model.FocusedItem);

        var first = new Rig(LauncherGameInfo.None);
        first.Hover(HitKind.HomeItem, (int)HomeItem.Play);
        Assert.NotEqual(HomeItem.Play, first.Model.FocusedItem);
        Assert.Equal(LauncherResult.None, first.Click(HitKind.HomeItem, (int)HomeItem.Play));
    }

    [Fact]
    public void Home_LeftClickActsLikeSelect()
    {
        var r = new Rig();
                Assert.Equal(LauncherResult.ChooseGame, r.Click(HitKind.HomeItem, (int)HomeItem.Change));
        Assert.Equal(LauncherResult.Quit, r.Click(HitKind.HomeItem, (int)HomeItem.Quit));
        Assert.Equal(LauncherResult.Play, r.Click(HitKind.HomeItem, (int)HomeItem.Play));
        Assert.Equal(LauncherResult.None, r.Click(HitKind.HomeItem, (int)HomeItem.Options));
        Assert.Equal(LauncherScreen.Options, r.Model.Screen);
        r.Click(HitKind.Back);
        Assert.Equal(LauncherScreen.Home, r.Model.Screen);
        r.Click(HitKind.HomeItem, (int)HomeItem.About);
        Assert.Equal(LauncherScreen.About, r.Model.Screen);
        r.Click(HitKind.Back);
        Assert.Equal(LauncherScreen.Home, r.Model.Screen);
    }

    [Fact]
    public void Home_ClickOnTheLanguageSegments_SetsThatLanguage_NotJustToggles()
    {
        var saved = new List<LauncherLanguage>();
        var settings = new LauncherSettings();
        var m = new LauncherModel(settings, Valid, "0.4.0", s => saved.Add(s.Language));
        var hits = new LauncherHitMap();
        var canvas = new LauncherCanvas();
        LauncherView.Render(m, canvas, Font, hits);
        var rig = new Rig(settings: settings);
        rig.Click(HitKind.Language, 1);
        Assert.Equal(LauncherLanguage.Japanese, rig.Model.Language);
        rig.Click(HitKind.Language, 1);
        Assert.Equal(LauncherLanguage.Japanese, rig.Model.Language);   // clicking the active one keeps it
        rig.Click(HitKind.Language, 0);
        Assert.Equal(LauncherLanguage.English, rig.Model.Language);
        Assert.Equal(HomeItem.Language, rig.Model.FocusedItem);
    }

    [Fact]
    public void Home_RightClickAndClicksOnNothingDoNothing()
    {
        var r = new Rig();
        r.Model.Hover(r.At(r.Find(HitKind.HomeItem, (int)HomeItem.Quit)));
        Assert.Equal(LauncherResult.None, r.Model.Click(null, false));
        Assert.Equal(LauncherResult.None, r.Model.Click(null, true));
        Assert.Equal(LauncherResult.None, r.Click(HitKind.HomeItem, (int)HomeItem.Quit, right: true));
        Assert.Equal(LauncherScreen.Home, r.Model.Screen);
    }

    [Fact]
    public void Options_TabsRowsAndBackAreClickable_RightClickGoesBack()
    {
        var r = new Rig();
        r.Click(HitKind.HomeItem, (int)HomeItem.Options);
        for (int i = 0; i < 8; i++) Assert.NotNull(r.At(r.Find(HitKind.Tab, i)));
        r.Click(HitKind.Tab, (int)OptionsTab.Video);
        Assert.Equal(OptionsTab.Video, r.Model.Tab);
        Assert.Equal(0, r.Model.Row);

        r.Hover(HitKind.Row, 2);                         // integer scale
        Assert.Equal(2, r.Model.Row);
        bool before = r.Model.Settings.IntegerScale;
        r.Click(HitKind.Row, 2);
        Assert.Equal(!before, r.Model.Settings.IntegerScale);
        int scale = r.Model.Settings.WindowScale;
        r.Click(HitKind.Row, 1);                         // a choice cycles forward
        Assert.Equal((scale + 1) % 3, r.Model.Settings.WindowScale);

        r.Click(HitKind.Row, 0, right: true);            // right click: back
        Assert.Equal(LauncherScreen.Home, r.Model.Screen);
    }

    [Fact]
    public void Options_HoveringABarCellFocusesTheRow_AndClickingSetsTheLevel()
    {
        var r = new Rig();
        r.ToOptions(OptionsTab.Audio);
        r.Hover(HitKind.BarCell, 1, 5);
        Assert.Equal(1, r.Model.Row);
        r.Click(HitKind.BarCell, 0, 3);
        Assert.Equal(3, r.Model.Settings.MusicVolume);
        Assert.Equal(10, r.Model.Settings.EffectsVolume);
        r.Click(HitKind.BarCell, 1, 7);
        Assert.Equal(7, r.Model.Settings.EffectsVolume);
        Assert.Equal(3, r.Model.Settings.MusicVolume);
        int rev = r.Model.Revision;
        r.Click(HitKind.BarCell, 0, 10);
        Assert.Equal(10, r.Model.Settings.MusicVolume);
        Assert.True(r.Model.Revision > rev);
        Assert.Equal(0.7f, r.Model.Settings.EffectsLevel, 3);
    }

    [Fact]
    public void Options_ButtonsAndInfoRows()
    {
        var r = new Rig();
        r.ToOptions(OptionsTab.Data);
        r.Click(HitKind.Row, 1);                         // restore: asks first
        Assert.Equal(r.Model.Text(Str.BtnConfirm), r.Model.FocusedRow.Button);
        r.Hover(HitKind.Row, 0);                         // moving over another row cancels the question
        Assert.Equal(r.Model.Text(Str.BtnRestore), r.Model.Rows[1].Button);
        r.ToOptions(OptionsTab.Game);
        int rev = r.Model.Revision;
        r.Click(HitKind.Row, 0);                         // read-only info: nothing happens
        Assert.Equal(rev, r.Model.Revision);
    }

    [Fact]
    public void Options_WheelScrollsTheListAndKeepsTheFocusInside()
    {
        var r = new Rig();
        r.ToOptions(OptionsTab.Controls);                // 9 rows, 6 visible
        Assert.Equal(0, r.Model.RowScroll);
        r.Model.Scroll(1);
        Assert.Equal(1, r.Model.RowScroll);
        Assert.Equal(1, r.Model.Row);                    // the focused row was above the window: pulled in
        r.Model.Scroll(10);
        Assert.Equal(3, r.Model.RowScroll);              // 9 - 6
        Assert.Equal(3, r.Model.Row);
        r.Model.Scroll(-1);
        Assert.Equal(2, r.Model.RowScroll);
        Assert.True(r.Model.Row < r.Model.RowScroll + LauncherModel.VisibleRows);
        r.Model.Scroll(-10);
        Assert.Equal(0, r.Model.RowScroll);
        r.Draw();
        var visible = r.Find(HitKind.Row, 5);
        Assert.NotEqual(default, visible);
        Assert.Throws<InvalidOperationException>(() => r.Find(HitKind.Row, 6));   // not on screen: no hit region
    }

    [Fact]
    public void Options_ScrollIsInertOnShortTabsAndOtherScreens()
    {
        var r = new Rig();
        r.Model.Scroll(3);
        Assert.Equal(0, r.Model.RowScroll);
        r.ToOptions(OptionsTab.Audio);
        r.Model.Scroll(3);
        Assert.Equal(0, r.Model.RowScroll);
    }

    [Fact]
    public void Report_AnyClickClosesIt_AndRemapIgnoresLeftClicks()
    {
        var r = new Rig();
        r.ToOptions(OptionsTab.Diagnostics);
        var svc = new FakeReportServices();
        r.Model.Attach(svc, null, null);
        r.Click(HitKind.Row, 1);
        Assert.True(r.Model.ShowingReport);
        r.Hover(HitKind.Row, 0);
        Assert.Equal(1, r.Model.Row);                    // hover is ignored while the report is open
        Assert.Equal(LauncherResult.None, r.Model.Click(null, false));
        Assert.False(r.Model.ShowingReport);

        var b = new Th01.Platform.MonoGame.InputBindings();
        r.ToOptions(OptionsTab.Controls);
        r.Model.Attach(svc, b, null);
        r.Draw();
        r.Click(HitKind.Row, 1);                         // shot: starts listening
        Assert.True(r.Model.Listening);
        r.Click(HitKind.Row, 2);                         // left click while listening: ignored
        r.Hover(HitKind.Row, 3);
        Assert.True(r.Model.Listening);
        Assert.Equal(1, r.Model.Row);
        r.Click(HitKind.Row, 0, right: true);            // right click cancels
        Assert.False(r.Model.Listening);
        Assert.Equal(LauncherScreen.Options, r.Model.Screen);
    }

    sealed class FakeReportServices : ILauncherServices
    {
        public bool GamepadConnected => false;
        public string? GamepadName => null;
        public ReportSummary Summary() => ReportSummary.Empty;
        public void RefreshReport() { }
        public void PreviewEffect() { }
        public FontRomState FontRomStatus => FontRomState.NotFound;
        public void OpenDataFolder() { }
        public IReadOnlyList<string> ReportLines() => ["missing art: x"];
        public string? PickFontRom() => null;
    }

    [Fact]
    public void About_BackButtonIsClickable()
    {
        var r = new Rig();
        r.Click(HitKind.HomeItem, (int)HomeItem.About);
        Assert.Equal(LauncherScreen.About, r.Model.Screen);
        Assert.Equal(1, r.Hits.Count);                   // only the back button is a target there
        r.Click(HitKind.Back);
        Assert.Equal(LauncherScreen.Home, r.Model.Screen);
    }

    [Fact]
    public void HitRegions_FollowThePicture_NotAStaleScreen()
    {
        var r = new Rig();
        int home = r.Hits.Count;
        r.Click(HitKind.HomeItem, (int)HomeItem.Options);
        Assert.NotEqual(home, r.Hits.Count);
        Assert.Throws<InvalidOperationException>(() => r.Find(HitKind.HomeItem, (int)HomeItem.Quit));   // menu is gone from the options
    }
}
