using Th01.Core.Game;
using Th01.Core.Graphics;
using Th01.Core.Input;

namespace Th01.Core.Tests.Game;

/// <summary>Drawing of Sariel's entrance, backgrounds, sprites and defeat. Drawing only.</summary>
public class SarielDrawTests
{
    // Each picture is one flat colour so the tests can tell which one is on screen.
    sealed class Art : IGameArt
    {
        static readonly Dictionary<string, byte> Pictures = new()
        {
            ["BOSS6_L.GRP"] = 1, ["BOSS6_H.GRP"] = 2, ["BOSS6_A1.GRP"] = 3, ["BOSS6_A2.GRP"] = 4, ["BOSS6_A3.GRP"] = 5,
            ["BOSS6_A4.GRP"] = 6, ["BOSS6_A.GRP"] = 8, ["BOSS6_A5.GRP"] = 9, ["BOSS6_A6.GRP"] = 10,
        };
        public int FrameCount(string sheet) => 0;
        public bool TryGetBackground(out ArtFrame art) { art = default; return false; }
        public bool TryGetFrame(string sheet, int frame, out ArtFrame art)
        {
            art = default;
            if (!Pictures.TryGetValue(sheet, out byte c)) return false;
            var px = new byte[Framebuffer.Width * Framebuffer.Height];
            Array.Fill(px, c);
            art = new ArtFrame(Framebuffer.Width, Framebuffer.Height, px, Array.Empty<byte>(), Array.Empty<byte>());
            return true;
        }
    }

    static StageSetup Stage() => new()
    {
        StageId = Sariel.Id, Route = 0,
        Cards = new bool[StageSetup.Rows, StageSetup.Cols],
        Obstacles = new byte[StageSetup.Rows, StageSetup.Cols],
    };

    static GameSession Session(IGameArt? art, bool soft = false) =>
        new(new GameOptions { FrameRand = 5, Stage = Stage(), Lives = 3, Bombs = 1 }, null, art, visuals: new VisualOptions { SoftFlashes = soft });

    static Framebuffer Render(GameSession g)
    {
        var fb = new Framebuffer();
        g.Render(fb);
        return fb;
    }

    static void RunTo(GameSession g, Sariel s, int tick)
    {
        int guard = 0;
        while (s.EntranceTick < tick && guard++ < 5000) g.Step(GameButtons.None);
        Assert.Equal(tick, s.EntranceTick);
    }

    static void ToPlaying(GameSession g)
    {
        while (g.Phase == SessionPhase.BossEntrance) g.Step(GameButtons.None);
        g.Step(GameButtons.None); g.Step(GameButtons.None);
    }

    [Fact]
    public void The_entrance_shows_only_pictures_scrolling_then_a_diagonal_wipe_then_rings_and_no_hud()
    {
        var g = Session(new Art());
        var s = (Sariel)g.World.Boss!;
        RunTo(g, s, 100);
        var scroll = Render(g);
        Assert.Equal(300, Sariel.ScrollTicks - 100);
        Assert.Equal(300, scroll.ScrollY);                         // line 400 - tick
        RunTo(g, s, Sariel.ScrollTicks + 60);
        var wipe = Render(g);
        Assert.Equal(3, wipe.GetPixel(0, 0));                      // the first diagonal is already the new picture
        Assert.Equal(2, wipe.GetPixel(639, 399));                  // the far corner is still the old one
        RunTo(g, s, Sariel.EntranceTotal - 80);
        var rings = Render(g);
        int ringPixels = 0;
        for (int y = 0; y < Framebuffer.Height; y++) for (int x = 0; x < Framebuffer.Width; x++) if (rings.GetPixel(x, y) == (3 ^ 2)) ringPixels++;
        Assert.True(ringPixels > 1000);
        var g2 = Session(new Art());
        var s2 = (Sariel)g2.World.Boss!;
        var allowed = new HashSet<byte> { 0, 1, 2, 3, 3 ^ 2 };
        for (int tick = 1; tick < Sariel.EntranceTotal; tick += 41)
        {
            RunTo(g2, s2, tick);
            var fb = Render(g2);
            for (int y = 0; y < Framebuffer.Height; y += 3)
                for (int x = 0; x < Framebuffer.Width; x += 3)
                    Assert.True(allowed.Contains(fb.GetPixel(x, y)), $"stray pixel {fb.GetPixel(x, y)} at ({x},{y}) in tick {tick}");
        }
    }

    [Fact]
    public void The_first_form_shows_the_current_background_and_wipes_to_the_next_one_in_the_transition_phases()
    {
        var g = Session(new Art());
        var s = (Sariel)g.World.Boss!;
        ToPlaying(g);
        Assert.Equal(3, Render(g).GetPixel(10, 100));              // BOSS6_A1
        s.Phase = 2; s.PhaseFrame = 30;
        Assert.Equal(3, Render(g).GetPixel(10, 100));              // still waiting
        s.Phase = 6; s.BackgroundId = 2;                           // A3 -> A4
        s.PhaseFrame = 0;
        int guard = 0;
        bool sawBoth = false;
        while (s.Phase == 6 && guard++ < 500)
        {
            g.Step(GameButtons.None);
            if (s.TransitionProgress is > 0.3 and < 0.4)
            {
                var fb = Render(g);
                sawBoth |= fb.GetPixel(10, 100) == 6 && fb.GetPixel(600, 100) == 5;   // new picture on the left, old on the right
            }
        }
        Assert.True(sawBoth);
        Assert.Equal(3, s.BackgroundId);
        Assert.Equal(6, Render(g).GetPixel(10, 100));
    }

    [Fact]
    public void The_second_form_uses_the_last_picture_and_the_defeat_shakes_and_fades()
    {
        var g = Session(new Art());
        var s = (Sariel)g.World.Boss!;
        ToPlaying(g);
        s.Phase = Sariel.PhaseForm2; s.Hp = 6; s.PatternCur = 0; s.PhaseFrame = 1;
        Assert.Equal(10, Render(g).GetPixel(100, 100));
        s.Hp = 0;
        g.Step(GameButtons.None); g.Step(GameButtons.None);
        Assert.Equal(SessionPhase.BossDefeat, g.Phase);
        Assert.NotEqual(0, Render(g).ScrollY);                     // shaking
        static int Brightness(Framebuffer f)
        {
            int sum = 0;
            for (int c = 0; c < Framebuffer.ColorCount; c++) { f.GetPalette4(c, out int r, out int gg, out int b); sum += r + gg + b; }
            return sum;
        }
        int before = Brightness(Render(g));
        int guard = 0;
        while (g.DefeatElapsed < 2 * 210 && guard++ < 9000) g.Step(GameButtons.None);
        Assert.True(Brightness(Render(g)) < before);                // the palette has faded towards black
    }

    [Fact]
    public void The_shield_wand_and_birds_have_fallback_boxes_without_art()
    {
        var g = Session(null);
        var s = (Sariel)g.World.Boss!;
        ToPlaying(g);
        s.Phase = 1; s.PatternCur = 2; s.PhaseFrame = 10;
        var fb = Render(g);
        Assert.NotEqual(0, fb.GetPixel(Sariel.ShieldLeft + 5, Sariel.ShieldTop + 5));     // the shield box
        s.WandStage = 2;
        fb = Render(g);
        Assert.NotEqual(0, fb.GetPixel(Sariel.WandLeft + 50, Sariel.WandTop + 10));       // the raised wand box
        s.BirdAlive[0] = true; s.BirdHatch[0] = 0; s.BirdLeft[0] = 100 << 4; s.BirdTop[0] = 150 << 4;
        fb = Render(g);
        Assert.Equal(15, fb.GetPixel(116, 166));                                          // the bird square in colour 15
    }

    [Fact]
    public void Drawing_never_changes_the_simulation()
    {
        ulong Run(IGameArt? art, bool render)
        {
            var g = Session(art);
            var fb = new Framebuffer();
            for (int i = 0; i < Sariel.EntranceTotal + 3000; i++) { g.Step(i % 40 < 20 ? GameButtons.Left : GameButtons.Right); if (render && i % 7 == 0) g.Render(fb); }
            return g.StateHash();
        }
        var baseline = Run(null, false);
        Assert.Equal(baseline, Run(null, true));
        Assert.Equal(baseline, Run(new Art(), true));
    }
}
