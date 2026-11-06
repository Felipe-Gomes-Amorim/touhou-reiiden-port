using Th01.Core.Game;
using Th01.Core.Graphics;
using Th01.Core.Input;

namespace Th01.Core.Tests.Game;

/// <summary>
/// Drawing of Sariel: entrance, backgrounds, transformation, sprites, defeat. Every effect that is gradual in the original
/// is checked tick by tick (not just at its two ends). Drawing only.
/// </summary>
public class SarielDrawTests
{
    // Each picture is one flat colour so the tests can tell which one is on screen. Palette entry 2 differs per picture.
    sealed class Art : IGameArt
    {
        static readonly Dictionary<string, byte> Pictures = new()
        {
            ["BOSS6_L.GRP"] = 1, ["BOSS6_H.GRP"] = 2, ["BOSS6_A1.GRP"] = 3, ["BOSS6_A2.GRP"] = 4, ["BOSS6_A3.GRP"] = 5,
            ["BOSS6_A4.GRP"] = 6, ["BOSS6_A5.GRP"] = 9, ["BOSS6_A6.GRP"] = 11,
        };
        public int FrameCount(string sheet) => 0;
        public bool TryGetBackground(out ArtFrame art) { art = default; return false; }
        public bool TryGetFrame(string sheet, int frame, out ArtFrame art)
        {
            art = default;
            if (!Pictures.TryGetValue(sheet, out byte c)) return false;
            var px = new byte[Framebuffer.Width * Framebuffer.Height];
            Array.Fill(px, c);
            var pal = new byte[48];
            pal[2 * 3] = (byte)(sheet == "BOSS6_A5.GRP" ? 0 : 15);         // entry 2: red in the pictures, blue in the dark one
            pal[2 * 3 + 2] = (byte)(sheet == "BOSS6_A5.GRP" ? 15 : 0);
            art = new ArtFrame(Framebuffer.Width, Framebuffer.Height, px, Array.Empty<byte>(), pal);
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
    }

    static int Count(Framebuffer fb, byte color, int step = 1)
    {
        int n = 0;
        for (int y = 0; y < Framebuffer.Height; y += step)
            for (int x = 0; x < Framebuffer.Width; x += step)
                if (fb.GetPixel(x, y) == color) n++;
        return n;
    }

    // ---- the entrance: Sariel appears ----

    [Fact]
    public void The_entrance_lengths_come_from_the_original_code()
    {
        Assert.Equal(400, Sariel.ScrollTicks);
        Assert.Equal(130, Sariel.TransitionFrames);
        Assert.Equal(3, Sariel.TransitionTicksPerFrame);            // measured by T5: delay(40) = 3 VSyncs per frame
        Assert.Equal(375, Sariel.TransitionTicks);                  // from the end of the scroll to the HUD and the rings (VSync 796)
        Assert.InRange(Sariel.RingsTicks, 38, 46);                  // measured: VSync 796 to the first game tick at ~837
        Assert.Equal(817, Sariel.EntranceTotal);                    // measured: 20 + 817 = 837
        Assert.Equal(Sariel.ScrollTicks + Sariel.TransitionTicks + Sariel.RingsTicks, Sariel.EntranceTotal);
        Assert.Equal(2, Sariel.TransitionFrameAt(0));                  // measured: frame f is shown from tick 400 - 6 + 3 f
        Assert.Equal(127, Sariel.TransitionFrameAt(Sariel.TransitionTicks));
    }

    [Fact]
    public void The_scroll_brings_in_the_high_picture_one_row_per_tick_while_the_view_follows()
    {
        var g = Session(new Art());
        var s = (Sariel)g.World.Boss!;
        int Rows(Framebuffer fb) { int n = 0; for (int y = 0; y < Framebuffer.Height; y++) if (fb.GetPixel(10, y) == 2) n++; return n; }
        int previousScroll = -1;
        for (int tick = 1; tick <= 60; tick++)
        {
            RunTo(g, s, tick);
            var fb = Render(g);
            Assert.Equal(tick, Rows(fb));                           // exactly one more row of the high picture per tick
            Assert.Equal(Framebuffer.Height - tick, fb.ScrollY);    // and the view moves up one line per tick
            Assert.NotEqual(previousScroll, fb.ScrollY);
            previousScroll = fb.ScrollY;
        }
    }

    [Fact]
    public void The_diagonal_transition_dithers_the_new_picture_in_frame_by_frame_with_two_waves()
    {
        var g = Session(new Art());
        var s = (Sariel)g.World.Boss!;
        int last = -1, increases = 0, maxJump = 0;
        for (int t = 1; t <= Sariel.TransitionTicks; t++)
        {
            RunTo(g, s, Sariel.ScrollTicks + t);
            int shown = Count(Render(g), 3, 2);
            Assert.True(shown >= last, $"the new picture went away at tick {t}");
            if (shown > last) increases++;
            if (last >= 0) maxJump = Math.Max(maxJump, shown - last);
            last = shown;
        }
        Assert.True(increases > 100, "the picture must come in over many ticks, not at once");
        Assert.Equal(3, Sariel.TransitionTicksPerFrame);
        Assert.True(maxJump < 320 * 200 / 4, "no single tick may show a quarter of the picture or more");
        Assert.Equal(320 * 200, last);                              // sampled every second dot in each axis: all of them the new picture

        // the dither: after the first wave reached a square it shows every other dot of the new picture (mask 1: even dots)
        var g2 = Session(new Art());
        var s2 = (Sariel)g2.World.Boss!;
        RunTo(g2, s2, Sariel.ScrollTicks + 14);                     // frame 6: squares with column + row <= 6 are half new
        var fb2 = Render(g2);
        Assert.Equal(3, fb2.GetPixel(0, 0));                        // (0 + 0) even: the new picture
        Assert.Equal(2, fb2.GetPixel(1, 0));                        // (1 + 0) odd: still the old one
        Assert.Equal(2, fb2.GetPixel(300, 300));                    // far from both corners: untouched
    }

    [Fact]
    public void The_rings_grow_every_two_ticks_until_every_square_has_left_the_screen()
    {
        var g = Session(new Art());
        var s = (Sariel)g.World.Boss!;
        int start = Sariel.ScrollTicks + Sariel.TransitionTicks;
        RunTo(g, s, start + 6);
        var a = Render(g);
        RunTo(g, s, start + 20);
        var b = Render(g);
        int changed = 0;
        for (int y = 0; y < Framebuffer.Height; y += 2) for (int x = 0; x < Framebuffer.Width; x += 2) if (a.GetPixel(x, y) != b.GetPixel(x, y)) changed++;
        Assert.True(changed > 500, $"the rings must have moved outwards (changed={changed}, a!=3: {Count(a, 3, 2)}, b!=3: {Count(b, 3, 2)}, tick={s.EntranceTick}, phase={g.Phase}, total={Sariel.EntranceTotal}, start={start})");
        RunTo(g, s, Sariel.EntranceTotal - 1);
        var late = Render(g);
        int ringPixels = 0;
        for (int y = 0; y < Framebuffer.Height; y++) for (int x = 0; x < Framebuffer.Width; x++) if (late.GetPixel(x, y) != 3) ringPixels++;
        Assert.True(ringPixels < 8000, "by the end only a few squares of the rings are left on the screen");
    }

    [Fact]
    public void The_entrance_shows_only_pictures_and_rings_never_the_hud()
    {
        var g = Session(new Art());
        var s = (Sariel)g.World.Boss!;
        var allowed = new HashSet<byte> { 0, 1, 2, 3, 3 ^ 2 };
        for (int tick = 1; tick <= Sariel.ScrollTicks + Sariel.TransitionTicks; tick += 41)
        {
            RunTo(g, s, tick);
            var fb = Render(g);
            for (int y = 0; y < Framebuffer.Height; y += 3)
                for (int x = 0; x < Framebuffer.Width; x += 3)
                    Assert.True(allowed.Contains(fb.GetPixel(x, y)), $"stray pixel {fb.GetPixel(x, y)} at ({x},{y}) in tick {tick}");
        }
        // the HUD comes back with the rings (VSync 796 in the original)
        RunTo(g, s, Sariel.ScrollTicks + Sariel.TransitionTicks + 3);
        var hud = Render(g);
        int other = 0;
        for (int y = 0; y < 64; y++) for (int x = 0; x < Framebuffer.Width; x++) if (!allowed.Contains(hud.GetPixel(x, y))) other++;
        Assert.True(other > 0);
    }

    // ---- the fight begins: the bar fills up and the shield and dress pop in ----

    static int HpPoints(Framebuffer fb)
    {
        int n = 0;
        for (int p = 0; p < 20; p++) if (fb.GetPixel(128 + p * 8 + 2, 52) is 7 or 10) n++;
        return n;
    }

    [Fact]
    public void The_hp_bar_fills_up_one_point_per_tick_at_the_start_of_each_form()
    {
        var g = Session(new Art());
        var s = (Sariel)g.World.Boss!;
        ToPlaying(g);
        g.Step(GameButtons.None);                                   // phase 0 -> 1
        int previous = 0;
        for (int tick = 1; tick <= 18; tick++)
        {
            g.Step(GameButtons.None);
            int points = HpPoints(Render(g));
            Assert.Equal(tick, points);
            previous = points;
        }
        Assert.Equal(18, previous);
        g.Step(GameButtons.None);
        Assert.Equal(18, HpPoints(Render(g)));

        s.Phase = Sariel.PhaseForm2; s.Hp = 6; s.PatternCur = 0; s.PhaseFrame = 0; s.InitialHpRendered = false;
        for (int tick = 1; tick <= 6; tick++)
        {
            g.Step(GameButtons.None);
            Assert.Equal(tick, HpPoints(Render(g)));
        }
    }

    [Fact]
    public void The_shield_and_the_dress_pop_in_on_their_first_animation_frames_and_the_shield_leaves_with_the_picture()
    {
        var g = Session(null);
        var s = (Sariel)g.World.Boss!;
        ToPlaying(g);
        g.Step(GameButtons.None);
        bool ShieldBox() => Render(g).GetPixel(Sariel.ShieldLeft + 5, Sariel.ShieldTop + 5) != 0;
        bool DressBox() => Render(g).GetPixel(Sariel.DressLeft + 5, Sariel.DressTop + 5) != 0;
        for (int f = 1; f < 10; f++) { g.Step(GameButtons.None); Assert.False(ShieldBox(), $"shield at frame {f}"); }
        g.Step(GameButtons.None);                                   // frame 10
        Assert.True(ShieldBox());
        for (int f = 11; f < 15; f++) g.Step(GameButtons.None);
        Assert.False(DressBox());
        g.Step(GameButtons.None);                                   // frame 15
        Assert.True(DressBox());
        // the picture is replaced at the end of a transition: the shield is gone until its next first frame
        s.Phase = 2; s.PhaseFrame = 0;
        int guard = 0;
        while (s.Phase == 2 && guard++ < 500) g.Step(GameButtons.None);
        Assert.Equal(3, s.Phase);
        Assert.False(s.ShieldShown);
        Assert.True(s.DressShown);                                  // dress_render_both() ran in the same call
    }

    // ---- the background transitions: gusts, then the new picture at once ----

    [Fact]
    public void A_background_transition_sweeps_gusts_of_streaks_and_swaps_the_picture_only_at_the_end()
    {
        var g = Session(new Art());
        var s = (Sariel)g.World.Boss!;
        ToPlaying(g);
        g.Step(GameButtons.None);
        s.Phase = 2; s.BackgroundId = 0; s.PhaseFrame = 0;
        int guard = 0, streakTicks = 0;
        var seen = new HashSet<int>();
        while (s.Phase == 2 && guard++ < 500)
        {
            g.Step(GameButtons.None);
            if (s.Phase != 2) break;
            var fb = Render(g);
            Assert.Equal(3, fb.GetPixel(300, 330));                 // the old picture (index 3) stays under the streaks
            if (s.PhaseFrame >= 50 && s.GustId < 3)
            {
                seen.Add(s.GustCalls + 100 * s.GustId);
                if (fb.GetPixel(300, 330) == 3 && Count(fb, 3, 2) < 320 * 200) streakTicks++;
            }
        }
        Assert.Equal(1, s.BackgroundId);                            // the picture changed exactly once, at the end
        Assert.True(streakTicks > 50);                              // the gust paints over the picture while it moves
        Assert.True(seen.Count > 50);                               // calls moved along 3 gusts of 20
    }

    // ---- the transformation: form 1 falls and form 2 appears ----

    [Fact]
    public void The_first_form_falls_shaking_its_own_picture_then_swaps_to_form_2_at_once()
    {
        var g = Session(new Art());
        var s = (Sariel)g.World.Boss!;
        ToPlaying(g);
        g.Step(GameButtons.None);
        s.Phase = 5; s.BackgroundId = 0; s.Hp = 0;
        g.Step(GameButtons.None);
        Assert.Equal(Sariel.PhaseForm1Defeated, s.Phase);
        g.Step(GameButtons.None);
        // 200 frames of shaking the current picture
        var shakeLines = new HashSet<int>();
        for (int f = 1; f < Sariel.Form1DefeatShakeTicks; f++)
        {
            g.Step(GameButtons.None);
            var fb = Render(g);
            shakeLines.Add(fb.ScrollY);
            Assert.Equal(3, fb.GetPixel(300, 300));                 // the first form's own picture, not the dark one
        }
        Assert.Contains(16, shakeLines); Assert.Contains(386, shakeLines);
        // 10 more ticks with the old picture (the page transition runs unseen), then form 2 appears at once
        for (int t = 0; t < Sariel.Form1DefeatSwapTicks - 1; t++)
        {
            g.Step(GameButtons.None);
            Assert.Equal(3, Render(g).GetPixel(300, 300));
        }
        g.Step(GameButtons.None); g.Step(GameButtons.None);
        Assert.Equal(11, Render(g).GetPixel(300, 300));
        // form 2 appears all at once
        int guard = 0;
        while (s.Phase == Sariel.PhaseForm1Defeated && guard++ < 1000) g.Step(GameButtons.None);
        Assert.Equal(Sariel.PhaseForm2, s.Phase);
        Assert.Equal(11, Render(g).GetPixel(300, 300));
    }

    [Fact]
    public void Form_2_recomposes_without_the_HUD_which_returns_whole_at_the_end()
    {
        var g = Session(new Art());
        var s = (Sariel)g.World.Boss!;
        ToPlaying(g);
        g.Step(GameButtons.None);
        s.Phase = 5; s.BackgroundId = 0; s.Hp = 0;
        g.Step(GameButtons.None);
        int guard = 0;
        while (s.Phase == Sariel.PhaseForm1Defeated && s.PhaseFrame < Sariel.Form1DefeatShakeTicks + Sariel.Form1DefeatSwapTicks + 20 && guard++ < 2000) g.Step(GameButtons.None);
        Assert.Equal(Sariel.PhaseForm1Defeated, s.Phase);
        var bare = Render(g);
        for (int y = 0; y < 60; y += 4) for (int x = 0; x < Framebuffer.Width; x += 4) Assert.Equal(11, bare.GetPixel(x, y));   // form 2 picture only
        while (s.Phase == Sariel.PhaseForm1Defeated && guard++ < 4000) g.Step(GameButtons.None);
        Assert.Equal(Sariel.PhaseForm2, s.Phase);
        int other = 0; var back = Render(g);
        for (int y = 0; y < 60; y++) for (int x = 0; x < Framebuffer.Width; x++) if (back.GetPixel(x, y) != 11) other++;
        Assert.True(other > 0);                                      // the HUD is back all at once
    }

    [Fact]
    public void Soft_flashes_never_strobe_during_the_transformation()
    {
        var g = Session(new Art(), soft: true);
        var s = (Sariel)g.World.Boss!;
        ToPlaying(g);
        g.Step(GameButtons.None);
        s.Phase = 5; s.Hp = 0;
        g.Step(GameButtons.None); g.Step(GameButtons.None);
        int prev = -1, flips = 0;
        for (int f = 1; f < Sariel.Form1DefeatShakeTicks; f++)
        {
            g.Step(GameButtons.None);
            var fb = Render(g);
            fb.GetPalette4(15, out int r, out _, out _);
            if (prev >= 0 && r != prev) flips++;
            prev = r;
        }
        Assert.Equal(0, flips);                                     // palette entry 15 is not flashed in the soft mode
    }

    // ---- sprites, fallbacks and the final defeat ----

    [Fact]
    public void The_second_form_uses_the_last_picture_and_the_defeat_shakes_and_fades()
    {
        var g = Session(new Art());
        var s = (Sariel)g.World.Boss!;
        ToPlaying(g);
        g.Step(GameButtons.None);
        s.Phase = Sariel.PhaseForm2; s.Hp = 6; s.PatternCur = 0; s.PhaseFrame = 1;
        Assert.Equal(11, Render(g).GetPixel(100, 100));
        s.Hp = 0;
        g.Step(GameButtons.None); g.Step(GameButtons.None);
        Assert.Equal(SessionPhase.BossDefeat, g.Phase);
        for (int i = 0; i < 8; i++) g.Step(GameButtons.None);
        var shakes = new HashSet<int>(); for (int i = 0; i < 8; i++) { g.Step(GameButtons.None); shakes.Add(Render(g).ScrollY); }
        Assert.Contains(16, shakes); Assert.Contains(386, shakes);   // shaking, period of 4 ticks
        var hudless = Render(g);
        for (int y = 0; y < 60; y += 4) for (int x = 0; x < Framebuffer.Width; x += 4) Assert.Equal(11, hudless.GetPixel(x, y));   // the HUD is gone: only the form 2 picture is left up there
        static int Brightness(Framebuffer f)
        {
            int sum = 0;
            for (int c = 0; c < Framebuffer.ColorCount; c++) { f.GetPalette4(c, out int r, out int gg, out int b); sum += r + gg + b; }
            return sum;
        }
        int before = Brightness(Render(g));
        int guard = 0, previous = before, drops = 0;
        while (g.DefeatElapsed < Sariel.DefeatFadeStart + 60 && guard++ < 9000)
        {
            g.Step(GameButtons.None);
            int now = Brightness(Render(g));
            Assert.True(now <= previous);
            if (now < previous) drops++;
            previous = now;
        }
        Assert.True(previous < before);                             // the palette has faded towards black
        Assert.True(drops > 5);                                     // step by step, not in one go
        Assert.Equal(15, drops);                                    // 15 levels, one every 4 ticks
        while (g.DefeatElapsed < Sariel.DefeatShakeEnd && guard++ < 9000) g.Step(GameButtons.None);
        g.Step(GameButtons.None);
        var black = Render(g);
        Assert.Equal(0, black.ScrollY);                             // the scroll is back at 0 and the screen stays black
        Assert.Equal(0, Brightness(black));
        Assert.Equal(590, Sariel.DefeatTotal);
    }

    [Fact]
    public void The_shield_wand_and_birds_have_fallback_boxes_without_art()
    {
        var g = Session(null);
        var s = (Sariel)g.World.Boss!;
        ToPlaying(g);
        g.Step(GameButtons.None);
        s.Phase = 1; s.PatternCur = 2; s.PhaseFrame = 10; s.ShieldShown = true;
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
    public void The_dotted_shapes_are_plotted_dot_by_dot_like_the_original()
    {
        var g = Session(null);
        var s = (Sariel)g.World.Boss!;
        ToPlaying(g);
        g.Step(GameButtons.None);
        s.Phase = Sariel.PhaseForm2; s.Hp = 6; s.PatternCur = 1; s.PhaseFrame = 60; s.InitialHpRendered = true;
        var fb = Render(g);
        int ring = 0;
        for (int y = 100; y < Framebuffer.Height; y++) for (int x = 0; x < Framebuffer.Width; x++) if (fb.GetPixel(x, y) == 7) ring++;
        Assert.InRange(ring, 100, 260);                             // at most one dot per angle: 256, not a solid line
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
