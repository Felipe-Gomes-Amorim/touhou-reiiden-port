using Microsoft.Xna.Framework.Input;
using Th01.Assets.Text;
using Th01.Core.Game;
using Th01.Core.Graphics;
using Th01.Core.Input;
using Th01.Platform.MonoGame;
using Th01.Platform.MonoGame.Launcher;

namespace Th01.Platform.Tests;

/// <summary>One test per behaviour behind the options rows: each row is wired to something real, at once or at the next start.</summary>
public class LauncherAuditTests : IDisposable
{
    readonly string _dir = Directory.CreateTempSubdirectory("th01-audit-").FullName;
    string Path2(string name) => Path.Combine(_dir, name);

    sealed class FakeSurface : ILauncherSurface
    {
        public bool IsFullscreen { get; set; }
        public List<string> Calls = [];
        public (int W, int H)? Window;
        public bool Integer, Numpad, Record;
        public int Filter;
        public float Music = -1, Effects = -1;
        public (string? Rom, bool Prefer, string? Folder)? Fonts;
        public void SetFullscreen(bool on) { IsFullscreen = on; Calls.Add("fullscreen " + on); }
        public void SetWindowClientSize(int w, int h) { Window = (w, h); Calls.Add($"window {w}x{h}"); }
        public void SetIntegerScale(bool on) => Integer = on;
        public void SetFilter(int f) => Filter = f;
        public void SetVolumes(float m, float e) { Music = m; Effects = e; }
        public void SetNumpad(bool on) => Numpad = on;
        public void ConfigureFonts(string? r, bool p, string? f) => Fonts = (r, p, f);
        public void SetRecordMissingArt(bool on) => Record = on;
    }

    sealed class FakeServices : ILauncherServices
    {
        public bool GamepadConnected { get; set; }
        public string? GamepadName { get; set; }
        public FontRomState FontRomStatus { get; set; }
        public int Opened, Previews, Refreshes;
        public string[] Report = [];
        public string? Picked;
        public void OpenDataFolder() => Opened++;
        public IReadOnlyList<string> ReportLines() => Report;
        public ReportSummary Summary() => ReportSummary.FromLines(Report);
        public void RefreshReport() => Refreshes++;
        public void PreviewEffect() => Previews++;
        public string? PickFontRom() => Picked;
    }

    readonly FakeServices _svc = new();

    LauncherModel Open(OptionsTab tab, LauncherSettings? s = null, InputBindings? b = null, GameSettings? game = null)
    {
        var m = new LauncherModel(s ?? new LauncherSettings(), new LauncherGameInfo("C:\\g", true, GameVersion.Japanese), "0.4.0");
        m.Attach(_svc, b, () => game ?? new GameSettings());
        m.Press(LauncherKey.Down); m.Press(LauncherKey.Select);
        while (m.Tab != tab) m.Press(LauncherKey.TabNext);
        return m;
    }

    InputBindings Bindings()
    {
        File.WriteAllText(Path2(InputBindings.FileName), "{}");
        return InputBindings.Load(_dir);
    }

    static void Down(LauncherModel m, int n) { for (int i = 0; i < n; i++) m.Press(LauncherKey.Down); }

    // ---- the audit table: every row of every tab names what it is wired to ------------------------------------------------

    static readonly Dictionary<RowId, string> Audit = new()
    {
        [RowId.ReduceAll] = "flow: flash (bomb + boss), shake, title lines  [next start of the game]",
        [RowId.SoftFlash] = "flow: VisualOptions.BombFlash + SoftFlashes  [next start]",
        [RowId.ReduceShake] = "flow: VisualOptions.ScreenShake, GameFlow miss shake, cutscene shake  [next start]",
        [RowId.NoLines] = "flow: TitleScene white lines  [next start]",
        [RowId.Warning] = "flow: warning screen before the logo  [next start]",
        [RowId.Fullscreen] = "window  [at once]",
        [RowId.WindowSize] = "window client size  [at once, while windowed; after leaving fullscreen otherwise]",
        [RowId.IntegerScale] = "game view scale mode  [at once]",
        [RowId.Filter] = "presentation filter, game and launcher  [at once]",
        [RowId.Music] = "AudioMixer.MusicLevel  [at once, also in the title]",
        [RowId.Effects] = "AudioMixer.EffectLevel + preview sample  [at once]",
        [RowId.Language] = "launcher text  [at once]",
        [RowId.Font] = "LauncherFonts (launcher and game)  [at once]",
        [RowId.FontRom] = "status of the ROM search  [at once]",
        [RowId.LocateFontRom] = "file dialog, path saved, ROM re-read  [at once]",
        [RowId.Move] = "InputBindings, saved to input.local.json  [at once]",
        [RowId.Shot] = "InputBindings  [at once]",
        [RowId.Bomb] = "InputBindings  [at once]",
        [RowId.Pause] = "InputBindings  [at once]",
        [RowId.Numpad] = "InputBindings.Numpad  [at once]",
        [RowId.PadShot] = "InputBindings.Gamepad  [at once]",
        [RowId.PadBomb] = "InputBindings.Gamepad  [at once]",
        [RowId.PadPause] = "InputBindings.Gamepad  [at once]",
        [RowId.Gamepad] = "detection and name of the pad  [live]",
        [RowId.Rank] = "read only: settings.local.json, else the player's REIIDEN.CFG",
        [RowId.Lives] = "read only: same source",
        [RowId.Bombs] = "read only: same source",
        [RowId.OpenFolder] = "file explorer on the user data folder",
        [RowId.Restore] = "launcher settings + key map back to defaults",
        [RowId.RecordArt] = "MissingArtLog.Recording and the file written at exit  [at once]",
        [RowId.Report] = "report view of crash log + missing art",
        [RowId.LastReport] = "real counts from the report files  [live]",
    };

    [Fact]
    public void EveryRow_IsInTheAuditTable_AndEveryAuditEntryIsARealRow()
    {
        var shown = new HashSet<RowId>();
        foreach (OptionsTab tab in Enum.GetValues<OptionsTab>())
            foreach (var row in Open(tab, b: Bindings()).Rows) shown.Add(row.Id);
        Assert.Equal(Enum.GetValues<RowId>().Order(), shown.Order());   // no row exists only in the enum, none is hidden
        Assert.Equal(shown.Order(), Audit.Keys.Order());
    }

    // ---- window, scale, filter, volumes: the applier ----------------------------------------------------------------------

    [Fact]
    public void Applier_PutsEverythingToWork_AndOnlyWhenTheRevisionIsNew()
    {
        var s = new LauncherSettings { Fullscreen = false, WindowScale = 2, IntegerScale = false, Filter = 2, MusicVolume = 4, EffectsVolume = 6, Numpad = false, RecordMissingArt = false, PreferFontRom = false, FontRomPath = "X" };
        var surf = new FakeSurface();
        var applier = new LauncherApplier(surf);
        Assert.True(applier.Apply(s, 1, "G"));
        Assert.Equal((1920, 1200), surf.Window);
        Assert.False(surf.Integer);
        Assert.Equal(2, surf.Filter);
        Assert.Equal(0.4f, surf.Music, 3);
        Assert.Equal(0.6f, surf.Effects, 3);
        Assert.False(surf.Numpad);
        Assert.False(surf.Record);
        Assert.Equal(("X", false, "G"), surf.Fonts);
        s.MusicVolume = 9;
        Assert.False(applier.Apply(s, 1, "G"));                // same revision: nothing
        Assert.Equal(0.4f, surf.Music, 3);
        Assert.True(applier.Apply(s, 2, "G"));
        Assert.Equal(0.9f, surf.Music, 3);
    }

    [Fact]
    public void Fullscreen_IsAppliedAtOnce_AndTheWindowSizeWaitsForWindowedMode()
    {
        var s = new LauncherSettings { WindowScale = 1 };
        var surf = new FakeSurface();
        var applier = new LauncherApplier(surf);
        applier.Apply(s, 1, null);
        Assert.Equal((1280, 800), surf.Window);

        s.Fullscreen = true; s.WindowScale = 2;                 // both during one change: fullscreen wins, the size is kept for later
        applier.Apply(s, 2, null);
        Assert.True(surf.IsFullscreen);
        Assert.Equal((1280, 800), surf.Window);                 // not resized while fullscreen
        s.Fullscreen = false;
        applier.Apply(s, 3, null);
        Assert.False(surf.IsFullscreen);
        Assert.Equal((1920, 1200), surf.Window);                // resized as soon as it is windowed again

        surf.Calls.Clear();
        s.WindowScale = 2;
        applier.Apply(s, 4, null);
        Assert.Empty(surf.Calls);                               // nothing changed: the window is left alone
    }

    [Fact]
    public void VideoRows_DriveTheSurface_ThroughTheModelRevision()
    {
        var m = Open(OptionsTab.Video);
        var surf = new FakeSurface();
        var applier = new LauncherApplier(surf);
        applier.Apply(m.Settings, m.Revision, null);
        m.Press(LauncherKey.Right);                              // fullscreen on
        applier.Apply(m.Settings, m.Revision, null);
        Assert.True(surf.IsFullscreen);
        m.Press(LauncherKey.Down); m.Press(LauncherKey.Select);  // window size 3x (while fullscreen: waits)
        m.Press(LauncherKey.Down); m.Press(LauncherKey.Select);  // integer scale off
        m.Press(LauncherKey.Down); m.Press(LauncherKey.Select);  // filter: smooth
        applier.Apply(m.Settings, m.Revision, null);
        Assert.False(surf.Integer);
        Assert.Equal(1, surf.Filter);
        m.Press(LauncherKey.Up); m.Press(LauncherKey.Up); m.Press(LauncherKey.Up);
        m.Press(LauncherKey.Left);                               // fullscreen off
        applier.Apply(m.Settings, m.Revision, null);
        Assert.Equal((1920, 1200), surf.Window);
    }

    [Fact]
    public void F11_KeepsTheSettingInStep_SoNothingTogglesBack()
    {
        var m = Open(OptionsTab.Video);
        var surf = new FakeSurface { IsFullscreen = true };      // the player pressed F11
        var applier = new LauncherApplier(surf);
        m.SetFullscreen(true);
        applier.Apply(m.Settings, m.Revision, null);
        Assert.DoesNotContain("fullscreen False", surf.Calls);
        Assert.True(surf.IsFullscreen);
        Assert.True(m.Settings.Fullscreen);
    }

    [Fact]
    public void Volumes_ApplyThroughTheMixer_AndEffectsPlayAPreview()
    {
        var m = Open(OptionsTab.Audio);
        var surf = new FakeSurface();
        var applier = new LauncherApplier(surf);
        m.Press(LauncherKey.Left);                               // music 9
        applier.Apply(m.Settings, m.Revision, null);
        Assert.Equal(0.9f, surf.Music, 3);
        Assert.Equal(0, _svc.Previews);                          // nothing to preview for music
        m.Press(LauncherKey.Down); m.Press(LauncherKey.Left);    // effects 9
        applier.Apply(m.Settings, m.Revision, null);
        Assert.Equal(0.9f, surf.Effects, 3);
        Assert.Equal(1, _svc.Previews);
        for (int i = 0; i < 12; i++) m.Press(LauncherKey.Left);  // down to 0: silent, no preview at 0
        Assert.Equal(0, m.Settings.EffectsVolume);
        Assert.Equal(9, _svc.Previews);                          // 9..1 played, 0 did not
        m.SetVolume(RowId.Effects, 5);                           // the mouse path uses the same call
        Assert.Equal(10, _svc.Previews);
        var mixer = new Th01.Audio.Playback.AudioMixer();
        mixer.MusicLevel = m.Settings.MusicLevel; mixer.EffectLevel = m.Settings.EffectsLevel;
        Assert.Equal(0.9f, mixer.MusicLevel, 3);
        Assert.Equal(0.5f, mixer.EffectLevel, 3);
    }

    // ---- accessibility and the flow ---------------------------------------------------------------------------------------

    [Fact]
    public void FlowOptions_FollowTheAccessibilityRows()
    {
        var d = new LauncherSettings().ToFlowOptions(startsAtLogo: true);
        Assert.True(d.ShowWarning); Assert.True(d.ScreenShake); Assert.True(d.TitleLines);
        Assert.Equal(BombFlash.Softened, d.Visuals.BombFlash);
        Assert.True(d.Visuals.ScreenShake);

        var s = new LauncherSettings { PhotosensitivityWarning = false };
        Assert.False(s.ToFlowOptions(true).ShowWarning);
        Assert.False(new LauncherSettings().ToFlowOptions(startsAtLogo: false).ShowWarning);   // --stage / --cutscene

        var m = Open(OptionsTab.Accessibility);
        m.Press(LauncherKey.Select);                             // reduce all
        var r = m.Settings.ToFlowOptions(true);
        Assert.False(r.ScreenShake); Assert.False(r.TitleLines);
        Assert.False(r.Visuals.ScreenShake);
        m.Press(LauncherKey.Down); m.Press(LauncherKey.Select);  // flash back to original
        Assert.Equal(BombFlash.Original, m.Settings.ToFlowOptions(true).Visuals.BombFlash);
    }

    // ---- controls: real, saved, restored ----------------------------------------------------------------------------------

    [Fact]
    public void Controls_ShowTheRealBindings_SavedToDisk_AndRestoredByRestoreDefaults()
    {
        var b = Bindings();
        var m = Open(OptionsTab.Controls, b: b);
        Assert.Equal(["←", "↑", "→", "↓"], m.Rows[0].Keys);
        Assert.Equal("Z", m.Rows[1].Keys![0]);                   // defaults as the program has them
        m.Press(LauncherKey.Down); m.Press(LauncherKey.Select); m.OfferKey(Keys.K);
        Down(m, 4);                                              // gamepad shot
        m.Press(LauncherKey.Select); m.OfferButton(Buttons.Y);

        var reloaded = InputBindings.Load(_dir);                 // what the next start reads
        Assert.Equal([Keys.K], reloaded.Keyboard[GameButtons.Shot]);
        Assert.Equal([Buttons.Y], reloaded.Gamepad[GameButtons.Shot]);

        var d = Open(OptionsTab.Data, b: b);                     // restore defaults (two presses)
        d.Press(LauncherKey.Down); d.Press(LauncherKey.Select); d.Press(LauncherKey.Select);
        Assert.Equal(new InputBindings().Keyboard[GameButtons.Shot], b.Keyboard[GameButtons.Shot]);
        var again = InputBindings.Load(_dir);
        Assert.Equal(new InputBindings().Keyboard[GameButtons.Shot], again.Keyboard[GameButtons.Shot]);
        Assert.Equal(new InputBindings().Gamepad[GameButtons.Shot], again.Gamepad[GameButtons.Shot]);
    }

    [Fact]
    public void Gamepad_ShowsNoneConnectedOrTheNameOfTheController()
    {
        var m = Open(OptionsTab.Controls, b: Bindings());
        Down(m, 8);
        Assert.Equal(RowId.Gamepad, m.FocusedRow.Id);
        Assert.Equal("None detected", m.FocusedRow.Value);
        Assert.False(m.FocusedRow.Good);
        _svc.GamepadConnected = true;
        Assert.Equal("Connected", m.FocusedRow.Value);           // connected but nameless
        _svc.GamepadName = "  Xbox 360 Controller ";
        Assert.Equal("Xbox 360 Controller", m.FocusedRow.Value);
        Assert.True(m.FocusedRow.Good);
        _svc.GamepadConnected = false;
        Assert.Equal("None detected", m.FocusedRow.Value);       // unplugged: gone at once
    }

    // ---- game tab: the player's own REIIDEN.CFG ---------------------------------------------------------------------------

    [Fact]
    public void GameSettings_StartFromTheReiidenCfg_UntilTheTitleMenuSavesTheirOwn()
    {
        string game = Path2("game"); Directory.CreateDirectory(game);
        File.WriteAllBytes(Path.Combine(game, "REIIDEN.CFG"), Th01.Assets.Formats.ReiidenCfg.Serialize(new Th01.Assets.Formats.GameOptions(2, 0, 3, 4)));
        string settings = Path2("settings.local.json");

        var s = GameSettings.Load(settings, game);
        Assert.Equal(Rank.Hard, s.Rank);
        Assert.False(s.Music);
        Assert.Equal(3, s.Bombs);
        Assert.Equal(4, s.LivesExtra);

        s.Rank = Rank.Easy; s.Save(settings);                    // the title menu saved its own: the CFG no longer applies
        var t = GameSettings.Load(settings, game);
        Assert.Equal(Rank.Easy, t.Rank);

        Assert.Equal(Rank.Normal, GameSettings.Load(Path2("none.json"), Path2("nowhere")).Rank);   // no CFG, no file: defaults
        File.WriteAllBytes(Path.Combine(game, "REIIDEN.CFG"), [1, 2, 3]);
        Assert.Equal(Rank.Normal, GameSettings.Load(Path2("none2.json"), game).Rank);               // damaged CFG: defaults
    }

    [Fact]
    public void GameTab_ShowsTheSettingsOfTheGame_ReadOnly()
    {
        var m = Open(OptionsTab.Game, game: new GameSettings { Rank = Rank.Lunatic, LivesExtra = 0, Bombs = 5 });
        Assert.Equal(["Lunatic", "3", "5"], m.Rows.Select(r => r.Value));
        Assert.All(m.Rows, r => Assert.Equal(RowKind.Info, r.Kind));
        var ja = Open(OptionsTab.Game, new LauncherSettings { Language = LauncherLanguage.Japanese }, game: new GameSettings { Rank = Rank.Easy });
        Assert.Equal("イージー", ja.Rows[0].Value);
    }

    [Fact]
    public void GameTab_ReadsTheSettingsOncePerVisit()
    {
        int reads = 0;
        var m = new LauncherModel(new LauncherSettings(), new LauncherGameInfo("C:/g", true, GameVersion.Japanese), "0.4.0");
        m.Attach(_svc, null, () => { reads++; return new GameSettings(); });
        m.Press(LauncherKey.Down); m.Press(LauncherKey.Select);
        while (m.Tab != OptionsTab.Game) m.Press(LauncherKey.TabNext);
        for (int i = 0; i < 30; i++) _ = m.Rows;
        Assert.Equal(1, reads);
        m.Press(LauncherKey.TabNext); m.Press(LauncherKey.TabPrev);
        _ = m.Rows;
        Assert.Equal(2, reads);
    }

    // ---- font, language, data, diagnostics --------------------------------------------------------------------------------

    sealed class StubFont : IFontSource
    {
        public bool TryGetGlyph(int cp, out Glyph g) { g = new Glyph(8, new byte[16]); return true; }
    }

    [Fact]
    public void FontRow_SwapsTheRealFontAtOnce()
    {
        string rom = Path2("FONT.ROM");
        var bytes = new byte[PcFontRom.EmulatorImageSize];
        for (int y = 0; y < 16; y++) bytes[0x800 + 0x41 * 16 + y] = 0xFF;      // 'A' is a solid block in the ROM
        File.WriteAllBytes(rom, bytes);
        var fonts = new LauncherFonts(new StubFont());
        var surf = new RealFontSurface(fonts);
        var applier = new LauncherApplier(surf);
        var m = Open(OptionsTab.Font, new LauncherSettings { FontRomPath = rom });
        applier.Apply(m.Settings, m.Revision, null);
        fonts.TryGetGlyph('A', out var a);
        Assert.Equal(0xFF, a.Rows.Span[0]);                       // ROM glyph
        Assert.Equal(FontRomState.Found, fonts.State);
        Down(m, 1);
        Assert.Equal(RowId.Font, m.FocusedRow.Id);
        m.Press(LauncherKey.Select);                              // free font
        applier.Apply(m.Settings, m.Revision, null);
        fonts.TryGetGlyph('A', out var b);
        Assert.Equal(0, b.Rows.Span[0]);
        m.Press(LauncherKey.Select);                              // ROM again
        applier.Apply(m.Settings, m.Revision, null);
        fonts.TryGetGlyph('A', out var c);
        Assert.Equal(0xFF, c.Rows.Span[0]);
    }

    sealed class RealFontSurface(LauncherFonts fonts) : ILauncherSurface
    {
        public bool IsFullscreen => false;
        public void SetFullscreen(bool on) { }
        public void SetWindowClientSize(int w, int h) { }
        public void SetIntegerScale(bool on) { }
        public void SetFilter(int f) { }
        public void SetVolumes(float m, float e) { }
        public void SetNumpad(bool on) { }
        public void ConfigureFonts(string? r, bool p, string? f) => fonts.Configure(r, p, f);
        public void SetRecordMissingArt(bool on) { }
    }

    [Fact]
    public void LocateFontRom_StoresThePath_AndTheStatusFollows()
    {
        var m = Open(OptionsTab.Font);
        Down(m, 3);
        Assert.Equal(RowId.LocateFontRom, m.FocusedRow.Id);
        _svc.Picked = "C:\\x\\FONT.ROM";
        int rev = m.Revision;
        m.Press(LauncherKey.Select);
        Assert.Equal("C:\\x\\FONT.ROM", m.Settings.FontRomPath);
        Assert.True(m.Revision > rev);                            // the applier re-reads the ROM
    }

    [Fact]
    public void RecordMissingArt_StopsTheLogAtOnce()
    {
        var log = new MissingArtLog();
        log.Note("a", 1, "x");
        Assert.Equal(1, log.Count);
        log.Recording = false;
        log.Note("b", 2, "y");
        Assert.Equal(1, log.Count);
        log.Recording = true;
        log.Note("b", 2, "y");
        Assert.Equal(2, log.Count);
        var surf = new FakeSurface();
        new LauncherApplier(surf).Apply(new LauncherSettings { RecordMissingArt = false }, 1, null);
        Assert.False(surf.Record);
    }

    [Fact]
    public void LastReport_CountsTheRealFiles_AndRefreshesWhenAsked()
    {
        var log = new MissingArtLog();
        var svc = new LauncherServices(_dir, log, new NoPrompt());
        Assert.True(svc.Summary().IsEmpty);
        File.WriteAllLines(Path2(MissingArtLog.FileName), ["sheetA[1]: not found", "sheetB: not loaded"]);
        Assert.True(svc.Summary().IsEmpty);                       // cached until refreshed (rows ask many times per frame)
        svc.RefreshReport();
        Assert.Equal(new ReportSummary(2, false), svc.Summary());
        log.Note("sheetC", 3, "missing");
        svc.RefreshReport();
        Assert.Equal(new ReportSummary(3, false), svc.Summary());
        File.WriteAllText(Path2("crash.local.log"), "InvalidOperationException: boom\nat x");
        svc.RefreshReport();
        Assert.Equal(new ReportSummary(3, true), svc.Summary());
        Assert.Contains(svc.ReportLines(), l => l.StartsWith("crash: InvalidOperationException"));
    }

    sealed class NoPrompt : IUserPrompt
    {
        public string? PickGameFile() => null;
        public void ShowError(string message) { }
    }

    [Fact]
    public void LastReportRow_SaysWhatIsInTheReport_InBothLanguages()
    {
        var m = Open(OptionsTab.Diagnostics);
        Down(m, 2);
        Assert.Equal("No errors", m.FocusedRow.Value);
        Assert.True(m.FocusedRow.Good);
        _svc.Report = ["missing art: a", "missing art: b"];
        Assert.Equal("2 missing art item(s)", m.FocusedRow.Value);
        Assert.False(m.FocusedRow.Good);
        _svc.Report = ["crash: boom", "missing art: a"];
        Assert.Equal("crash log + 1 missing art item(s)", m.FocusedRow.Value);
        _svc.Report = ["crash: boom"];
        Assert.Equal("crash log", m.FocusedRow.Value);
        var ja = Open(OptionsTab.Diagnostics, new LauncherSettings { Language = LauncherLanguage.Japanese });
        Down(ja, 2);
        Assert.Equal("クラッシュログ", ja.FocusedRow.Value);
        _svc.Report = ["crash: boom", "missing art: a"];
        Assert.Equal("クラッシュログ、1件の足りない画像", ja.FocusedRow.Value);
    }

    [Fact]
    public void OptionsOpeningAndTabChanges_RefreshTheReport_NotEveryFrame()
    {
        var m = Open(OptionsTab.Diagnostics);
        int n = _svc.Refreshes;
        for (int i = 0; i < 20; i++) _ = m.Rows;                  // drawing asks the rows many times
        Assert.Equal(n, _svc.Refreshes);
        m.Press(LauncherKey.TabNext);
        Assert.Equal(n + 1, _svc.Refreshes);
        m.Press(LauncherKey.TabPrev); Down(m, 1);
        m.Press(LauncherKey.Select);                              // opening the report refreshes too
        Assert.True(_svc.Refreshes >= n + 3);
    }

    [Fact]
    public void Language_ChangesTheLauncherAtOnce_AndTheFilterAndDataRowsStillWork()
    {
        var m = Open(OptionsTab.Data);
        m.Press(LauncherKey.Select);
        Assert.Equal(1, _svc.Opened);
        var f = Open(OptionsTab.Font);
        f.Press(LauncherKey.Select);
        Assert.Equal("はじめる", f.Text(Str.MenuPlay));
    }

    public void Dispose()
    {
        try { Directory.Delete(_dir, true); } catch (IOException) { }
    }
}
