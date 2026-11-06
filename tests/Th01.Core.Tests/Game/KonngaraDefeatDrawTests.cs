using Th01.Core.Game;
using Th01.Core.Graphics;
using Th01.Core.Input;

namespace Th01.Core.Tests.Game;

/// <summary>Konngara's defeat sequence on screen: nine kuji images over a flipping background, a shake and the final scroll.</summary>
public class KonngaraDefeatDrawTests
{
    sealed class Art : IGameArt
    {
        public int FrameCount(string sheet) => 0;
        public bool TryGetBackground(out ArtFrame art) { art = default; return false; }
        public bool TryGetFrame(string sheet, int frame, out ArtFrame art)
        {
            art = default;
            var px = new byte[Framebuffer.Width * Framebuffer.Height];
            byte[] alpha = Array.Empty<byte>();
            if (sheet == "BOSS8.GRZ" && frame >= 7)
            {
                for (int y = 100; y < 110; y++) for (int x = 0; x < 640; x++) px[y * 640 + x] = (byte)(frame - 5);   // a band, colour 2..10
                alpha = px.Select(v => (byte)(v == 0 ? 0 : 1)).ToArray();
            }
            else if (sheet == "BOSS7_D1.GRP") Array.Fill(px, (byte)3);
            else return false;
            art = new ArtFrame(Framebuffer.Width, Framebuffer.Height, px, alpha, Array.Empty<byte>());
            return true;
        }
    }

    static StageSetup Stage() => new()
    {
        StageId = Konngara.Id, Route = 1,
        Cards = new bool[StageSetup.Rows, StageSetup.Cols],
        Obstacles = new byte[StageSetup.Rows, StageSetup.Cols],
    };

    static (GameSession g, Konngara k) Defeated(bool soft, bool shake = true, IGameArt? art = null)
    {
        var g = new GameSession(new GameOptions { FrameRand = 12, Stage = Stage(), Lives = 3, Bombs = 1 }, null, art ?? new Art(),
            visuals: new VisualOptions { SoftFlashes = soft, ScreenShake = shake });
        for (int i = 0; i < Konngara.EntranceTotal + 5; i++) g.Step(GameButtons.None);
        var k = (Konngara)g.World.Boss!;
        k.Phase = 7; k.Hp = 0;
        g.Step(GameButtons.None);
        Assert.Equal(SessionPhase.BossDefeat, g.Phase);
        return (g, k);
    }

    static void RunToSequence(GameSession g, Konngara k, int tick)
    {
        int target = tick, guard = 0;
        while (g.DefeatElapsed < target && guard++ < 5000) g.Step(GameButtons.None);
        Assert.Equal(target, g.DefeatElapsed);
    }

    static Framebuffer Render(GameSession g)
    {
        var fb = new Framebuffer();
        g.Render(fb);
        return fb;
    }

    static int Level0(Framebuffer fb)
    {
        fb.GetPalette4(0, out int r, out _, out _);
        return r;
    }

    [Fact]
    public void The_original_blinks_the_background_four_times_during_the_kuji_as_measured()
    {
        var (g, k) = Defeated(soft: false);
        for (int kuji = 0; kuji < Konngara.KujiCount; kuji++)
        {
            RunToSequence(g, k, kuji * Konngara.KujiTicks + 5);
            var fb = Render(g);
            Assert.Equal(kuji + 2, fb.GetPixel(300, 105));         // that kuji image (frame 7 + kuji, colour frame - 5)
            Assert.Equal(0, fb.GetPixel(300, 300));                // nothing else on the screen: no HUD, no stage
        }
        // lit at 10, 30, 50, 70 (12 ticks) and off at 22, 42, 62, 82 (T5: VSync 1272/1292/1312/1332 on, 1284/1304/1324/1344 off)
        var lit = new List<int>();
        (g, k) = Defeated(soft: false);
        for (int tick = 0; tick < 90; tick++) { RunToSequence(g, k, tick); if (Level0(Render(g)) == 15) lit.Add(tick); }
        Assert.Equal(48, lit.Count);
        Assert.Equal(new[] { 10, 30, 50, 70 }, new[] { lit[0], lit[12], lit[24], lit[36] });
        Assert.DoesNotContain(22, lit); Assert.DoesNotContain(82, lit);
    }

    [Fact]
    public void The_soft_mode_never_blinks_the_background()
    {
        var (g, k) = Defeated(soft: true);
        for (int tick = 0; tick < Konngara.KujiCount * Konngara.KujiTicks; tick++)
        {
            RunToSequence(g, k, tick);
            Assert.Equal(0, Level0(Render(g)));
        }
    }

    [Fact]
    public void The_defeat_follows_the_measured_timeline()
    {
        var (g, k) = Defeated(soft: false);
        int fadeStart = Konngara.KujiCount * Konngara.KujiTicks + Konngara.KujiHoldTicks;
        Assert.Equal(548, Konngara.DefeatSequenceTicks);
        // the fade to black with the shake (ScreenShake option respected)
        RunToSequence(g, k, fadeStart + 9);
        Assert.NotEqual(0, Render(g).ScrollY);
        var (g2, k2) = Defeated(soft: false, shake: false);
        RunToSequence(g2, k2, fadeStart + 9);
        Assert.Equal(0, Render(g2).ScrollY);
        // black gap, then the stage picture with the palette coming in, scrolling 32 lines a tick
        RunToSequence(g, k, 152);
        var fb = Render(g);
        Assert.Equal(0, fb.GetPixel(100, 300));
        RunToSequence(g, k, 160);
        Assert.Equal(3, Render(g).GetPixel(100, 300));             // the picture is there (palette still black)
        RunToSequence(g, k, Konngara.DefeatScrollStart + 3);
        fb = Render(g);
        Assert.Equal(3 * 32 % 400, fb.ScrollY);
        int Bright(Framebuffer f) { int sum = 0; for (int c = 0; c < Framebuffer.ColorCount; c++) { f.GetPalette4(c, out int r, out int gg, out int b); sum += r + gg + b; } return sum; }
        RunToSequence(g, k, 250); int fullIn = Bright(Render(g));
        RunToSequence(g, k, 280); Assert.True(Bright(Render(g)) >= fullIn);
        RunToSequence(g, k, 400); Assert.True(Bright(Render(g)) < fullIn);          // fading out again from 318
        RunToSequence(g, k, Konngara.DefeatBlankAt + 2);
        fb = Render(g);
        Assert.Equal(0, Bright(fb) - Bright(fb) + fb.GetPixel(100, 300));          // the erased screen
    }

    [Fact]
    public void Duration_and_state_do_not_depend_on_the_drawing_options()
    {
        ulong Run(bool soft, bool render)
        {
            var (g, k) = Defeated(soft);
            var fb = new Framebuffer();
            int guard = 0;
            while (g.Phase == SessionPhase.BossDefeat && guard++ < 5000) { g.Step(GameButtons.None); if (render) g.Render(fb); }
            Assert.Equal(k.DefeatTicks(), g.DefeatTotal);
            return g.StateHash();
        }
        var baseline = Run(false, false);
        Assert.Equal(baseline, Run(false, true));
        Assert.Equal(baseline, Run(true, true));
    }
}
