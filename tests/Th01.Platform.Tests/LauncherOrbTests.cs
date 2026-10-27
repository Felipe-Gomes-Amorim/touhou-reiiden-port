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
    public void HeaderOrbs_NeverTurn_OnAnyScreen_NoMatterTheFocusOrTheMouse()
    {
        // Each screen has an orb next to its title. Render at two ticks a quarter turn apart and compare the header strip: it must not change.
        // (Bands at the bottom and the focused menu orb do change, so only the header region is compared.)
        foreach (int screen in new[] { 0, 1, 2 })
        {
            var m = Model();
            if (screen == 1) { m.Press(LauncherKey.Down); m.Press(LauncherKey.Select); }
            if (screen == 2) { m.Press(LauncherKey.Up); m.Press(LauncherKey.Select); }
            m.Hover(new HitTarget(HitKind.HomeItem, (int)HomeItem.Quit));
            var a = new LauncherCanvas(); var b = new LauncherCanvas();
            LauncherView.Render(m, a, Font);
            for (int i = 0; i < 14; i++) m.Step();
            LauncherView.Render(m, b, Font);
            int stripBottom = screen == 0 ? 108 : 100;
            for (int y = 24; y < stripBottom; y++)
                for (int x = 60; x < 800; x++)
                    Assert.True(a.Get(x, y) == b.Get(x, y), $"screen {screen}: header pixel {x},{y} changed");
        }
    }

    [Fact]
    public void OrbSpinning_IsOffWhenMotionIsReduced_AndThereIsNoHeaderHoverState()
    {
        Assert.True(Model().OrbSpinning);
        var reduced = Model(new LauncherSettings { SoftenBombFlash = true, ReduceShake = true, RemoveTitleLines = true });
        Assert.False(reduced.OrbSpinning);
        Assert.DoesNotContain("Header", Enum.GetNames<HitKind>());
    }

    [Fact]
    public void EveryMenuItemOrb_TurnsOnlyWhileThatItemHasTheFocus()
    {
        // PLAY, OPTIONS and QUIT: each orb, when focused, differs between two ticks; the others show nothing.
        foreach (var (downs, index) in new[] { (0, 0), (1, 1), (2, 2) })
        {
            var m = Model();
            for (int i = 0; i < downs; i++) m.Press(LauncherKey.Down);
            Assert.Equal(LauncherModel.HomeOrder[index], m.FocusedItem);
            int cx = 96 + 56 * index + 8 + 27, cy = 270 + 70 * index + 28;
            var a = new LauncherCanvas(); var b = new LauncherCanvas();
            LauncherView.Render(m, a, Font);
            for (int i = 0; i < 14; i++) m.Step();
            LauncherView.Render(m, b, Font);
            Assert.NotEqual(Region(a, cx, cy, 11), Region(b, cx, cy, 11));
        }
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
