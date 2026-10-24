using Th01.Core.Game;
using Th01.Core.Graphics;
using Th01.Core.Input;
using Th01.Core.Sim;

namespace Th01.Core.Tests.Game;

/// <summary>Rules: the boss defeat white-out, the route menu and the hit flash are drawing only (ref: th01/main/boss/defeat.cpp, hit.cpp).</summary>
public class DefeatRenderTests
{
    sealed class BoxFont : IFontSource
    {
        public bool TryGetGlyph(int cp, out Glyph g)
        {
            g = new Glyph(8, Enumerable.Repeat((byte)0xFF, 16).ToArray());
            return true;
        }
    }

    static StageSetup BossStage() => new()
    {
        StageId = 4,
        Cards = new bool[StageSetup.Rows, StageSetup.Cols],
        Obstacles = new byte[StageSetup.Rows, StageSetup.Cols],
    };

    static GameSession Started()
    {
        var g = new GameSession(new GameOptions { FrameRand = 12, Stage = BossStage(), Lives = 3, Bombs = 1, Rank = Rank.Hard }, null, null, new BoxFont());
        for (int i = 0; i < SinGyoku.EntranceFrames; i++) g.Step(GameButtons.None);
        return g;
    }

    static void Defeat(GameSession g)
    {
        var b = g.World.Boss!;
        b.Phase = 2; b.Hp = 1; b.Invincible = false;
        g.World.Orb.Left = b.Left + 32; g.World.Orb.Top = b.Top + 32; g.World.Orb.Y.Force = 0; g.World.Orb.ForceFrame = 0;
        g.World.Orb.PrevLeft = g.World.Orb.Left; g.World.Orb.PrevTop = g.World.Orb.Top;
        g.Step(GameButtons.None);
        g.Step(GameButtons.None);
    }

    static Framebuffer Render(GameSession g)
    {
        var fb = new Framebuffer();
        g.Render(fb);
        return fb;
    }

    static int C(uint rgb, int shift) => (int)((rgb >> shift) & 0xFF) / 17;

    [Fact]
    public void White_lines_spread_one_row_per_tick_from_the_boss_centre()
    {
        var g = Started();
        Defeat(g);
        Assert.Equal(SessionPhase.BossDefeat, g.Phase);
        int y = g.DefeatStartY;
        Assert.Equal(g.World.Boss!.CenterY, y);
        for (int i = g.DefeatElapsed; i < 20; i++) g.Step(GameButtons.None);
        var fb = Render(g);
        int e = g.DefeatElapsed;
        Assert.Equal(7, fb.GetPixel(600, y - (e - 1)));
        Assert.Equal(7, fb.GetPixel(600, y + (e - 1)));
        Assert.NotEqual(7, fb.GetPixel(600, y + e + 3));
    }

    [Fact]
    public void Palette_whitens_every_15_ticks_then_the_screen_fades_back()
    {
        var g = Started();
        Defeat(g);
        var before = Render(g).Palette[1];
        while (g.DefeatElapsed < 30) g.Step(GameButtons.None);
        var after = Render(g).Palette[1];
        Assert.Equal(Math.Min(15, C(before, 16) + 2), C(after, 16));
        // run to the end of the line phase: pure white, then back to the stage palette
        int lines = Math.Max(g.DefeatStartY + 1, 400 - g.DefeatStartY);
        while (g.DefeatElapsed < lines) g.Step(GameButtons.None);
        Assert.Equal(15, C(Render(g).Palette[1], 16));
        var stage = before;                                          // colour 1 of the stage palette
        while (g.DefeatElapsed < lines + 8 * 15) g.Step(GameButtons.None);
        int level = Math.Max(C(stage, 16), 15 - 8);                   // 8 fade steps of 15 ticks each
        Assert.Equal(level, C(Render(g).Palette[1], 16));
        Assert.Equal(SessionPhase.BossDefeat, g.Phase);
    }

    [Fact]
    public void Route_menu_draws_and_highlights_the_chosen_route_and_input_picks_it()
    {
        var g = Started();
        Defeat(g);
        while (g.Phase == SessionPhase.BossDefeat) g.Step(GameButtons.None);
        Assert.Equal(SessionPhase.RouteSelect, g.Phase);
        var fb = Render(g);
        Assert.Equal(15, C(fb.Palette[1], 16));                       // Makai selected: white
        Assert.Equal(9, C(fb.Palette[3], 16));                        // Jigoku: dim
        Assert.Equal(1, fb.GetPixel(256 + 2, 250 + 8));                // caption glyph in colour 1
        Assert.NotEqual(0, fb.GetPixel(128 + 4, 250 + 4));             // cursor next to Makai
        Assert.Equal(0, fb.GetPixel(128 + 4, 300 + 4));
        for (int i = 0; i < GameSession.RouteSelectDelayTicks; i++) g.Step(GameButtons.None);
        g.Step(GameButtons.Down);
        g.Step(GameButtons.None);
        fb = Render(g);
        Assert.Equal(9, C(fb.Palette[1], 16));
        Assert.Equal(15, C(fb.Palette[3], 16));
        Assert.NotEqual(0, fb.GetPixel(128 + 4, 300 + 4));
        g.Step(GameButtons.Shot);
        Assert.Equal(SessionPhase.StageBonus, g.Phase);
        StageTests.Dismiss(g);
        Assert.Equal(SessionOutcome.Cleared, g.Outcome);
        Assert.Equal(1, g.CarryOver!.RouteChosen);
    }

    [Fact]
    public void The_hit_flash_lights_the_boss_colours_on_frames_2_and_3_of_every_4_and_never_changes_state()
    {
        var g = Started();
        var b = g.World.Boss!;
        b.Invincible = true;
        var expected = new[] { (0, false), (1, false), (2, true), (3, true), (4, false), (6, true), (7, true), (38, true), (39, true), (40, true), (41, false) };
        foreach (var (f, on) in expected)
        {
            b.InvincibilityFrame = f;
            Assert.Equal(on, b.FlashOn);
            var h1 = g.StateHash();
            var fb = Render(g);
            Assert.Equal(h1, g.StateHash());
            Assert.Equal(on, C(fb.Palette[13], 16) == 15 && C(fb.Palette[13], 8) == 15);
        }
        b.Invincible = false; b.InvincibilityFrame = 2;
        Assert.False(b.FlashOn);
    }
}
