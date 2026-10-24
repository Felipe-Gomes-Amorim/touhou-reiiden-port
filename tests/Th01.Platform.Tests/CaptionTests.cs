using Th01.Assets;
using Th01.Assets.Config;
using Th01.Core.Graphics;
using Th01.Platform.MonoGame;

namespace Th01.Platform.Tests;

/// <summary>Captions of the slideshow: text comes from an <see cref="ICaptionSource"/> (the user's FUUIN.EXE at run time).</summary>
public class CaptionTests
{
    sealed class Images : IScreenImages
    {
        public bool TryLoad(string file, out ArtFrame image)
        {
            var pal = new byte[48];
            pal[7 * 3] = pal[7 * 3 + 1] = pal[7 * 3 + 2] = 15; // colour 7 is the brightest
            image = new ArtFrame(640, 400, new byte[640 * 400], Array.Empty<byte>(), pal);
            return true;
        }
    }

    /// <summary>Line i of any scene is the single letter 'a' + i, so what gets drawn tells which lines were picked.</summary>
    sealed class Letters : ICaptionSource
    {
        public readonly List<string> Asked = new();
        public IReadOnlyList<string> Lines(string scene)
        {
            Asked.Add(scene);
            return Enumerable.Range(0, 14).Select(i => ((char)('a' + i)).ToString()).ToList();
        }
    }

    sealed class RecordingFont : IFontSource
    {
        public readonly List<int> Drawn = new();
        public bool TryGetGlyph(int codePoint, out Glyph glyph)
        {
            Drawn.Add(codePoint);
            glyph = new Glyph(8, new byte[16].Select(_ => (byte)0xFF).ToArray());
            return true;
        }
    }

    static string DrawnAfter(IReadOnlyList<SlideStep> steps, int ticks)
    {
        var font = new RecordingFont();
        var show = new Slideshow(new Images(), steps, font, new Letters());
        show.Tone = 0;
        for (int i = 0; i < ticks; i++) show.Tick();
        font.Drawn.Clear();
        show.Render(new Framebuffer());
        return new string(font.Drawn.Select(c => (char)c).Distinct().ToArray());
    }

    [Fact]
    public void Caption_DrawsTheRequestedLines_AndClearCaptionRemovesThem()
    {
        var steps = new SlideStep[] { new LoadPics("A.GRP"), new ShowPic(0, 3), new Caption("S.GRP", 2, 2, 316), new Wait(4), new ClearCaption(), new Wait(4) };
        Assert.Equal("", DrawnAfter(steps, 0));           // not yet
        Assert.Equal("cd", DrawnAfter(steps, 3));         // lines 2 and 3
        Assert.Equal("", DrawnAfter(steps, 7));           // cleared
    }

    [Fact]
    public void Caption_PaintsInColour15_CentredAndByteAligned_32RowsApart()
    {
        var show = new Slideshow(new Images(), [new LoadPics("A.GRP"), new ShowPic(0, 1), new Caption("S.GRP", 0, 2, 316), new Wait(5)], new RecordingFont(), new Letters());
        show.Tone = 0;
        show.Tick();
        var fb = new Framebuffer();
        show.Render(fb);
        // one 8 dot glyph: x = (320 - 4 + 7) & ~7 = 320; the lines start at rows 316 and 348
        Assert.Equal(15, fb.GetPixel(320, 320));
        Assert.Equal(15, fb.GetPixel(327, 331));
        Assert.Equal(0, fb.GetPixel(319, 320));
        Assert.Equal(0, fb.GetPixel(328, 320));
        Assert.Equal(15, fb.GetPixel(320, 350));          // second line
        Assert.Equal(0, fb.GetPixel(320, 336));           // nothing between them
    }

    [Fact]
    public void WithoutFontOrSource_NothingIsDrawn_AndTheSequenceStillFinishes()
    {
        var show = new Slideshow(new Images(), [new LoadPics("A.GRP"), new ShowPic(0, 1), new Caption("S.GRP", 0, 2, 316), new Wait(2)]);
        show.Tone = 0;
        var fb = new Framebuffer();
        show.Render(fb);
        for (int i = 0; i < 5; i++) show.Tick();
        Assert.True(show.Finished);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    public void BossSlides_PickTheBossPairOfEachRoute(int route)
    {
        // Makai shows boss pairs 0 1 3 5 (SinGyoku, YuugenMagan, Elis, Sariel); Jigoku 0 2 4 6 (SinGyoku, Mima, Kikuri, Konngara).
        int[] pairs = route == 0 ? [0, 1, 3, 5] : [0, 2, 4, 6];
        var steps = Cutscenes.BossSlides(route);
        var font = new RecordingFont();
        var show = new Slideshow(new Images(), steps, font, new Letters());
        var seen = new List<string>();
        for (int guard = 0; guard < 4000 && !show.Finished; guard++)
        {
            font.Drawn.Clear();
            show.Render(new Framebuffer());
            string drawn = new string(font.Drawn.Select(c => (char)c).Distinct().ToArray());
            if (drawn.Length > 0 && (seen.Count == 0 || seen[^1] != drawn)) seen.Add(drawn);
            show.Tick();
        }
        Assert.Equal(pairs.Select(p => $"{(char)('a' + p * 2)}{(char)('a' + p * 2 + 1)}"), seen);
    }

    [SkippableFact]
    public void RealFuuin_ServesTheEndingCaptions()
    {
        string? folder = GameDataConfig.Resolve();
        Skip.If(folder is null || !File.Exists(Path.Combine(folder!, "FUUIN.EXE")), "FUUIN.EXE not available.");
        var images = new AssetScreenImages(new FolderAssetSource(folder!));
        Assert.Equal(2, images.Lines("ED2A.GRP").Count);
        Assert.Equal(2, images.Lines("ed5c.grp").Count);
        Assert.True(images.Lines("ENDB_B.GRP").Count >= 14);
        Assert.Empty(images.Lines("NOPE.GRP"));
    }
}
