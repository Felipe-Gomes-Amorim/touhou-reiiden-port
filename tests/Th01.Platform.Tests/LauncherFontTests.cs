using Th01.Assets.Text;
using Th01.Core.Graphics;
using Th01.Platform.MonoGame.Launcher;

namespace Th01.Platform.Tests;

public class LauncherFontTests : IDisposable
{
    readonly string _dir = Directory.CreateTempSubdirectory("th01-lf-").FullName;

    /// <summary>A synthetic ROM image of the emulator layout: 'A' and the kana 'あ' (JIS 4-2) get recognisable patterns, nothing else is drawn.</summary>
    static byte[] FakeRom()
    {
        var rom = new byte[PcFontRom.EmulatorImageSize];
        int ankA = 0x800 + 0x41 * 16;
        for (int y = 0; y < 16; y++) rom[ankA + y] = (byte)(y % 2 == 0 ? 0xAA : 0x55);
        int a = 0x1800 + 32 + (4 - 1) * 0xC00 + (2 - 1) * 32;       // JIS row 4, column 2
        for (int y = 0; y < 16; y++) { rom[a + y] = 0xF0; rom[a + 16 + y] = 0x0F; }   // left half 11110000, right half 00001111
        int bs = 0x800 + 0x5C * 16;
        for (int y = 0; y < 16; y++) rom[bs + y] = 0xFF;             // the ROM's 0x5C: a block (stands for the yen sign)
        return rom;
    }

    sealed class StubFont : IFontSource
    {
        public readonly List<int> Asked = [];
        public bool TryGetGlyph(int codePoint, out Glyph glyph)
        {
            Asked.Add(codePoint);
            glyph = new Glyph(8, new byte[16]);
            return true;
        }
    }

    static byte Row(Glyph g, int y, int b = 0) => g.Rows.Span[y * (g.Width / 8) + b];

    [Fact]
    public void Bridge_ReadsAnkAndKanaFromTheRom_AndPacksTheRows()
    {
        var bridge = new FontRomBridge(new PcFontRom(FakeRom(), FontRomLayout.EmulatorImage), new StubFont());
        Assert.True(bridge.TryGetGlyph('A', out var a));
        Assert.Equal(8, a.Width);
        Assert.Equal(0xAA, Row(a, 0));
        Assert.Equal(0x55, Row(a, 1));
        Assert.True(bridge.TryGetGlyph('あ', out var kana));
        Assert.Equal(16, kana.Width);
        Assert.Equal(0xF0, Row(kana, 5, 0));
        Assert.Equal(0x0F, Row(kana, 5, 1));
    }

    [Fact]
    public void Bridge_FallsBack_ForWhatTheRomLacks_AndForYenAndOverlinePositions()
    {
        var fallback = new StubFont();
        var bridge = new FontRomBridge(new PcFontRom(FakeRom(), FontRomLayout.EmulatorImage), fallback);
        Assert.True(bridge.TryGetGlyph('\\', out _));    // paths must show a backslash, not the ROM's yen
        Assert.True(bridge.TryGetGlyph('~', out _));
        Assert.True(bridge.TryGetGlyph(0x1F600, out _));     // not JIS: fallback
        Assert.True(bridge.TryGetGlyph(0x07, out _));    // control code
        Assert.Contains((int)'\\', fallback.Asked);
        Assert.Contains((int)'~', fallback.Asked);
        Assert.Contains(0x1F600, fallback.Asked);
        Assert.Contains(0x07, fallback.Asked);
        Assert.DoesNotContain((int)'A', fallback.Asked);
    }

    [Fact]
    public void RealFontsConfig_PicksTheRomOnlyWhenPreferredAndSupported()
    {
        string good = Path.Combine(_dir, "FONT.ROM");
        File.WriteAllBytes(good, FakeRom());
        string bad = Path.Combine(_dir, "small.rom");
        File.WriteAllBytes(bad, new byte[100]);
        var free = new StubFont();
        var fonts = new LauncherFonts(free);

        fonts.Configure(good, preferRom: true, gameFolder: null);
        Assert.Equal(FontRomState.Found, fonts.State);
        Assert.Equal(good, fonts.RomPath);
        fonts.TryGetGlyph('A', out var a);
        Assert.Equal(0xAA, Row(a, 0));                   // from the ROM
        Assert.DoesNotContain((int)'A', free.Asked);

        fonts.Configure(good, preferRom: false, gameFolder: null);
        Assert.Equal(FontRomState.Found, fonts.State);   // still found, just not used
        fonts.TryGetGlyph('A', out _);
        Assert.Contains((int)'A', free.Asked);

        fonts.Configure(bad, preferRom: true, gameFolder: null);
        Assert.Equal(FontRomState.Unsupported, fonts.State);
        free.Asked.Clear();
        fonts.TryGetGlyph('A', out _);
        Assert.Contains((int)'A', free.Asked);           // free font as the reserve
    }

    [Fact]
    public void FontRomInTheGameFolder_IsFound()
    {
        string game = Path.Combine(_dir, "game");
        Directory.CreateDirectory(game);
        File.WriteAllBytes(Path.Combine(game, "FONT.ROM"), FakeRom());
        var fonts = new LauncherFonts(new StubFont());
        fonts.Configure(null, true, game);
        // TH01_FONT_ROM or config.local.json on the machine may win, but either way a usable ROM is found.
        Assert.Equal(FontRomState.Found, fonts.State);
    }

    [Fact]
    public void LauncherScreens_DrawWithTheRomFont()
    {
        string good = Path.Combine(_dir, "FONT.ROM");
        File.WriteAllBytes(good, FakeRom());
        var fonts = new LauncherFonts(JapaneseFont.CreateDefault());
        fonts.Configure(good, true, null);
        var m = new LauncherModel(new LauncherSettings(), new LauncherGameInfo("C:\\g", true, GameVersion.Japanese), "0.4.0");
        var canvas = new LauncherCanvas();
        LauncherView.Render(m, canvas, fonts);           // the synthetic ROM is mostly blank: this only checks nothing breaks
        m.Press(LauncherKey.Down); m.Press(LauncherKey.Select);
        LauncherView.Render(m, canvas, fonts);
    }

    [SkippableFact]
    public void RealRom_WhenConfigured_DrawsEveryLauncherCharacter()
    {
        string? path = Th01.Assets.Config.FontRomConfig.Resolve();
        Skip.If(path is null, "no FONT.ROM configured");
        var fonts = new LauncherFonts(JapaneseFont.CreateDefault());
        fonts.Configure(null, true, null);
        Skip.If(fonts.State != FontRomState.Found, "the configured ROM is not the emulator image");
        foreach (var key in LauncherStrings.All)
            foreach (var lang in new[] { LauncherLanguage.English, LauncherLanguage.Japanese })
                foreach (var rune in LauncherStrings.Get(key, lang).EnumerateRunes())
                    Assert.True(fonts.TryGetGlyph(rune.Value, out _), $"{key}/{lang}: U+{rune.Value:X4}");

        // Looking at it: LAUNCHER_DUMP=folder writes the home screen drawn with the real ROM.
        string? dir = Environment.GetEnvironmentVariable("LAUNCHER_DUMP");
        if (!string.IsNullOrEmpty(dir))
        {
            Directory.CreateDirectory(dir);
            foreach (var lang in new[] { LauncherLanguage.English, LauncherLanguage.Japanese })
            {
                var m = new LauncherModel(new LauncherSettings { Language = lang }, new LauncherGameInfo(@"C:\Jogos\Touhou\th01.hdi", true, GameVersion.Japanese), "0.4.0");
                var canvas = new LauncherCanvas();
                LauncherView.Render(m, canvas, fonts);
                LauncherTests.WritePng(Path.Combine(dir, $"p4-rom-home-{lang}.png"), canvas);
            }
        }
    }

    public void Dispose()
    {
        try { Directory.Delete(_dir, true); } catch (IOException) { }
    }
}
