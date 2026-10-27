using Microsoft.Xna.Framework.Input;
using Th01.Assets.Text;
using Th01.Core.Game;
using Th01.Core.Input;
using Th01.Platform.MonoGame;
using Th01.Platform.MonoGame.Launcher;

namespace Th01.Platform.Tests;

public class LauncherOptionsTests : IDisposable
{
    sealed class FakeServices : ILauncherServices
    {
        public bool GamepadConnected { get; set; }
        public string? GamepadName { get; set; }
        public int Previews, Refreshes;
        public ReportSummary Sum = ReportSummary.Empty;
        public ReportSummary Summary() => Report.Length == 0 ? Sum : ReportSummary.FromLines(Report);
        public void RefreshReport() => Refreshes++;
        public void PreviewEffect() => Previews++;
        public FontRomState FontRomStatus { get; set; }
        public int Opened;
        public string[] Report = [];
        public string? Picked;
        public void OpenDataFolder() => Opened++;
        public IReadOnlyList<string> ReportLines() => Report;
        public string? PickFontRom() => Picked;
    }

    readonly string _dir = Directory.CreateTempSubdirectory("th01-lo-").FullName;
    readonly FakeServices _svc = new();
    readonly List<LauncherLanguage> _saves = [];
    int _saveCount;

    LauncherModel Open(OptionsTab tab = OptionsTab.Accessibility, LauncherSettings? settings = null, InputBindings? bindings = null)
    {
        var m = new LauncherModel(settings ?? new LauncherSettings(), new LauncherGameInfo("C:\\g", true, GameVersion.Japanese), "0.4.0", s => { _saveCount++; _saves.Add(s.Language); });
        m.Attach(_svc, bindings, () => new GameSettings { Rank = Rank.Hard, LivesExtra = 1, Bombs = 2 });
        m.Press(LauncherKey.Down);   // OPTIONS
        m.Press(LauncherKey.Select);
        while (m.Tab != tab) m.Press(LauncherKey.TabNext);
        return m;
    }

    InputBindings Bindings()
    {
        string file = Path.Combine(_dir, InputBindings.FileName);
        if (!File.Exists(file)) File.WriteAllText(file, "{}");
        return InputBindings.Load(_dir);
    }

    static void Down(LauncherModel m, int n) { for (int i = 0; i < n; i++) m.Press(LauncherKey.Down); }

    [Fact]
    public void Defaults_AreTheOnesAskedFor()
    {
        var s = new LauncherSettings();
        Assert.True(s.SoftenBombFlash);
        Assert.False(s.ReduceShake);
        Assert.False(s.RemoveTitleLines);
        Assert.True(s.PhotosensitivityWarning);
        var v = s.ToVisualOptions();
        Assert.Equal(BombFlash.Softened, v.BombFlash);
        Assert.True(v.ScreenShake);
    }

    [Fact]
    public void ReduceAll_SwitchesTheThreeTogether_AndFollowsThem()
    {
        var m = Open();
        Assert.Equal(RowId.ReduceAll, m.FocusedRow.Id);
        Assert.False(m.FocusedRow.On);                 // flash is soft by default, the other two are not
        m.Press(LauncherKey.Select);
        Assert.True(m.Settings.SoftenBombFlash && m.Settings.ReduceShake && m.Settings.RemoveTitleLines);
        Assert.True(m.FocusedRow.On);
        Assert.False(m.Settings.ToVisualOptions().ScreenShake);
        m.Press(LauncherKey.Down);                      // soften flash: off
        m.Press(LauncherKey.Select);
        Assert.False(m.Settings.SoftenBombFlash);
        Assert.Equal(BombFlash.Original, m.Settings.ToVisualOptions().BombFlash);
        m.Press(LauncherKey.Up);
        Assert.False(m.FocusedRow.On);                  // no longer "all"
        m.Press(LauncherKey.Select);
        Assert.True(m.Settings.ReduceAll);
        m.Press(LauncherKey.Select);
        Assert.False(m.Settings.SoftenBombFlash || m.Settings.ReduceShake || m.Settings.RemoveTitleLines);
    }

    [Fact]
    public void LeftAndRight_SetTogglesBars_AndCycleChoices()
    {
        var m = Open(OptionsTab.Video);
        m.Press(LauncherKey.Right);
        Assert.True(m.Settings.Fullscreen);
        m.Press(LauncherKey.Left);
        Assert.False(m.Settings.Fullscreen);
        m.Press(LauncherKey.Down);                       // window size: 2x by default
        Assert.Equal(1, m.Settings.WindowScale);
        m.Press(LauncherKey.Right); m.Press(LauncherKey.Right);
        Assert.Equal(0, m.Settings.WindowScale);         // 3x then wraps to 1x
        m.Press(LauncherKey.Left);
        Assert.Equal(2, m.Settings.WindowScale);
        Down(m, 2);                                      // filter
        m.Press(LauncherKey.Select); m.Press(LauncherKey.Select);
        Assert.Equal(2, m.Settings.Filter);
        m.Press(LauncherKey.Select);
        Assert.Equal(0, m.Settings.Filter);

        var a = Open(OptionsTab.Audio);
        for (int i = 0; i < 15; i++) a.Press(LauncherKey.Left);
        Assert.Equal(0, a.Settings.MusicVolume);
        for (int i = 0; i < 15; i++) a.Press(LauncherKey.Right);
        Assert.Equal(10, a.Settings.MusicVolume);
        a.Press(LauncherKey.Down);
        a.Press(LauncherKey.Left);
        Assert.Equal(9, a.Settings.EffectsVolume);
        Assert.Equal(10, a.Settings.MusicVolume);        // separate
        Assert.Equal(0.9f, a.Settings.EffectsLevel, 3);
    }

    [Fact]
    public void EveryChange_RaisesTheRevision_AndSaves()
    {
        var m = Open();
        int r = m.Revision, saves = _saveCount;
        m.Press(LauncherKey.Down); m.Press(LauncherKey.Select);
        Assert.Equal(r + 1, m.Revision);
        Assert.Equal(saves + 1, _saveCount);
        m.Press(LauncherKey.Left);                       // already off: Left sets "off" again, still a change event is fine
        Assert.True(m.Revision >= r + 1);
    }

    [Fact]
    public void Language_RowSwitchesTheScreenText()
    {
        var m = Open(OptionsTab.Font);
        Assert.Equal(RowId.Language, m.FocusedRow.Id);
        Assert.Equal("English", m.FocusedRow.Value);
        int r = m.Revision;
        m.Press(LauncherKey.Select);
        Assert.Equal(LauncherLanguage.Japanese, m.Language);
        Assert.Equal("日本語", m.FocusedRow.Value);
        Assert.Equal("オプション", m.Text(Str.OptionsHeading));
        Assert.True(m.Revision > r);
        Assert.Contains(LauncherLanguage.Japanese, _saves);
    }

    [Fact]
    public void FontTab_ShowsTheRomState_AndLocateStoresThePath()
    {
        var m = Open(OptionsTab.Font);
        Down(m, 2);
        Assert.Equal(RowId.FontRom, m.FocusedRow.Id);
        Assert.Equal("Not found", m.FocusedRow.Value);
        _svc.FontRomStatus = FontRomState.Found;
        Assert.Equal("Found", m.FocusedRow.Value);
        m.Press(LauncherKey.Down);
        Assert.Equal(RowId.LocateFontRom, m.FocusedRow.Id);
        _svc.Picked = null;
        m.Press(LauncherKey.Select);
        Assert.Null(m.Settings.FontRomPath);             // cancelled
        _svc.Picked = @"C:\fonts\FONT.ROM";
        m.Press(LauncherKey.Select);
        Assert.Equal(@"C:\fonts\FONT.ROM", m.Settings.FontRomPath);
    }

    [Fact]
    public void Controls_ListScrollsToKeepTheFocusVisible()
    {
        var m = Open(OptionsTab.Controls);
        Assert.Equal(9, m.Rows.Count);
        Assert.Equal(0, m.RowScroll);
        Down(m, 8);
        Assert.Equal(8, m.Row);
        Assert.Equal(8 - LauncherModel.VisibleRows + 1, m.RowScroll);
        m.Press(LauncherKey.Down);                       // wraps to the top
        Assert.Equal(0, m.Row);
        Assert.Equal(0, m.RowScroll);
        m.Press(LauncherKey.Up);
        Assert.Equal(8, m.Row);
        Assert.True(m.Row < m.RowScroll + LauncherModel.VisibleRows);
    }

    [Fact]
    public void Remap_ShotKey_TakesTheNextKey_NeverEscape()
    {
        var b = Bindings();
        var m = Open(OptionsTab.Controls, bindings: b);
        m.Press(LauncherKey.Down);                       // Shot
        Assert.Equal(RowId.Shot, m.FocusedRow.Id);
        Assert.Equal("Z", m.FocusedRow.Keys![0]);
        m.Press(LauncherKey.Select);
        Assert.True(m.Listening);
        Assert.True(m.FocusedRow.Listening);
        m.OfferKey(Keys.Escape);                         // not a binding
        Assert.True(m.Listening);
        m.OfferKey(Keys.A);
        Assert.False(m.Listening);
        Assert.Equal([Keys.A], b.Keyboard[GameButtons.Shot]);
        Assert.Equal("A", m.FocusedRow.Keys![0]);
        Assert.Contains("\"A\"", File.ReadAllText(Path.Combine(_dir, InputBindings.FileName)));   // saved
    }

    [Fact]
    public void Remap_Move_AsksForFourKeysInOrder_AndBackCancels()
    {
        var b = Bindings();
        var m = Open(OptionsTab.Controls, bindings: b);
        m.Press(LauncherKey.Select);
        m.OfferKey(Keys.W); m.OfferKey(Keys.S); m.OfferKey(Keys.A); m.OfferKey(Keys.D);
        Assert.False(m.Listening);
        Assert.Equal([Keys.W], b.Keyboard[GameButtons.Up]);
        Assert.Equal([Keys.S], b.Keyboard[GameButtons.Down]);
        Assert.Equal([Keys.A], b.Keyboard[GameButtons.Left]);
        Assert.Equal([Keys.D], b.Keyboard[GameButtons.Right]);
        Assert.Equal(["A", "W", "D", "S"], m.FocusedRow.Keys);   // shown left, up, right, down

        m.Press(LauncherKey.Select);
        m.OfferKey(Keys.I);
        Assert.True(m.Listening);
        m.Press(LauncherKey.Back);                       // cancels, stays on the options screen
        Assert.False(m.Listening);
        Assert.Equal(LauncherScreen.Options, m.Screen);
        Assert.Equal([Keys.I], b.Keyboard[GameButtons.Up]);
    }

    [Fact]
    public void Remap_PadButton_OnlyTakesButtons()
    {
        var b = Bindings();
        var m = Open(OptionsTab.Controls, bindings: b);
        Down(m, 5);
        Assert.Equal(RowId.PadShot, m.FocusedRow.Id);
        m.Press(LauncherKey.Select);
        Assert.True(m.ListeningForButton);
        m.OfferKey(Keys.A);
        Assert.True(m.Listening);                        // a key is not a button
        m.OfferButton(Buttons.X);
        Assert.False(m.Listening);
        Assert.Equal([Buttons.X], b.Gamepad[GameButtons.Shot]);
        Assert.Equal("X", m.FocusedRow.Keys![0]);
    }

    [Fact]
    public void Numpad_MovesWhenOn_AndNotWhenOff()
    {
        var b = new InputBindings();
        var kb = new KeyboardState(Keys.NumPad8, Keys.NumPad4);
        Assert.Equal(GameButtons.Up | GameButtons.Left, b.Poll(kb, default));
        b.Numpad = false;
        Assert.Equal(GameButtons.None, b.Poll(kb, default));
    }

    [Fact]
    public void KeyNames_AreShort()
    {
        Assert.Equal("↑", LauncherModel.KeyName(Keys.Up));
        Assert.Equal("Esc", LauncherModel.KeyName(Keys.Escape));
        Assert.Equal("5", LauncherModel.KeyName(Keys.D5));
        Assert.Equal("Num2", LauncherModel.KeyName(Keys.NumPad2));
        Assert.Equal("Z", LauncherModel.KeyName(Keys.Z));
    }

    [Fact]
    public void GameTab_ShowsTheGameSettingsReadOnly_AndSelectDoesNothing()
    {
        var m = Open(OptionsTab.Game);
        var rows = m.Rows;
        Assert.Equal("Hard", rows[0].Value);
        Assert.Equal("4", rows[1].Value);                // 3 + LivesExtra 1
        Assert.Equal("2", rows[2].Value);
        int r = m.Revision;
        m.Press(LauncherKey.Select); m.Press(LauncherKey.Right);
        Assert.Equal(r, m.Revision);
    }

    [Fact]
    public void Data_OpenFolderAsksTheProgram_RestoreNeedsTwoPresses_AndKeepsLanguage()
    {
        var s = new LauncherSettings { Language = LauncherLanguage.Japanese, MusicVolume = 3, Fullscreen = true };
        var m = Open(OptionsTab.Data, s);
        m.Press(LauncherKey.Select);
        Assert.Equal(1, _svc.Opened);
        m.Press(LauncherKey.Down);
        Assert.Equal(RowId.Restore, m.FocusedRow.Id);
        m.Press(LauncherKey.Select);
        Assert.Equal(3, s.MusicVolume);                  // first press only asks
        Assert.Equal(m.Text(Str.BtnConfirm), m.FocusedRow.Button);
        m.Press(LauncherKey.Up); m.Press(LauncherKey.Down);   // moving away cancels
        Assert.Equal(m.Text(Str.BtnRestore), m.FocusedRow.Button);
        m.Press(LauncherKey.Select); m.Press(LauncherKey.Select);
        Assert.Equal(10, s.MusicVolume);
        Assert.False(s.Fullscreen);
        Assert.Equal(LauncherLanguage.Japanese, s.Language);
    }

    [Fact]
    public void Diagnostics_ReportOpensAndClosesWithBack_AndSummaryCounts()
    {
        var m = Open(OptionsTab.Diagnostics);
        Assert.True(m.Settings.RecordMissingArt);
        m.Press(LauncherKey.Down);
        Assert.Equal(RowId.Report, m.FocusedRow.Id);
        m.Press(LauncherKey.Down);
        Assert.Equal("No errors", m.FocusedRow.Value);
        _svc.Report = ["missing art: a", "missing art: b"];
        Assert.Equal("2 missing art item(s)", m.FocusedRow.Value);
        m.Press(LauncherKey.Up);
        m.Press(LauncherKey.Select);
        Assert.True(m.ShowingReport);
        Assert.Equal(2, m.ReportLines.Count);
        m.Press(LauncherKey.Back);
        Assert.False(m.ShowingReport);
        Assert.Equal(LauncherScreen.Options, m.Screen);
    }

    [Fact]
    public void Settings_RoundTripEveryKey_ClampBadValues_AndKeepTheGameKeys()
    {
        string path = Path.Combine(_dir, "launcher.json");
        File.WriteAllText(path, "{\"Source\":\"S\",\"DataPath\":\"D\",\"windowScale\":9,\"musicVolume\":-4,\"filter\":\"x\"}");
        var s = LauncherSettings.Load(path);
        Assert.Equal(2, s.WindowScale);                  // clamped
        Assert.Equal(0, s.MusicVolume);
        Assert.Equal(0, s.Filter);                       // wrong type: default
        s = new LauncherSettings
        {
            Language = LauncherLanguage.Japanese, SoftenBombFlash = false, ReduceShake = true, RemoveTitleLines = true, PhotosensitivityWarning = false,
            Fullscreen = true, WindowScale = 2, IntegerScale = false, Filter = 2, MusicVolume = 4, EffectsVolume = 6,
            PreferFontRom = false, FontRomPath = "F", Numpad = false, RecordMissingArt = false,
        };
        s.Save(path);
        var t = LauncherSettings.Load(path);
        Assert.Equal(LauncherLanguage.Japanese, t.Language);
        Assert.False(t.SoftenBombFlash); Assert.True(t.ReduceShake); Assert.True(t.RemoveTitleLines); Assert.False(t.PhotosensitivityWarning);
        Assert.True(t.Fullscreen); Assert.Equal(2, t.WindowScale); Assert.False(t.IntegerScale); Assert.Equal(2, t.Filter);
        Assert.Equal(4, t.MusicVolume); Assert.Equal(6, t.EffectsVolume);
        Assert.False(t.PreferFontRom); Assert.Equal("F", t.FontRomPath); Assert.False(t.Numpad); Assert.False(t.RecordMissingArt);
        string text = File.ReadAllText(path);
        Assert.Contains("\"Source\":\"S\"", text);       // the remembered game survives
        Assert.Contains("\"DataPath\":\"D\"", text);
        File.WriteAllText(path, "not json at all");
        Assert.Equal(new LauncherSettings().MusicVolume, LauncherSettings.Load(path).MusicVolume);
    }

    [Fact]
    public void MixerLevels_AreClamped()
    {
        var mixer = new Th01.Audio.Playback.AudioMixer();
        mixer.MusicLevel = 3f; mixer.EffectLevel = -1f;
        Assert.Equal(1f, mixer.MusicLevel);
        Assert.Equal(0f, mixer.EffectLevel);
    }

    [Theory]
    [InlineData(LauncherLanguage.English)]
    [InlineData(LauncherLanguage.Japanese)]
    public void EveryTab_Renders_WithFocusOnEveryRow_AndWhileListeningAndReporting(LauncherLanguage lang)
    {
        var font = JapaneseFont.CreateDefault();
        var canvas = new LauncherCanvas();
        var b = Bindings();
        var m = Open(OptionsTab.Accessibility, new LauncherSettings { Language = lang }, b);
        _svc.Report = Enumerable.Range(0, 20).Select(i => $"missing art: sheet{i}[3]: not found").ToArray();
        for (int tab = 0; tab < 8; tab++)
        {
            for (int row = 0; row < m.Rows.Count; row++)
            {
                LauncherView.Render(m, canvas, font);
                var r = m.FocusedRow;
                foreach (var rune in (m.Text(r.Label) + m.Text(r.Hint) + r.Value + r.Button + r.Tag).EnumerateRunes())
                    Assert.True(font.TryGetGlyph(rune.Value, out _), $"{tab}/{row}: U+{rune.Value:X4}");
                if (r.Kind == RowKind.Keys) { m.Press(LauncherKey.Select); LauncherView.Render(m, canvas, font); m.Press(LauncherKey.Back); }
                if (r.Id == RowId.Report) { m.Press(LauncherKey.Select); LauncherView.Render(m, canvas, font); m.Press(LauncherKey.Back); }
                m.Press(LauncherKey.Down);
            }
            m.Press(LauncherKey.TabNext);
        }
    }

    /// <summary>LAUNCHER_DUMP=folder also writes the options screens.</summary>
    [Fact]
    public void Dump_OptionsScreens_WhenAsked()
    {
        string? dir = Environment.GetEnvironmentVariable("LAUNCHER_DUMP");
        if (string.IsNullOrEmpty(dir)) return;
        Directory.CreateDirectory(dir);
        var font = JapaneseFont.CreateDefault();
        foreach (var (lang, tag) in new[] { (LauncherLanguage.English, "en"), (LauncherLanguage.Japanese, "ja") })
        {
            var canvas = new LauncherCanvas();
            foreach (var tab in new[] { OptionsTab.Accessibility, OptionsTab.Video, OptionsTab.Audio, OptionsTab.Font, OptionsTab.Controls, OptionsTab.Diagnostics })
            {
                var m = Open(tab, new LauncherSettings { Language = lang }, Bindings());
                if (tab == OptionsTab.Controls) { Down(m, 1); m.Press(LauncherKey.Select); }
                if (tab == OptionsTab.Accessibility) Down(m, 1);
                LauncherView.Render(m, canvas, font);
                LauncherTests.WritePng(Path.Combine(dir, $"p3-{tab}-{tag}.png"), canvas);
            }
        }
    }

    public void Dispose()
    {
        try { Directory.Delete(_dir, true); } catch (IOException) { }
    }
}
