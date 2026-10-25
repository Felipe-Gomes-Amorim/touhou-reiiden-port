using Th01.Core.Game;
using Th01.Core.Graphics;

namespace Th01.Core.Tests.Game;

public class PillarDrawingTests
{
    /// <summary>A pillar table where every sprite row has exactly one set pixel, at a column that tells which row it came from.</summary>
    sealed class PillarArt : IGameArt
    {
        public int FrameCount(string sheet) => sheet == "pillar" ? 2 : 0;

        public bool TryGetFrame(string sheet, int frame, out ArtFrame art)
        {
            art = default;
            if (sheet != "pillar" || frame is < 0 or > 1) return false;
            var bits = new byte[32 * 8];
            for (int r = 0; r < 8; r++) bits[r * 32 + (frame * 8 + r)] = 1; // body: column r; top: column 8 + r
            art = new ArtFrame(32, 8, bits, bits, Array.Empty<byte>());
            return true;
        }

        public bool TryGetBackground(out ArtFrame art) { art = default; return false; }
    }

    static (int x, int y)[] SetPixels(Framebuffer fb, byte color)
    {
        var list = new List<(int, int)>();
        for (int y = 0; y < Framebuffer.Height; y++)
            for (int x = 0; x < Framebuffer.Width; x++)
                if (fb.Pixels[y * Framebuffer.Width + x] == color) list.Add((x, y));
        return list.ToArray();
    }

    [Fact]
    public void Rows_are_drawn_from_the_floor_up_with_each_segments_rows_reversed()
    {
        var fb = new Framebuffer();
        Assert.True(PillarDrawing.Draw(fb, new PillarArt(), left: 40, bottom: 300, height: 24, color: 5));

        var set = SetPixels(fb, 5);
        Assert.Equal(24, set.Length); // one pixel per drawn row
        for (int y = 0; y < 16; y++) Assert.Contains((40 + (7 - y % 8), 300 - y), set);              // body segments
        for (int y = 16; y < 24; y++) Assert.Contains((40 + 8 + (7 - (y - 16)), 300 - y), set);      // top segment
    }

    [Fact]
    public void Left_edge_is_rounded_down_to_a_byte_boundary_like_the_original()
    {
        var a = new Framebuffer(); var b = new Framebuffer();
        PillarDrawing.Draw(a, new PillarArt(), 45, 300, 16, 5);
        PillarDrawing.Draw(b, new PillarArt(), 40, 300, 16, 5);
        Assert.Equal(SetPixels(b, 5), SetPixels(a, 5));
    }

    [Fact]
    public void Without_the_sprite_table_nothing_is_drawn_and_false_is_returned()
    {
        var fb = new Framebuffer();
        Assert.False(PillarDrawing.Draw(fb, null, 40, 300, 16, 5));
        Assert.False(PillarDrawing.Draw(fb, NullGameArt.Instance, 40, 300, 16, 5));
        Assert.Empty(SetPixels(fb, 5));
    }
}
