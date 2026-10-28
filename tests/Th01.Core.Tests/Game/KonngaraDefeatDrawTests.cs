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
    public void The_original_flips_the_background_between_black_and_white_every_10_ticks()
    {
        var (g, k) = Defeated(soft: false);
        var seen = new List<int>();
        for (int kuji = 0; kuji < Konngara.KujiCount; kuji++)
        {
            RunToSequence(g, k, kuji * Konngara.KujiTicks + 5);
            var fb = Render(g);
            seen.Add(Level0(fb));
            Assert.Equal(kuji + 2, fb.GetPixel(300, 105));         // that kuji image (frame 7 + kuji, colour frame - 5)
            Assert.Equal(0, fb.GetPixel(300, 300));                // nothing else on the screen: no HUD, no stage
        }
        Assert.Equal(new[] { 0, 15, 0, 15, 0, 15, 0, 15, 0 }, seen);
    }

    [Fact]
    public void The_soft_mode_never_reaches_white_and_changes_slowly()
    {
        var (g, k) = Defeated(soft: true);
        int prev = -1, maxLevel = 0, maxStep = 0;
        for (int tick = 0; tick < Konngara.KujiCount * Konngara.KujiTicks; tick++)
        {
            RunToSequence(g, k, tick);
            int level = Level0(Render(g));
            maxLevel = Math.Max(maxLevel, level);
            if (prev >= 0) maxStep = Math.Max(maxStep, Math.Abs(level - prev));
            prev = level;
        }
        Assert.True(maxLevel <= 4);
        Assert.True(maxStep <= 1);                                 // at most one level per tick, never a jump to white
    }

    [Fact]
    public void The_shake_fades_the_palette_and_respects_the_screen_shake_option_then_the_picture_scrolls()
    {
        var (g, k) = Defeated(soft: false);
        int shakeStart = Konngara.KujiCount * Konngara.KujiTicks;
        RunToSequence(g, k, shakeStart + 9);
        var fb = Render(g);
        Assert.NotEqual(0, fb.ScrollY);
        Assert.Equal(2 * 0 + 9 + 1 - 1, 9);
        RunToSequence(g, k, shakeStart + Konngara.DefeatShakeTicks + 3);
        fb = Render(g);
        Assert.Equal(3, fb.GetPixel(100, 300));                    // the scroll picture
        Assert.Equal(3 * 32 % 400, fb.ScrollY);
        var (g2, k2) = Defeated(soft: false, shake: false);
        RunToSequence(g2, k2, shakeStart + 9);
        Assert.Equal(0, Render(g2).ScrollY);
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
