using Th01.Core.Game;
using Th01.Core.Graphics;

namespace Th01.Core.Tests.Game;

/// <summary>
/// A card that has been destroyed leaves its empty frame (the "removed" cel) behind for the rest of the stage: the original
/// blits the last cel of the final flip to both VRAM pages, so it becomes part of the background.
/// </summary>
public class CardBorderTests
{
    sealed class Art : IGameArt
    {
        readonly Dictionary<int, byte> _colorOf = new();
        public Art Add(int frame, byte color) { _colorOf[frame] = color; return this; }
        public int FrameCount(string sheet) => 0;
        public bool TryGetFrame(string sheet, int frame, out ArtFrame art)
        {
            if (sheet == "STG.PTN" && _colorOf.TryGetValue(frame, out var c))
            {
                art = new ArtFrame(32, 32, Enumerable.Repeat(c, 32 * 32).ToArray(), Array.Empty<byte>(), Array.Empty<byte>());
                return true;
            }
            art = default;
            return false;
        }
        public bool TryGetBackground(out ArtFrame art) { art = default; return false; }
    }

    // frames of a card with no flips left (0HP): whole, half, edge, removed-half, removed
    static Art CardArt() => new Art().Add(17, 3).Add(18, 4).Add(19, 5).Add(20, 6).Add(21, 7);

    static GameSession OneCard(IGameArt? art)
    {
        var cards = new bool[StageSetup.Rows, StageSetup.Cols];
        cards[3, 8] = true;
        var setup = new StageSetup { StageId = 0, Cards = cards, Obstacles = new byte[StageSetup.Rows, StageSetup.Cols] };
        return new GameSession(new GameOptions { FrameRand = 5, Stage = setup }, null, art);
    }

    static byte PixelOfCard(GameSession g, Framebuffer fb)
    {
        var st = g.World.Stage!;
        g.Render(fb);
        return fb.GetPixel(st.CardLeft[0] + 16, st.CardTop[0] + 16);
    }

    [Fact]
    public void A_removed_card_leaves_its_empty_frame_where_it_was()
    {
        var g = OneCard(CardArt());
        var st = g.World.Stage!;
        var fb = new Framebuffer();
        Assert.Equal(3, PixelOfCard(g, fb));                 // alive: the whole card

        st.CardFlagArr[0] = CardFlag.Removed;
        Assert.Equal(7, PixelOfCard(g, fb));                 // removed: the border, not nothing
    }

    [Fact]
    public void The_last_cel_of_the_flip_is_the_cel_that_stays_behind()
    {
        var g = OneCard(CardArt());
        var st = g.World.Stage!;
        var fb = new Framebuffer();
        st.CardFlagArr[0] = CardFlag.Flipping;
        var seen = new List<byte>();
        foreach (int frame in new[] { 0, 6, 12, 18, 24 })
        {
            st.CardFlipFrame[0] = frame;
            seen.Add(PixelOfCard(g, fb));
        }
        Assert.Equal(new byte[] { 3, 4, 5, 6, 7 }, seen);    // 0HP, half, edge, removed-half, removed
        st.CardFlagArr[0] = CardFlag.Removed;
        Assert.Equal(seen[^1], PixelOfCard(g, fb));          // and the same cel remains once the flip is over
    }

    [Fact]
    public void Without_the_sprite_a_removed_card_leaves_nothing()
    {
        var g = OneCard(new Art());                           // no STG.PTN frames at all
        var st = g.World.Stage!;
        var fb = new Framebuffer();
        st.CardFlagArr[0] = CardFlag.Removed;
        g.Render(fb);
        Assert.Equal(0, fb.GetPixel(st.CardLeft[0] + 16, st.CardTop[0] + 16));
    }

    [Fact]
    public void The_border_is_only_drawing_and_does_not_change_the_simulation()
    {
        var plain = OneCard(null);
        var arted = OneCard(CardArt());
        plain.World.Stage!.CardFlagArr[0] = CardFlag.Removed;
        arted.World.Stage!.CardFlagArr[0] = CardFlag.Removed;
        var fb = new Framebuffer();
        for (int i = 0; i < 20; i++)
        {
            plain.Step(Th01.Core.Input.GameButtons.None); arted.Step(Th01.Core.Input.GameButtons.None);
            arted.Render(fb);
            Assert.Equal(plain.StateHash(), arted.StateHash());
        }
    }
}
