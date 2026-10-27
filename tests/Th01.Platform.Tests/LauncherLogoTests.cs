using Th01.Assets;
using Th01.Assets.Model;
using Th01.Assets.Text;
using Th01.Platform.MonoGame.Launcher;

namespace Th01.Platform.Tests;

public class LauncherLogoTests
{
    static readonly Th01.Core.Graphics.IFontSource Font = JapaneseFont.CreateDefault();
    static readonly LauncherGameInfo Valid = new("C:\\g", true, GameVersion.Japanese);

    static Palette4 Pal()
    {
        var p = new Palette4();
        p.Set(0, 0, 0, 0);          // black: background
        p.Set(15, 15, 15, 15);      // white: letters
        p.Set(1, 15, 0, 0);         // red: spiral
        p.Set(2, 9, 9, 9);          // grey: anti-aliasing of the letters
        return p;
    }

    /// <summary>A made-up title picture: white and grey blocks where the letters go, a red stroke across them, white noise far below.</summary>
    static IndexedImage Synthetic(bool withLetters = true, bool withPalette = true)
    {
        var px = new byte[640 * 400];
        void Block(int x, int y, int w, int h, byte v) { for (int yy = y; yy < y + h; yy++) for (int xx = x; xx < x + w; xx++) px[yy * 640 + xx] = v; }
        if (withLetters)
        {
            Block(120, 100, 40, 60, 15);     // a kanji-like block
            Block(300, 95, 30, 70, 15);
            Block(200, 180, 100, 10, 2);     // subtitle: grey
            Block(110, 140, 410, 3, 1);      // red stroke across: must not be part of the logo
        }
        Block(50, 300, 20, 20, 15);          // white far below the search box: ignored
        return new IndexedImage(640, 400, px, withPalette ? Pal() : null);
    }

    [Fact]
    public void Logo_KeepsOnlyTheNeutralLetters_TrimmedToTheirBox()
    {
        var logo = LauncherLogo.FromImage(Synthetic());
        Assert.NotNull(logo);
        Assert.Equal(300 + 30 - 120, logo!.Width);          // x 120..329
        Assert.Equal(190 - 95, logo.Height);                // y 95..189
        int white = 0, grey = 0, other = 0;
        foreach (uint p in logo.Pixels)
        {
            if (p == 0) continue;
            if (p == 0xFFFFFFFF) white++;
            else if (p == 0xFF999999) grey++;
            else other++;
        }
        Assert.Equal(40 * 60 + 30 * 70 - 3 * 40 - 3 * 30, white);   // the red stroke covers three rows of each block
        Assert.Equal(100 * 10, grey);
        Assert.Equal(0, other);                              // no red from the spiral
        Assert.Equal(0u, logo.Pixels[(140 - 95) * logo.Width + 5]);   // where the red stroke was (a gap between blocks): transparent
    }

    [Fact]
    public void Logo_IsNullWithoutLettersOrWithoutAPalette_AndTakesAFallbackPalette()
    {
        Assert.Null(LauncherLogo.FromImage(Synthetic(withLetters: false)));
        Assert.Null(LauncherLogo.FromImage(Synthetic(withPalette: false)));
        Assert.NotNull(LauncherLogo.FromImage(Synthetic(withPalette: false), Pal()));
    }

    [Fact]
    public void Logo_DrawsAtWholeNumberScale_WithNoSmoothing()
    {
        var logo = LauncherLogo.FromImage(Synthetic())!;
        Assert.Equal(2, LauncherLogo.Scale);
        var c = new LauncherCanvas();
        c.Pixels.AsSpan().Fill(LauncherColors.Background);
        logo.Draw(c, 100, 200);
        // The first white block starts at (120,100) -> logo (0,5); each logo pixel is a 2x2 block with a hard edge.
        Assert.Equal(0xFFFFFFFF, c.Get(100, 200 + 10));
        Assert.Equal(0xFFFFFFFF, c.Get(101, 200 + 11));
        Assert.Equal(LauncherColors.Background, c.Get(99, 200 + 10));
        Assert.Equal(LauncherColors.Background, c.Get(100, 200 + 9));
        Assert.Equal(0xFFFFFFFF, c.Get(100 + 79, 200 + 10 + 119));   // last pixel of the 40x60 block at 2x
        Assert.Equal(LauncherColors.Background, c.Get(100 + 80, 200 + 10));
        Assert.Equal(logo.Width * 2, logo.DrawnWidth);
    }

    [Fact]
    public void Home_WithALogo_ReplacesTheSubtitleOnly_AndMovesTheMenuAndPanelDown()
    {
        var logo = LauncherLogo.FromImage(Synthetic())!;
        var with = new LauncherModel(new LauncherSettings(), Valid, "0.4.0") { Logo = logo };
        var without = new LauncherModel(new LauncherSettings(), Valid, "0.4.0");
        var a = new LauncherCanvas(); var b = new LauncherCanvas();
        var hitsA = new LauncherHitMap(); var hitsB = new LauncherHitMap();
        LauncherView.Render(with, a, Font, hitsA);
        LauncherView.Render(without, b, Font, hitsB);
        Assert.NotEqual(a.Pixels, b.Pixels);

        // The "fan port" title (left of the orb) and the whole footer with the unofficial-work notice stay the same.
        for (int y = 56; y < 104; y++) for (int x = 72; x < 600; x++) Assert.Equal(b.Get(x, y), a.Get(x, y));
        for (int y = 624; y < 720; y++) for (int x = 0; x < 1280; x++) Assert.Equal(b.Get(x, y), a.Get(x, y));

        // The menu starts lower with the logo, and the logo is on the picture.
        int PlayTop(LauncherHitMap h) { for (int y = 0; y < 720; y++) if (h.At(120, y) is { Kind: HitKind.HomeItem, A: (int)HomeItem.Play }) return y; return -1; }
        Assert.True(PlayTop(hitsA) > PlayTop(hitsB));
        Assert.Contains(Enumerable.Range(110, 100), y => a.Get(72 + 20, y) == 0xFFFFFFFF || a.Get(72 + 150, y) == 0xFFFFFFFF || a.Get(72 + 360, y) == 0xFFFFFFFF);
    }

    [Fact]
    public void Home_WithoutAValidGame_KeepsTheTextSubtitle_EvenIfALogoIsSet()
    {
        var logo = LauncherLogo.FromImage(Synthetic())!;
        var first = new LauncherModel(new LauncherSettings(), LauncherGameInfo.None, "0.4.0") { Logo = logo };
        var plain = new LauncherModel(new LauncherSettings(), LauncherGameInfo.None, "0.4.0");
        var a = new LauncherCanvas(); var b = new LauncherCanvas();
        LauncherView.Render(first, a, Font);
        LauncherView.Render(plain, b, Font);
        Assert.Equal(b.Pixels, a.Pixels);
    }

    [Fact]
    public void ApplyChoice_SwapsTheLogoAtOnce_AndARefusalKeepsIt()
    {
        var logo = LauncherLogo.FromImage(Synthetic())!;
        var m = new LauncherModel(new LauncherSettings(), LauncherGameInfo.None, "0.4.0");
        Assert.Null(m.Logo);
        m.ApplyChoice(new GameChoice(Valid, Logo: logo));
        Assert.Same(logo, m.Logo);
        m.ApplyChoice(new GameChoice(null, Th01.Assets.GameProblemCode.NotFound));
        Assert.Same(logo, m.Logo);                                     // refused: the old game and its logo stay
        m.ApplyChoice(new GameChoice(new LauncherGameInfo("D:\\other", true, GameVersion.Translated), Logo: null));
        Assert.Null(m.Logo);                                           // another copy without the picture: back to the text subtitle
    }

    [Fact]
    public void Logo_FromAFolderWithoutThePicture_IsNull()
    {
        string dir = Directory.CreateTempSubdirectory("th01-logo-").FullName;
        try { Assert.Null(LauncherLogo.TryLoad(new FolderAssetSource(dir))); }
        finally { Directory.Delete(dir, true); }
    }

    [Fact]
    public void Logo_FromAnUnreadableFile_IsNull_NotACrash()
    {
        string dir = Directory.CreateTempSubdirectory("th01-logo-").FullName;
        try
        {
            File.WriteAllBytes(Path.Combine(dir, "REIIDEN3.GRP"), [1, 2, 3, 4, 5]);
            Assert.Null(LauncherLogo.TryLoad(new FolderAssetSource(dir)));
        }
        finally { Directory.Delete(dir, true); }
    }

    [SkippableFact]
    public void RealData_LogoIsCutOutOfTheRealTitlePicture()
    {
        string? data = Environment.GetEnvironmentVariable("TH01_DATA_PATH");
        Skip.If(string.IsNullOrEmpty(data) || !File.Exists(Path.Combine(data!, "REIIDEN3.GRP")), "no REIIDEN3.GRP");
        var logo = LauncherLogo.TryLoad(new FolderAssetSource(data!));
        Assert.NotNull(logo);
        Assert.InRange(logo!.Width, 300, LauncherLogo.SearchWidth);
        Assert.InRange(logo.Height, 60, LauncherLogo.SearchHeight);
        Assert.DoesNotContain(logo.Pixels, p => p != 0 && ((p >> 16) & 0xFF) > ((p & 0xFF) + 40));   // nothing red
        Assert.True(logo.Pixels.Count(p => p != 0) > 2000);

        string? dir = Environment.GetEnvironmentVariable("LAUNCHER_DUMP");
        if (!string.IsNullOrEmpty(dir))
        {
            Directory.CreateDirectory(dir);
            foreach (var lang in new[] { LauncherLanguage.English, LauncherLanguage.Japanese })
            {
                var m = new LauncherModel(new LauncherSettings { Language = lang }, new LauncherGameInfo(@"C:\Jogos\Touhou\th01.hdi", true, GameVersion.Japanese), "0.4.0") { Logo = logo };
                var c = new LauncherCanvas();
                LauncherView.Render(m, c, Font);
                LauncherTests.WritePng(Path.Combine(dir, $"p6-home-logo-{lang}.png"), c);
            }
        }
    }
}
