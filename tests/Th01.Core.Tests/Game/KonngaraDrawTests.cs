using Th01.Core.Game;
using Th01.Core.Graphics;
using Th01.Core.Input;

namespace Th01.Core.Tests.Game;

/// <summary>Drawing of Konngara's entrance (scroll, shake, pan, seal syllables), the slash images and the diamonds. Drawing only.</summary>
public class KonngaraDrawTests
{
    sealed class Art : IGameArt
    {
        public int FrameCount(string sheet) => 0;
        public bool TryGetBackground(out ArtFrame art) { art = default; return false; }
        public bool TryGetFrame(string sheet, int frame, out ArtFrame art)
        {
            art = default;
            byte? fill = sheet switch
            {
                "BOSS7_D1.GRP" => 3,
                "BOSS8_A1.GRP" => 4,
                "BOSS8_D1.GRP" => 9,
                "BOSS8.GRZ" => (byte)(10 + frame),
                _ => null,
            };
            if (fill == null) return false;
            var px = new byte[Framebuffer.Width * Framebuffer.Height];
            if (sheet is "BOSS8_D1.GRP" or "BOSS8.GRZ")
                for (int y = 0; y < 8; y++) for (int x = 0; x < 640; x++) px[((sheet == "BOSS8.GRZ" ? 200 : 100) + frame * 10 + y) * 640 + x] = fill.Value;   // a band, the rest transparent
            else Array.Fill(px, fill.Value);
            // like the real files: the key is index 15 and the platform hands it over as alpha 0
            byte[] alpha = Array.Empty<byte>();
            if (sheet is "BOSS8_D1.GRP" or "BOSS8.GRZ")
            {
                alpha = px.Select(v => (byte)(v == 0 ? 0 : 1)).ToArray();
                for (int i = 0; i < px.Length; i++) if (px[i] == 0) px[i] = 15;
            }
            art = new ArtFrame(Framebuffer.Width, Framebuffer.Height, px, alpha, Array.Empty<byte>());
            return true;
        }
    }

    static StageSetup Boss() => new()
    {
        StageId = Konngara.Id, Route = 1,
        Cards = new bool[StageSetup.Rows, StageSetup.Cols],
        Obstacles = new byte[StageSetup.Rows, StageSetup.Cols],
    };

    static GameSession Session(IGameArt? art, bool soft = false) =>
        new(new GameOptions { FrameRand = 2, Stage = Boss(), Lives = 3, Bombs = 1 }, null, art, visuals: new VisualOptions { SoftFlashes = soft });

    static Framebuffer Render(GameSession g)
    {
        var fb = new Framebuffer();
        g.Render(fb);
        return fb;
    }

    static void RunTo(GameSession g, Konngara k, int tick)
    {
        int guard = 0;
        while (k.EntranceTick < tick && guard++ < 5000) g.Step(GameButtons.None);
        Assert.Equal(tick, k.EntranceTick);
    }

    [Fact]
    public void The_entrance_timeline_follows_the_original_loops()
    {
        var k = (Konngara)Session(null).World.Boss!;
        int total = Konngara.EntranceTotal;
        Assert.Equal(0, k.ViewAt(1).ScrollLine);
        Assert.True(k.ViewAt(1).PaletteSteps == 1 && k.ViewAt(9).PaletteSteps == 2);          // one fade step every 8 ticks
        for (int t = 1; t <= total; t++) Assert.InRange(k.ViewAt(t).ScrollLine, 0, 416);
        var last = k.ViewAt(total);
        Assert.Equal((0, 16, 15, Konngara.SealSteps, true), (last.ScrollLine, last.PanLine, last.PaletteSteps, last.SealStep, last.Revealed));
        // the pan: 13 steps of 32 lines, 400 down to 16; the shakes before and after alternate 416 / 384 and 416 / 400
        var pans = Enumerable.Range(1, total).Select(t => k.ViewAt(t)).Where(v => v.PanLine == v.ScrollLine && !v.Revealed).Select(v => v.PanLine).ToArray();
        Assert.Equal(Enumerable.Range(0, 13).Select(n => 400 - 32 * n), pans);
        int belowStart = total - (Konngara.ShakeBelowTicks + Konngara.PanTicks + Konngara.ShakeTicks + Konngara.WaitSealTicks + Konngara.SealSteps * Konngara.SealStepTicks) + 1;
        var shakesBelow = Enumerable.Range(belowStart, 4).Select(t => k.ViewAt(t).ScrollLine).ToArray();
        Assert.Equal(new[] { 416, 384, 416, 384 }, shakesBelow);
        // seal fade steps 0..16 in blocks of 10 ticks, only at the end
        Assert.Equal(-1, k.ViewAt(total - Konngara.SealSteps * Konngara.SealStepTicks - 1).SealStep);
        Assert.Equal(0, k.ViewAt(total - Konngara.SealSteps * Konngara.SealStepTicks + 1).SealStep);
    }

    [Fact]
    public void Scroll_pan_and_seal_pictures_are_drawn_in_turn_and_the_view_shifts()
    {
        var g = Session(new Art());
        var k = (Konngara)g.World.Boss!;
        RunTo(g, k, 5);
        var fb = Render(g);
        Assert.Equal(3, fb.GetPixel(300, 300));                   // first picture, nothing of the boss yet
        int scroll = k.ViewAt(5).ScrollLine;
        Assert.Equal(scroll % 400, fb.ScrollY);
        int panStart = k.ViewAt(1000).PanLine;                     // placeholder to keep the probe simple
        Assert.True(panStart <= 400);
        // in the pan, rows from the pan line down show the boss picture
        int t = 1;
        while (k.ViewAt(t).PanLine == 400) t++;
        t += 2;
        RunTo(g, k, t);
        fb = Render(g);
        int line = k.ViewAt(t).PanLine;
        Assert.Equal(4, fb.GetPixel(300, line + 5 > 399 ? 399 : line + 5));
        Assert.Equal(3, fb.GetPixel(300, Math.Max(0, line - 5)));
        // the end of the entrance: seal syllables drawn over the boss picture
        RunTo(g, k, Konngara.EntranceTotal - 1);
        fb = Render(g);
        Assert.Equal(9, fb.GetPixel(300, 100));
        Assert.Equal(4, fb.GetPixel(300, 300));
    }

    [Fact]
    public void The_seal_colour_fades_from_white_to_blue_and_soft_flashes_stay_light()
    {
        int Red(bool soft, int tick)
        {
            var g = Session(new Art(), soft);
            var k = (Konngara)g.World.Boss!;
            RunTo(g, k, tick);
            var fb = Render(g);
            fb.GetPalette4(9, out int r, out _, out _);
            return r;
        }
        int sealStart = Konngara.EntranceTotal - Konngara.SealSteps * Konngara.SealStepTicks;
        Assert.Equal(15, Red(false, sealStart + 1));
        Assert.True(Red(false, Konngara.EntranceTotal - 1) < Red(false, sealStart + 1));
        Assert.True(Red(true, sealStart + 1) <= 3);
    }

    [Fact]
    public void Slash_images_diamonds_and_syllables_draw_during_the_fight_with_and_without_art()
    {
        foreach (bool withArt in new[] { false, true })
        {
            var g = Session(withArt ? new Art() : null);
            var k = (Konngara)g.World.Boss!;
            while (g.Phase == SessionPhase.BossEntrance) g.Step(GameButtons.None);
            k.SlashImage = 2;
            k.DiamondsActive = true;
            k.DiamondLeft[0] = 100; k.DiamondTop[0] = 150;
            var before = g.StateHash();
            var fb = Render(g);
            Assert.Equal(before, g.StateHash());
            Assert.NotEqual(0, fb.GetPixel(103, 153));            // the diamond (shape cel or box)
            if (withArt)
            {
                Assert.Equal(10, fb.GetPixel(300, 200 + 5));         // slash image 0 band
                Assert.Equal(12, fb.GetPixel(300, 220 + 5));         // slash image 2 band
                Assert.Equal(9, fb.GetPixel(500, 100 + 2));          // seal syllable drawn over (the later pictures win)
            }
        }
    }

    [Fact]
    public void Drawing_the_entrance_never_changes_the_simulation()
    {
        ulong Run(IGameArt? art, bool render)
        {
            var g = Session(art);
            var fb = new Framebuffer();
            for (int i = 0; i < 900; i++) { g.Step(GameButtons.None); if (render) g.Render(fb); }
            return g.StateHash();
        }
        var baseline = Run(null, false);
        Assert.Equal(baseline, Run(null, true));
        Assert.Equal(baseline, Run(new Art(), true));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Palette_entry_9_is_continuous_from_the_end_of_the_entrance_into_the_fight(bool soft)
    {
        var g = Session(new Art(), soft);
        var k = (Konngara)g.World.Boss!;
        RunTo(g, k, Konngara.EntranceTotal - 1);
        (int, int, int) Pal9()
        {
            var fb = Render(g);
            fb.GetPalette4(9, out int r, out int gg, out int b);
            return (r, gg, b);
        }
        var lastEntrance = Pal9();
        Assert.Equal((0, 9, 10), lastEntrance);                   // the end of the fade: dark blue-green
        while (g.Phase == SessionPhase.BossEntrance) g.Step(GameButtons.None);
        for (int i = 0; i < 20; i++)
        {
            Assert.Equal(lastEntrance, Pal9());                    // the same colour all through the quiet part of the fight
            g.Step(GameButtons.None);
        }
    }

    [Theory]
    [InlineData(false, 15)]
    [InlineData(true, 3)]
    public void The_seal_phases_flash_entry_9_from_frame_50_and_fade_it_back(bool soft, int expectedRedAt50)
    {
        var g = Session(new Art(), soft);
        var k = (Konngara)g.World.Boss!;
        while (g.Phase == SessionPhase.BossEntrance) g.Step(GameButtons.None);
        int Red()
        {
            var fb = Render(g);
            fb.GetPalette4(9, out int r, out _, out _);
            return r;
        }
        k.Phase = 2; k.PhaseFrame = 49;
        Assert.Equal(0, Red());
        k.PhaseFrame = 50;
        Assert.Equal(expectedRedAt50, Red());
        k.PhaseFrame = 60;                                         // three steps later
        int later = Red();
        Assert.True(later < expectedRedAt50 || soft && later <= expectedRedAt50);
        k.PhaseFrame = 50 + 4 * 16;
        Assert.Equal(0, Red());
    }
}
