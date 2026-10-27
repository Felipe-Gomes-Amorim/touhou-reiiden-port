using Th01.Core.Game;
using Th01.Core.Graphics;
using Th01.Core.Input;

namespace Th01.Core.Tests.Game;

/// <summary>Drawing options of the bomb (flash and shake): they change the picture, never the simulation.</summary>
public class VisualOptionsTests
{
    const GameButtons L = GameButtons.Left, R = GameButtons.Right, Z = GameButtons.Shot;

    static Framebuffer Stage()
    {
        var fb = new Framebuffer();
        for (int i = 0; i < Framebuffer.ColorCount; i++) fb.SetPalette4(i, i, 2, 15 - i);
        return fb;
    }

    static int[] Pal(Framebuffer fb)
    {
        var p = new List<int>();
        for (int i = 0; i < Framebuffer.ColorCount; i++) { fb.GetPalette4(i, out int r, out int g, out int b); p.AddRange(new[] { r, g, b }); }
        return p.ToArray();
    }

    [Fact]
    public void Defaults_are_softened_flash_and_shake_on()
    {
        var v = new VisualOptions();
        Assert.Equal((BombFlash.Softened, true), (v.BombFlash, v.ScreenShake));
        Assert.Equal(v, new GameSession(new GameOptions { FrameRand = 1, Lives = 3, Bombs = 1 }).Visuals);
    }

    [Fact]
    public void Original_changes_the_palette_at_frame_52_and_softened_does_not()
    {
        var fx = new BombEffects();
        var soft = Stage(); fx.Draw(soft, null, 52u, new VisualOptions());
        var orig = Stage(); fx.Draw(orig, null, 52u, new VisualOptions { BombFlash = BombFlash.Original });
        Assert.Equal(Pal(Stage()), Pal(soft));
        Assert.NotEqual(Pal(Stage()), Pal(orig));
        orig.GetPalette4(7, out int r, out int g, out int b);
        Assert.Equal((15, 15, 15), (r, g, b));                 // frame 52 % 4 == 0: the peak, white stays white
        orig.GetPalette4(1, out r, out g, out b);
        Assert.Equal((13, 13, 15), (r, g, b));
        var f50 = Stage(); fx.Draw(f50, null, 50u, new VisualOptions { BombFlash = BombFlash.Original });
        Assert.Equal(Pal(Stage()), Pal(f50));                   // nothing before frame 52
    }

    [Fact]
    public void Frame_54_is_the_brightened_stage_palette_and_it_gains_a_level_after_frame_60()
    {
        var fx = new BombEffects();
        var o = new VisualOptions { BombFlash = BombFlash.Original };
        var a = Stage(); fx.Draw(a, null, 54u, o);
        a.GetPalette4(1, out int r, out int g, out int b);      // stage (1,2,14): 1+0, 2+1, 14>10 -> 15
        Assert.Equal((1, 3, 15), (r, g, b));
        var c = Stage(); fx.Draw(c, null, 62u, o);               // frame 60 raised it once
        c.GetPalette4(1, out r, out g, out b);
        Assert.Equal((2, 4, 15), (r, g, b));
        var odd = Stage(); fx.Draw(odd, null, 55u, o);
        Assert.Equal(Pal(a), Pal(odd));                          // odd frames keep the even one
    }

    [Fact]
    public void Shake_off_zeroes_the_scroll_and_on_keeps_it()
    {
        var fx = new BombEffects();
        var on = Stage(); fx.Draw(on, null, 64u, new VisualOptions());
        var off = Stage(); fx.Draw(off, null, 64u, new VisualOptions { ScreenShake = false });
        Assert.Equal(Framebuffer.Height - 8, on.ScrollY);
        Assert.Equal(0, off.ScrollY);
    }

    [Fact]
    public void No_combination_changes_the_simulation_hash()
    {
        ulong Run(VisualOptions v)
        {
            var s = new GameSession(new GameOptions { FrameRand = 7, Lives = 3, Bombs = 2 }, visuals: v);
            var fb = new Framebuffer();
            s.Step(L | R | Z); s.Step(L | R | Z);
            for (int i = 0; i < 160; i++) { s.Step(L | R); s.Render(fb); }
            return s.StateHash();
        }
        var baseline = Run(new VisualOptions());
        foreach (var f in new[] { BombFlash.Softened, BombFlash.Original })
            foreach (var shake in new[] { true, false })
                Assert.Equal(baseline, Run(new VisualOptions { BombFlash = f, ScreenShake = shake }));
    }
}
