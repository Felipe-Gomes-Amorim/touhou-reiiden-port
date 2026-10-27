using System.IO.Compression;
using Th01.Assets.Text;
using Th01.Platform.MonoGame.Launcher;

namespace Th01.Platform.Tests;

public class LauncherTests
{
    static readonly LauncherGameInfo Valid = new("C:\\game", true, GameVersion.Japanese);

    static LauncherModel Model(LauncherGameInfo? game = null, LauncherSettings? settings = null, Action<LauncherSettings>? save = null) =>
        new(settings ?? new LauncherSettings(), game ?? Valid, "0.4.0", save);

    [Fact]
    public void Home_StartsOnPlay_AndDownWalksTheWholeListAndWraps()
    {
        var m = Model();
        Assert.Equal(HomeItem.Play, m.FocusedItem);
        var seen = new List<HomeItem> { m.FocusedItem };
        for (int i = 0; i < LauncherModel.HomeOrder.Length; i++) { m.Press(LauncherKey.Down); seen.Add(m.FocusedItem); }
        Assert.Equal(LauncherModel.HomeOrder, seen.Take(7));
        Assert.Equal(HomeItem.Play, m.FocusedItem); // wrapped
        m.Press(LauncherKey.Up);
        Assert.Equal(HomeItem.About, m.FocusedItem);
    }

    [Fact]
    public void FirstRun_PlayIsOff_FocusStartsOnDirectory_AndSelectDoesNothingOnPlay()
    {
        var m = Model(LauncherGameInfo.None);
        Assert.True(m.FirstRun);
        Assert.False(m.IsEnabled(HomeItem.Play));
        Assert.Equal(HomeItem.Directory, m.FocusedItem); // as in the design
        m.Press(LauncherKey.Up); m.Press(LauncherKey.Up);
        Assert.NotEqual(HomeItem.Play, m.FocusedItem); // wrapping skips the disabled item
        m.SetGame(Valid);
        Assert.True(m.IsEnabled(HomeItem.Play));
    }

    [Fact]
    public void Select_ReturnsTheHostAction()
    {
        var m = Model();
        Assert.Equal(LauncherResult.Play, m.Press(LauncherKey.Select));
        m.Press(LauncherKey.Down); m.Press(LauncherKey.Down);
        Assert.Equal(LauncherResult.ChooseGame, m.Press(LauncherKey.Select)); // DIRECTORY
        m.Press(LauncherKey.Down);
        Assert.Equal(LauncherResult.Quit, m.Press(LauncherKey.Select));
    }

    [Fact]
    public void Options_OpensAndBackReturns_TabsWrapBothWays()
    {
        var m = Model();
        m.Press(LauncherKey.Down);
        m.Press(LauncherKey.Select);
        Assert.Equal(LauncherScreen.Options, m.Screen);
        Assert.Equal(OptionsTab.Accessibility, m.Tab);
        m.Press(LauncherKey.TabPrev);
        Assert.Equal(OptionsTab.Diagnostics, m.Tab);
        m.Press(LauncherKey.TabNext);
        Assert.Equal(OptionsTab.Accessibility, m.Tab);
        for (int i = 0; i < 3; i++) m.Press(LauncherKey.TabNext);
        Assert.Equal(OptionsTab.Font, m.Tab);
        m.Press(LauncherKey.Back);
        Assert.Equal(LauncherScreen.Home, m.Screen);
        Assert.Equal(HomeItem.Options, m.FocusedItem); // focus is where it was
    }

    [Fact]
    public void About_OpensFromTheFooterLink_AndBackReturns()
    {
        var m = Model();
        m.Press(LauncherKey.Up);
        Assert.Equal(HomeItem.About, m.FocusedItem);
        m.Press(LauncherKey.Select);
        Assert.Equal(LauncherScreen.About, m.Screen);
        m.Press(LauncherKey.Back);
        Assert.Equal(LauncherScreen.Home, m.Screen);
    }

    [Fact]
    public void Language_SwitchesWithLeftRightOrSelect_AndIsSaved()
    {
        var saved = new List<LauncherLanguage>();
        var m = Model(save: s => saved.Add(s.Language));
        m.Press(LauncherKey.Up); m.Press(LauncherKey.Up); // About, then Language
        Assert.Equal(HomeItem.Language, m.FocusedItem);
        Assert.Equal("PLAY", m.Text(Str.MenuPlay));
        m.Press(LauncherKey.Right);
        Assert.Equal(LauncherLanguage.Japanese, m.Language);
        Assert.Equal("はじめる", m.Text(Str.MenuPlay));
        m.Press(LauncherKey.Select);
        Assert.Equal(LauncherLanguage.English, m.Language);
        Assert.Equal([LauncherLanguage.Japanese, LauncherLanguage.English], saved);
    }

    [Fact]
    public void Language_SurvivesAFileRoundTrip_AndBadFilesGiveDefaults()
    {
        string dir = Path.Combine(Path.GetTempPath(), "launcher-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        try
        {
            string path = Path.Combine(dir, LauncherSettings.FileName);
            Assert.Equal(LauncherLanguage.English, LauncherSettings.Load(path).Language); // missing
            new LauncherSettings { Language = LauncherLanguage.Japanese }.Save(path);
            Assert.Equal(LauncherLanguage.Japanese, LauncherSettings.Load(path).Language);
            File.WriteAllText(path, "{not json");
            Assert.Equal(LauncherLanguage.English, LauncherSettings.Load(path).Language);
        }
        finally { Directory.Delete(dir, true); }
    }

    [Fact]
    public void EveryString_HasBothLanguages_AndEveryCharacterHasAGlyph()
    {
        var font = JapaneseFont.CreateDefault();
        foreach (var key in LauncherStrings.All)
            foreach (var lang in new[] { LauncherLanguage.English, LauncherLanguage.Japanese })
            {
                string s = LauncherStrings.Get(key, lang);
                Assert.False(string.IsNullOrWhiteSpace(s), $"{key}/{lang} is empty");
                foreach (var rune in s.EnumerateRunes())
                    Assert.True(font.TryGetGlyph(rune.Value, out _), $"{key}/{lang}: no glyph for U+{rune.Value:X4} '{rune}'");
            }
        foreach (var rune in "↑↓LRZB·…×—".EnumerateRunes().Concat(LauncherStrings.ProjectUrl.EnumerateRunes()).Concat(string.Concat(LauncherView.TabKanji).EnumerateRunes()))
            Assert.True(font.TryGetGlyph(rune.Value, out _), $"no glyph for U+{rune.Value:X4}");
    }

    [Fact]
    public void English_HasNoJapaneseOutsideTheOriginalTitle_AndJapaneseUsesTheGivenDisclaimer()
    {
        foreach (var key in LauncherStrings.All)
        {
            if (key is Str.Subtitle or Str.LanguageSwitch or Str.RowLanguage) continue;   // these carry both scripts on purpose
            Assert.DoesNotContain(LauncherStrings.Get(key, LauncherLanguage.English), c => c >= 0x3000);
        }
        Assert.Equal("Unofficial fan work. Requires your own copy of the original game.", LauncherStrings.Get(Str.Disclaimer, LauncherLanguage.English));
        Assert.Equal("非公式の二次創作です。プレイには原作のゲームが必要です。", LauncherStrings.Get(Str.Disclaimer, LauncherLanguage.Japanese));
    }

    [Fact]
    public void Bands_ScrollLeftOnePixelPerTick_AndLoopEveryFourTicks()
    {
        foreach (DitherKind kind in Enum.GetValues<DitherKind>())
        {
            // The band at tick t+1 is the band at tick t moved one pixel to the left.
            for (int y = 0; y < LauncherBackdrop.BandHeight; y++)
                for (int x = 0; x < 16; x++)
                    Assert.Equal(LauncherBackdrop.Dither(kind, x + LauncherBackdrop.ScrollOffset(0) + 1, y),
                                 LauncherBackdrop.Dither(kind, x + 1 + LauncherBackdrop.ScrollOffset(0), y));
        }
        Assert.Equal(LauncherBackdrop.ScrollOffset(0), LauncherBackdrop.ScrollOffset(4));
        Assert.Equal(LauncherBackdrop.ScrollOffset(1), LauncherBackdrop.ScrollOffset(1001));
        Assert.Equal(1, LauncherBackdrop.ScrollOffset(1) - LauncherBackdrop.ScrollOffset(0));

        var c0 = new LauncherCanvas();
        var c1 = new LauncherCanvas();
        int bottom = 96, y0 = LauncherCanvas.Height - bottom - 30;
        LauncherBackdrop.DrawBands(c0, 0, bottom);
        LauncherBackdrop.DrawBands(c1, 1, bottom);
        for (int y = y0; y < y0 + 30; y++)
            for (int x = 0; x < LauncherCanvas.Width - 1; x++)
                Assert.Equal(c0.Get(x + 1, y), c1.Get(x, y)); // what was at x+1 is now at x
        var c4 = new LauncherCanvas();
        LauncherBackdrop.DrawBands(c4, 4, bottom);
        Assert.Equal(c0.Pixels, c4.Pixels);
    }

    [Fact]
    public void Model_StepMovesTheClock_AndTheBandsFollowIt()
    {
        var m = Model();
        var font = JapaneseFont.CreateDefault();
        var a = new LauncherCanvas(); var b = new LauncherCanvas();
        LauncherView.Render(m, a, font);
        m.Step();
        LauncherView.Render(m, b, font);
        Assert.NotEqual(a.Pixels, b.Pixels);
        for (int i = 0; i < 3; i++) m.Step();
        var c = new LauncherCanvas();
        LauncherView.Render(m, c, font);
        Assert.Equal(a.Pixels, c.Pixels); // four ticks later the picture repeats
    }

    [Fact]
    public void Input_PressesOncePerKey_RepeatsArrowsOnlyAfterTheDelay()
    {
        var s = new LauncherInputState();
        Assert.Equal(LauncherKey.Down, s.Step(LauncherKey.Down));
        for (int i = 1; i < LauncherInputState.RepeatDelay; i++) Assert.Equal(LauncherKey.None, s.Step(LauncherKey.Down));
        Assert.Equal(LauncherKey.Down, s.Step(LauncherKey.Down));       // first repeat
        for (int i = 1; i < LauncherInputState.RepeatEvery; i++) Assert.Equal(LauncherKey.None, s.Step(LauncherKey.Down));
        Assert.Equal(LauncherKey.Down, s.Step(LauncherKey.Down));
        Assert.Equal(LauncherKey.None, s.Step(LauncherKey.None));
        Assert.Equal(LauncherKey.Select, s.Step(LauncherKey.Select));
        for (int i = 0; i < 100; i++) Assert.Equal(LauncherKey.None, s.Step(LauncherKey.Select)); // Select never repeats
    }

    [Theory]
    [InlineData(LauncherLanguage.English)]
    [InlineData(LauncherLanguage.Japanese)]
    public void EveryScreen_Renders_InBothLanguages_AndWrapKeepsLinesInTheWidth(LauncherLanguage lang)
    {
        var font = JapaneseFont.CreateDefault();
        var m = new LauncherModel(new LauncherSettings { Language = lang }, Valid, "0.4.0");
        var canvas = new LauncherCanvas();
        LauncherView.Render(m, canvas, font);
        m.Press(LauncherKey.Down); m.Press(LauncherKey.Select);
        LauncherView.Render(m, canvas, font);
        m.Press(LauncherKey.Back); m.Press(LauncherKey.Up); m.Press(LauncherKey.Up); m.Press(LauncherKey.Select);
        Assert.Equal(LauncherScreen.About, m.Screen);
        LauncherView.Render(m, canvas, font);
        foreach (string line in LauncherView.Wrap(font, m.Text(Str.AboutNote), 600))
            Assert.True(LauncherCanvas.MeasureText(font, line) <= 600, line);
    }

    /// <summary>Set LAUNCHER_DUMP to a folder to get PNGs of the screens (for looking at them; not an assertion).</summary>
    [Fact]
    public void Dump_Screens_WhenAsked()
    {
        string? dir = Environment.GetEnvironmentVariable("LAUNCHER_DUMP");
        if (string.IsNullOrEmpty(dir)) return;
        Directory.CreateDirectory(dir);
        var font = JapaneseFont.CreateDefault();
        foreach (var (lang, tag) in new[] { (LauncherLanguage.English, "en"), (LauncherLanguage.Japanese, "ja") })
        {
            var m = new LauncherModel(new LauncherSettings { Language = lang }, Valid, "0.4.0");
            var canvas = new LauncherCanvas();
            LauncherView.Render(m, canvas, font); WritePng(Path.Combine(dir, $"home-{tag}.png"), canvas);
            m.Press(LauncherKey.Up); m.Press(LauncherKey.Up);
            LauncherView.Render(m, canvas, font); WritePng(Path.Combine(dir, $"home-lang-{tag}.png"), canvas);
            m.Press(LauncherKey.Down); m.Press(LauncherKey.Select);
            LauncherView.Render(m, canvas, font); WritePng(Path.Combine(dir, $"about-{tag}.png"), canvas);
            m.Press(LauncherKey.Back); m.Press(LauncherKey.Down); m.Press(LauncherKey.Down); m.Press(LauncherKey.Down); m.Press(LauncherKey.Down);
            var f = new LauncherModel(new LauncherSettings { Language = lang }, LauncherGameInfo.None, "0.4.0");
            LauncherView.Render(f, canvas, font); WritePng(Path.Combine(dir, $"firstrun-{tag}.png"), canvas);
            var o = new LauncherModel(new LauncherSettings { Language = lang }, Valid, "0.4.0");
            o.Press(LauncherKey.Down); o.Press(LauncherKey.Select);
            LauncherView.Render(o, canvas, font); WritePng(Path.Combine(dir, $"options-{tag}.png"), canvas);
        }
    }

    static uint Crc(byte[] data)
    {
        uint crc = 0xFFFFFFFF;
        foreach (byte b in data)
        {
            crc ^= b;
            for (int k = 0; k < 8; k++) crc = (crc & 1) != 0 ? 0xEDB88320 ^ (crc >> 1) : crc >> 1;
        }
        return ~crc;
    }

    internal static void WritePng(string path, LauncherCanvas c)
    {
        var raw = new byte[(LauncherCanvas.Width * 3 + 1) * LauncherCanvas.Height];
        int o = 0;
        for (int y = 0; y < LauncherCanvas.Height; y++)
        {
            raw[o++] = 0;
            for (int x = 0; x < LauncherCanvas.Width; x++)
            {
                uint p = c.Get(x, y);
                raw[o++] = (byte)(p >> 16); raw[o++] = (byte)(p >> 8); raw[o++] = (byte)p;
            }
        }
        using var ms = new MemoryStream();
        using (var z = new ZLibStream(ms, CompressionLevel.Fastest, true)) z.Write(raw);
        using var fs = File.Create(path);
        fs.Write([0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A]);
        void Chunk(string type, byte[] data)
        {
            var len = new byte[4]; System.Buffers.Binary.BinaryPrimitives.WriteInt32BigEndian(len, data.Length);
            fs.Write(len);
            var td = System.Text.Encoding.ASCII.GetBytes(type).Concat(data).ToArray();
            fs.Write(td);
            var crc = new byte[4]; System.Buffers.Binary.BinaryPrimitives.WriteUInt32BigEndian(crc, Crc(td));
            fs.Write(crc);
        }
        var ihdr = new byte[13];
        System.Buffers.Binary.BinaryPrimitives.WriteInt32BigEndian(ihdr, LauncherCanvas.Width);
        System.Buffers.Binary.BinaryPrimitives.WriteInt32BigEndian(ihdr.AsSpan(4), LauncherCanvas.Height);
        ihdr[8] = 8; ihdr[9] = 2;
        Chunk("IHDR", ihdr); Chunk("IDAT", ms.ToArray()); Chunk("IEND", []);
    }
}
