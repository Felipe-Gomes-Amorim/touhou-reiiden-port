using Th01.Assets.Text;
using Th01.Platform.MonoGame.Launcher;

namespace Th01.Platform.Tests;

public class LauncherOrbTests
{
    static readonly Th01.Core.Graphics.IFontSource Font = JapaneseFont.CreateDefault();
    static readonly LauncherGameInfo Valid = new("C:\\g", true, GameVersion.Japanese);

    static LauncherModel Model(LauncherSettings? s = null) => new(s ?? new LauncherSettings(), Valid, "0.4.0");

    static uint[] Region(LauncherCanvas c, int cx, int cy, int r)
    {
        var px = new List<uint>();
        for (int y = cy - r; y <= cy + r; y++) for (int x = cx - r; x <= cx + r; x++) px.Add(c.Get(x, y));
        return px.ToArray();
    }

    [Fact]
    public void Frames_AreCrisp_OnlyTheTwoOrbColoursAndNothing()
    {
        for (int f = 0; f < OrbFrames.Count; f++)
            foreach (uint p in OrbFrames.Get(12, f))
                Assert.True(p is 0 or LauncherColors.Red or LauncherColors.White, $"frame {f}: {p:X8}");
    }

    [Fact]
    public void Frame0_IsTheUprightOrb_AndHalfATurnIsThePictureTurnedAround()
    {
        const int r = 12, side = 2 * r + 1;
        var up = OrbFrames.Get(r, 0);
        Assert.Equal(LauncherColors.Red, up[r * side + r + 6]);    // right half red
        Assert.Equal(LauncherColors.White, up[r * side + r - 6]);  // left half white
        var half = OrbFrames.Get(r, OrbFrames.Count / 2);
        int same = 0;
        for (int i = 0; i < up.Length; i++) if (half[i] == up[up.Length - 1 - i]) same++;
        Assert.True(same >= up.Length * 99 / 100, $"{same}/{up.Length}");
        Assert.Equal(up[r * side + r + 6], half[r * side + r - 6]);   // the colours swapped sides
        Assert.Equal(up, OrbFrames.Get(r, OrbFrames.Count));          // a whole turn is the same frame
        Assert.Equal(up, OrbFrames.Get(r, -OrbFrames.Count));
    }

    [Fact]
    public void QuarterTurn_MapsEveryPixelLikeARotation()
    {
        const int r = 12, side = 2 * r + 1;
        var up = OrbFrames.Get(r, 0);
        var q = OrbFrames.Get(r, OrbFrames.Count / 4);
        int same = 0;
        for (int y = -r; y <= r; y++)
            for (int x = -r; x <= r; x++)
            {
                // pixel (x, y) of the turned orb is where (u, v) = (y, -x) sits in the upright one
                int u = y, v = -x;
                if (q[(y + r) * side + x + r] == up[(v + r) * side + u + r]) same++;
            }
        Assert.True(same >= side * side * 98 / 100, $"{same}/{side * side}");
        Assert.NotEqual(up, q);
    }

    [Fact]
    public void SpinFrame_IsOneTurnPerSecondAtTheTickRate()
    {
        Assert.Equal(0, LauncherBackdrop.SpinFrame(0));
        Assert.Equal(15, LauncherBackdrop.SpinFrame(28));                // about half a second
        Assert.Equal(31, LauncherBackdrop.SpinFrame(56));                // just before one second (56.423 ticks)
        Assert.Equal(0, LauncherBackdrop.SpinFrame(57));                 // the next turn
        for (int t = 0; t < 400; t++) Assert.InRange(LauncherBackdrop.SpinFrame(t), 0, OrbFrames.Count - 1);
        Assert.Equal(LauncherBackdrop.SpinFrame(123), LauncherBackdrop.SpinFrame(123 + 56423));   // exact period of 1000 turns
        int steps = 0;
        for (int t = 1; t <= 56; t++) if (LauncherBackdrop.SpinFrame(t) != LauncherBackdrop.SpinFrame(t - 1)) steps++;
        Assert.InRange(steps, 28, 56);                                    // it advances smoothly, never stuck
    }

    [Fact]
    public void MenuOrb_TurnsWithTheFocusedItem_AndStopsWhenMotionIsReduced()
    {
        // The focused PLAY item draws its orb at (96 + 8 + 27, 270 + 28).
        int cx = 96 + 8 + 27, cy = 270 + 28;
        var m = Model();
        var a = new LauncherCanvas(); var b = new LauncherCanvas();
        LauncherView.Render(m, a, Font);
        for (int i = 0; i < 14; i++) m.Step();
        LauncherView.Render(m, b, Font);
        Assert.NotEqual(Region(a, cx, cy, 11), Region(b, cx, cy, 11));      // turned

        var still = Model(new LauncherSettings { SoftenBombFlash = true, ReduceShake = true, RemoveTitleLines = true });   // "reduce lights and motion"
        Assert.False(still.OrbSpinning);
        LauncherView.Render(still, a, Font);
        for (int i = 0; i < 14; i++) still.Step();
        LauncherView.Render(still, b, Font);
        Assert.Equal(Region(a, cx, cy, 11), Region(b, cx, cy, 11));          // static
    }

    [Fact]
    public void OnlyTheFocusedItemShowsAnOrb_SoAnUnfocusedOneIsNeverSpinning()
    {
        var m = Model();
        m.Press(LauncherKey.Down);                                           // OPTIONS focused, PLAY not
        var c = new LauncherCanvas();
        LauncherView.Render(m, c, Font);
        int px = 96 + 27, py = 270 + 28;                                     // where PLAY's orb would be
        Assert.DoesNotContain(Region(c, px, py, 8), p => p == LauncherColors.Red);
    }

    [Fact]
    public void HeaderOrb_TurnsOnHoverOrKeyboardFocus_NotWhenTheMouseIsElsewhere_NotWhenReduced()
    {
        var m = Model();
        Assert.True(m.HeaderSpinning);                                       // keyboard-driven screen
        m.Hover(new HitTarget(HitKind.HomeItem, (int)HomeItem.Quit));
        Assert.Equal(InputDevice.Mouse, m.LastDevice);
        Assert.False(m.HeaderSpinning);                                      // mouse is on a button, not the header
        m.Hover(new HitTarget(HitKind.Header));
        Assert.True(m.HeaderSpinning);
        m.Hover(null);
        Assert.False(m.HeaderSpinning);
        m.Hover(new HitTarget(HitKind.Header));
        m.Press(LauncherKey.Down);                                           // keyboard takes over: the one focus
        Assert.Equal(InputDevice.Keyboard, m.LastDevice);
        Assert.True(m.HeaderSpinning);

        var reduced = Model(new LauncherSettings { SoftenBombFlash = true, ReduceShake = true, RemoveTitleLines = true });
        reduced.Hover(new HitTarget(HitKind.Header));
        Assert.False(reduced.HeaderSpinning);
        Assert.False(reduced.OrbSpinning);
    }

    [Fact]
    public void ClickingKeepsTheMouseAsTheLastDevice()
    {
        var m = Model();
        m.Click(new HitTarget(HitKind.HomeItem, (int)HomeItem.Options), false);   // Press runs inside, the device must stay "mouse"
        Assert.Equal(InputDevice.Mouse, m.LastDevice);
    }

    [Fact]
    public void EveryScreenHasAHeaderHitRegion_AndHoveringItDoesNotMoveTheFocus()
    {
        var m = Model();
        var hits = new LauncherHitMap();
        var canvas = new LauncherCanvas();
        bool Has(HitKind k)
        {
            for (int y = 0; y < 720; y += 2) for (int x = 0; x < 1280; x += 2) if (hits.At(x, y) is { } t && t.Kind == k) return true;
            return false;
        }
        LauncherView.Render(m, canvas, Font, hits);
        Assert.True(Has(HitKind.Header));
        int focus = m.Focus;
        m.Hover(new HitTarget(HitKind.Header));
        Assert.Equal(focus, m.Focus);
        m.Press(LauncherKey.Down); m.Press(LauncherKey.Select);              // options
        LauncherView.Render(m, canvas, Font, hits);
        Assert.True(Has(HitKind.Header));
        m.Press(LauncherKey.Back);
        m.Press(LauncherKey.Up); m.Press(LauncherKey.Select);                // About is last in the order; Up from OPTIONS wraps through PLAY to it
        m.Press(LauncherKey.Up);
    }

    /// <summary>LAUNCHER_DUMP=folder writes four turns of the orb on the options header, to look at.</summary>
    [Fact]
    public void Dump_OrbTurns_WhenAsked()
    {
        string? dir = Environment.GetEnvironmentVariable("LAUNCHER_DUMP");
        if (string.IsNullOrEmpty(dir)) return;
        Directory.CreateDirectory(dir);
        var c = new LauncherCanvas();
        c.Pixels.AsSpan().Fill(LauncherColors.Background);
        for (int f = 0; f < OrbFrames.Count; f++) c.YinYang(40 + (f % 16) * 60, 40 + (f / 16) * 60, 24, 0, f);
        LauncherTests.WritePng(Path.Combine(dir, "p5-orb-frames.png"), c);
    }
}
