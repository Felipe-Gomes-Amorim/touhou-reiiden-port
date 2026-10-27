using Th01.Core.Game;
using Th01.Core.Graphics;
using Th01.Core.Input;

namespace Th01.Core.Tests.Game;

/// <summary>Drawing of Elis: the entrance (circling spheres, sliding spheres, wave-in) and the fight effects. Drawing only.</summary>
public class ElisDrawTests
{
    sealed class Art : IGameArt
    {
        public int FrameCount(string sheet) => 0;
        public bool TryGetBackground(out ArtFrame art) { art = default; return false; }
        public bool TryGetFrame(string sheet, int frame, out ArtFrame art)
        {
            art = default;
            if (!sheet.StartsWith("BOSS5", StringComparison.Ordinal)) return false;
            int w = sheet.EndsWith(".GRC") ? 32 : 128, h = sheet.EndsWith(".GRC") ? 32 : 96;
            var on = Enumerable.Repeat((byte)5, w * h).ToArray();
            art = new ArtFrame(w, h, on, on, Array.Empty<byte>());
            return true;
        }
    }

    static StageSetup MakaiBoss() => new()
    {
        StageId = Elis.Id, Route = 0,
        Cards = new bool[StageSetup.Rows, StageSetup.Cols],
        Obstacles = new byte[StageSetup.Rows, StageSetup.Cols],
    };

    static GameSession Session(IGameArt? art) =>
        new(new GameOptions { FrameRand = 4, Stage = MakaiBoss(), Lives = 3, Bombs = 1 }, null, art);

    static int Count(Framebuffer fb, byte color, int left = 0, int top = 0, int w = Framebuffer.Width, int h = Framebuffer.Height)
    {
        int n = 0;
        for (int y = top; y < top + h; y++)
            for (int x = left; x < left + w; x++)
                if (fb.GetPixel(x, y) == color) n++;
        return n;
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void The_entrance_draws_something_on_every_stretch(bool withArt)
    {
        var g = Session(withArt ? new Art() : null);
        var e = (Elis)g.World.Boss!;
        int ticks = 0, spheres = 0, waves = 0, plain = 0;
        byte girl = (byte)(withArt ? 5 : 12);
        while (e.Phase == 0 && ticks++ < 2000)
        {
            g.Step(GameButtons.None);
            var fb = new Framebuffer();
            g.Render(fb);
            bool anySphere = e.EntranceSpheres().Any();
            bool girlVisible = e.EntranceGirlVisible;
            int girlPixels = Count(fb, girl, e.Left - 40, e.Top, Elis.GirlW + 80, Elis.GirlH);
            if (girlVisible)
            {
                Assert.True(girlPixels > 0, $"the girl is missing at entrance tick {e.InvincibilityFrame}");
                if (e.EntranceWave != null) waves++; else plain++;
            }
            else
            {
                // spheres only: heads are drawn in colour 8 (or the GRC cel in the art), unless they are off the screen
                int onScreen = e.EntranceSpheres().Count(s => s.left > -32 && s.left < Framebuffer.Width);
                if (onScreen > 0) Assert.True(Count(fb, 8) > 0, $"no sphere at tick {ticks}");
                if (anySphere) spheres++;
            }
        }
        Assert.True(spheres > 100, "circling spheres");
        Assert.True(waves > 20, "wave-in");
        Assert.True(plain > 20, "plain girl before the fight");
    }

    [Fact]
    public void The_entrance_sequence_matches_the_original_timeline()
    {
        var g = Session(null);
        var e = (Elis)g.World.Boss!;
        // circling: head follows the ring, trail grows over the first two ticks
        while (e.InvincibilityFrame < 1) g.Step(GameButtons.None);
        Assert.Single(e.EntranceSpheres().Where(s => s.cel == Elis.SphereLarge).Take(1));
        Assert.Equal(2, e.EntranceSpheres().Count());            // two heads only on tick 1
        while (e.InvincibilityFrame < 3) g.Step(GameButtons.None);
        Assert.Equal(6, e.EntranceSpheres().Count());             // heads, medium and small trails
        // the girl waves in from the 24th tick of the second loop; nothing earlier
        while (!e.EntranceGirlVisible) { Assert.Null(e.EntranceWave); g.Step(GameButtons.None); }
        Assert.Equal(24, e.InvincibilityFrame);
        Assert.NotNull(e.EntranceWave);
    }

    [Fact]
    public void Drawing_with_and_without_art_leaves_the_simulation_hash_alone()
    {
        ulong Run(IGameArt? art, bool render)
        {
            var g = Session(art);
            var fb = new Framebuffer();
            for (int i = 0; i < 1300; i++) { g.Step(GameButtons.None); if (render) g.Render(fb); }
            return g.StateHash();
        }
        var baseline = Run(null, false);
        Assert.Equal(baseline, Run(null, true));
        Assert.Equal(baseline, Run(new Art(), true));
    }

    [Fact]
    public void The_fight_poses_follow_the_patterns()
    {
        var g = Session(null);
        var e = (Elis)g.World.Boss!;
        e.Phase = 1; e.Pattern = 2; e.PhaseFrame = 49; e.TeleportDone = true;
        Assert.Equal((Elis.GirlSheet, Elis.CStill), e.GirlPose());
        e.PhaseFrame = 50;
        Assert.Equal((Elis.GirlSheet, Elis.CHand), e.GirlPose());
        e.Phase = 3; e.Pattern = 1; e.PhaseFrame = 5;                // Star of David not started: still
        Assert.Equal((Elis.GirlSheet, Elis.CStill), e.GirlPose());
        e.PhaseFrame = 12;
        Assert.Equal((Elis.AttackSheet, Elis.CPrepare), e.GirlPose());
    }

    [Fact]
    public void Circles_stars_and_rifts_draw_without_art_and_never_touch_the_state()
    {
        var g = Session(null);
        var e = (Elis)g.World.Boss!;
        while (e.Phase == 0) g.Step(GameButtons.None);
        e.Phase = 3; e.Pattern = 1; e.TeleportDone = true;
        foreach (int f in new[] { 12, 40, 70 })
        {
            e.PhaseFrame = f;
            var before = g.StateHash();
            var fb = new Framebuffer();
            g.Render(fb);
            Assert.Equal(before, g.StateHash());
            Assert.True(Count(fb, 7, 0, 64, Framebuffer.Width, Framebuffer.Height - 64) + Count(fb, 4) > 0);
        }
        e.Phase = 1; e.Pattern = 1; e.PhaseFrame = 100;
        var fb2 = new Framebuffer();
        g.Render(fb2);
        Assert.NotNull(e.HandRing);
        Assert.True(Count(fb2, 4) > 0);
    }
}
