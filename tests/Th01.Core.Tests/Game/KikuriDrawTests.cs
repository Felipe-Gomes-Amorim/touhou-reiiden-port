using Th01.Core.Game;
using Th01.Core.Graphics;
using Th01.Core.Input;

namespace Th01.Core.Tests.Game;

/// <summary>Drawing of Kikuri's entrance (barn door, rings) and of the tear ripples. Drawing only.</summary>
public class KikuriDrawTests
{
    sealed class Art : IGameArt
    {
        public int FrameCount(string sheet) => 0;
        public bool TryGetBackground(out ArtFrame art)
        {
            var on = Enumerable.Repeat((byte)5, Framebuffer.Width * Framebuffer.Height).ToArray();
            art = new ArtFrame(Framebuffer.Width, Framebuffer.Height, on, Array.Empty<byte>(), Array.Empty<byte>());
            return true;
        }
        public bool TryGetFrame(string sheet, int frame, out ArtFrame art) { art = default; return false; }
    }

    static StageSetup Boss() => new()
    {
        StageId = Kikuri.Id, Route = 1,
        Cards = new bool[StageSetup.Rows, StageSetup.Cols],
        Obstacles = new byte[StageSetup.Rows, StageSetup.Cols],
    };

    static GameSession Session(IGameArt? art) =>
        new(new GameOptions { FrameRand = 6, Stage = Boss(), Lives = 3, Bombs = 1 }, null, art);

    static int Count(Framebuffer fb, byte color, int left = 0, int top = 0, int w = Framebuffer.Width, int h = Framebuffer.Height)
    {
        int n = 0;
        for (int y = top; y < top + h; y++)
            for (int x = left; x < left + w; x++)
                if (fb.GetPixel(x, y) == color) n++;
        return n;
    }

    static Framebuffer Render(GameSession g)
    {
        var fb = new Framebuffer();
        g.Render(fb);
        return fb;
    }

    [Fact]
    public void The_barn_door_reveals_rows_from_the_edges_in_four_dot_passes()
    {
        var g = Session(new Art());
        var k = (Kikuri)g.World.Boss!;
        k.InvincibilityFrame = 0;
        var fb = Render(g);
        Assert.Equal(5, fb.GetPixel(2, 0));                       // mask 0x2222: x % 16 == 2, 6, 10, 14
        Assert.Equal(0, fb.GetPixel(0, 0));
        Assert.Equal(5, fb.GetPixel(Framebuffer.Width - 14, Framebuffer.Height - 1));
        Assert.Equal(0, fb.GetPixel(2, 200));                     // the middle is not revealed yet
        k.InvincibilityFrame = 8;
        fb = Render(g);
        Assert.Equal(5, fb.GetPixel(0, 0));                       // second pass 0xAAAA reaches row 0 as well (x % 16 even)
        k.InvincibilityFrame = 224;
        fb = Render(g);
        Assert.Equal(5, fb.GetPixel(0, 0));
        Assert.Equal(5, fb.GetPixel(1, 200));                     // all four passes: every dot
    }

    [Fact]
    public void The_rings_grow_from_frame_72_and_flip_only_the_red_plane()
    {
        var g = Session(null);
        var k = (Kikuri)g.World.Boss!;
        while (k.EntranceStage == 0) g.Step(GameButtons.None);
        k.PhaseFrame = 71;
        var before = Render(g);
        k.PhaseFrame = 87;
        var after = Render(g);
        int changed = 0;
        for (int y = 0; y < Framebuffer.Height; y++)
            for (int x = 0; x < Framebuffer.Width; x++)
                if (before.GetPixel(x, y) != after.GetPixel(x, y)) { changed++; Assert.Equal(2, before.GetPixel(x, y) ^ after.GetPixel(x, y)); }
        Assert.True(changed > 500);
    }

    [Fact]
    public void A_tear_ripple_rises_and_falls_along_the_floor()
    {
        var g = Session(null);
        var k = (Kikuri)g.World.Boss!;
        k.Phase = 4;
        k.Tears[0].PosSet(104, Playfield.Bottom);                 // already on the floor
        int bottom = Playfield.Bottom - Kikuri.TearH;
        int Drawn(int frame)
        {
            k.TearAnimFrame[0] = frame;
            return Count(Render(g), 10, 104 - 48, bottom - 40, 112, 56);
        }
        int first = Drawn(1), later = Drawn(11);
        Assert.True(first > 0);                                   // the centre column
        Assert.True(later > first);                               // inner columns join, the centre rises
        Assert.True(Drawn(40) > 0);
        Assert.Equal(0, Drawn(70));                               // every column has fallen back: gone
    }

    [Fact]
    public void Drawing_the_entrance_never_changes_the_simulation()
    {
        ulong Run(IGameArt? art, bool render)
        {
            var g = Session(art);
            var fb = new Framebuffer();
            for (int i = 0; i < 700; i++) { g.Step(GameButtons.None); if (render) g.Render(fb); }
            return g.StateHash();
        }
        var baseline = Run(null, false);
        Assert.Equal(baseline, Run(null, true));
        Assert.Equal(baseline, Run(new Art(), true));
    }
}
