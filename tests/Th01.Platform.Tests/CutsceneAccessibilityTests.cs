using Th01.Core.Graphics;
using Th01.Platform.MonoGame;
using Th01.Platform.MonoGame.Launcher;

namespace Th01.Platform.Tests;

/// <summary>The accessibility options act on the ending cutscenes: shake and the strobing tone / explosion picture.</summary>
public class CutsceneAccessibilityTests
{
    /// <summary>A picture whose four quarters hold four different colours, so the shown quarter can be told from a pixel.</summary>
    sealed class QuarterImages : IScreenImages
    {
        public bool TryLoad(string file, out ArtFrame image)
        {
            var px = new byte[640 * 400];
            for (int y = 0; y < 400; y++)
                for (int x = 0; x < 640; x++) px[y * 640 + x] = (byte)(1 + (y >= 200 ? 2 : 0) + (x >= 320 ? 1 : 0));
            image = new ArtFrame(640, 400, px, Array.Empty<byte>(), new byte[48]);
            return true;
        }
    }

    sealed record Frame(int Tone, int ScrollY, int Quarter);

    static List<Frame> Play(IReadOnlyList<SlideStep> steps, SlideEffects? effects)
    {
        var show = new Slideshow(new QuarterImages(), steps, effects: effects);
        var fb = new Framebuffer();
        var frames = new List<Frame>();
        for (int t = 0; t < 40000 && !show.Finished; t++)
        {
            show.Tick();
            fb.Clear();
            show.Render(fb);
            frames.Add(new Frame(show.Tone, fb.ScrollY, fb.GetPixel(200, 120)));   // inside the picture area: shows the current quarter
        }
        Assert.True(show.Finished);
        return frames;
    }

    static int Jumps(IEnumerable<int> values, int atLeast)
    {
        int n = 0, prev = int.MinValue;
        foreach (int v in values) { if (prev != int.MinValue && Math.Abs(v - prev) >= atLeast) n++; prev = v; }
        return n;
    }

    static int Changes(IEnumerable<int> values)
    {
        int n = 0, prev = int.MinValue;
        foreach (int v in values) { if (prev != int.MinValue && v != prev) n++; prev = v; }
        return n;
    }

    static readonly IReadOnlyList<SlideStep> BadEnding = Cutscenes.Bad(1);   // the Jigoku bad ending has the shaking explosion

    [Fact]
    public void Original_ShakesAndStrobes()
    {
        var f = Play(BadEnding, SlideEffects.Original);
        Assert.Equal(8, f.Max(x => x.ScrollY));
        Assert.True(Jumps(f.Select(x => x.Tone), 2) >= 30, "the tone should flip back and forth");
        Assert.True(Changes(f.Select(x => x.Quarter)) >= 20, "the explosion picture should flicker");
        // the default (no effects given) is the original too
        Assert.Equal(f.Select(x => x.ScrollY), Play(BadEnding, null).Select(x => x.ScrollY));
    }

    [Fact]
    public void ReducedShake_NeverScrollsTheScreen_ButStillStrobesIfFlashesAreNotSoftened()
    {
        var f = Play(BadEnding, new SlideEffects(Shake: false, SoftFlashes: false));
        Assert.All(f, x => Assert.Equal(0, x.ScrollY));
        Assert.True(Jumps(f.Select(x => x.Tone), 2) >= 30);   // the two options are independent
    }

    [Fact]
    public void SoftFlashes_HoldTheToneAndTheBoomPictureSteady_ButStillShake()
    {
        var original = Play(BadEnding, SlideEffects.Original);
        var soft = Play(BadEnding, new SlideEffects(Shake: true, SoftFlashes: true));
        Assert.Equal(original.Count, soft.Count);               // same length: only the look changes
        Assert.Equal(8, soft.Max(x => x.ScrollY));
        Assert.True(Jumps(soft.Select(x => x.Tone), 2) <= 4, "no back-and-forth tone jumps");
        Assert.True(Changes(soft.Select(x => x.Quarter)) < Changes(original.Select(x => x.Quarter)) / 3);
    }

    [Fact]
    public void BothReduced_IsCalmAndSameLength()
    {
        var original = Play(BadEnding, SlideEffects.Original);
        var calm = Play(BadEnding, new SlideEffects(false, true));
        Assert.Equal(original.Count, calm.Count);
        Assert.All(calm, x => Assert.Equal(0, x.ScrollY));
        Assert.True(Jumps(calm.Select(x => x.Tone), 2) <= 4);
    }

    [Fact]
    public void Settings_ReachTheCutscenes_ThroughFlowOptions()
    {
        var d = new LauncherSettings().ToFlowOptions(true).Slides;
        Assert.True(d.Shake);
        Assert.True(d.SoftFlashes);                              // default: flash softened
        var s = new LauncherSettings { SoftenBombFlash = false, ReduceShake = true };
        var o = s.ToFlowOptions(true);
        Assert.Equal(new SlideEffects(false, false), o.Slides);
        Assert.False(o.Visuals.SoftFlashes);
        Assert.Equal(Th01.Core.Game.BombFlash.Original, o.Visuals.BombFlash);
        var all = new LauncherSettings(); all.ReduceAll = true;
        Assert.Equal(new SlideEffects(false, true), all.ToFlowOptions(true).Slides);
        Assert.True(all.ToFlowOptions(true).Visuals.SoftFlashes);
    }

    [Fact]
    public void SoftenFlashRow_ControlsTheBombAndTheBossFlashesTogether()
    {
        var s = new LauncherSettings();
        Assert.Equal(s.ToVisualOptions().BombFlash == Th01.Core.Game.BombFlash.Softened, s.ToVisualOptions().SoftFlashes);
        s.SoftenBombFlash = false;
        var v = s.ToVisualOptions();
        Assert.Equal(Th01.Core.Game.BombFlash.Original, v.BombFlash);
        Assert.False(v.SoftFlashes);
        s.SoftenBombFlash = true;
        Assert.True(s.ToVisualOptions().SoftFlashes);
    }

    [Fact]
    public void TheFlowGetsTheCutsceneEffects()
    {
        // GameFlow keeps them and passes them to every slideshow it starts.
        var flow = new GameFlow(new FlowFakeStages(), new QuarterImages(), BdfFont.LoadSpleen8x16(), new GameSettings(),
            slideEffects: new SlideEffects(false, true), startCutscene: 7);
        for (int i = 0; i < 4000 && flow.Screen == Screen.Cutscene; i++)
        {
            flow.Tick(Th01.Core.Input.GameButtons.None);
            var fb = new Framebuffer();
            flow.Render(fb);
            Assert.Equal(0, fb.ScrollY);
        }
    }
}
