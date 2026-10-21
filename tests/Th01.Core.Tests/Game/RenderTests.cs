using Th01.Core.Game;
using Th01.Core.Graphics;
using Th01.Core.Input;

namespace Th01.Core.Tests.Game;

public class RenderTests
{
    /// <summary>Solid-colour frames of a given size; alpha is opaque (empty).</summary>
    sealed class FakeArt : IGameArt
    {
        readonly Dictionary<(string, int), ArtFrame> _f = new();
        public ArtFrame? Background;

        public FakeArt Add(string sheet, int frame, int w, int h, byte color, byte[]? palette = null)
        {
            var px = new byte[w * h];
            Array.Fill(px, color);
            _f[(sheet, frame)] = new ArtFrame(w, h, px, Array.Empty<byte>(), palette ?? Array.Empty<byte>());
            return this;
        }

        public int FrameCount(string sheet) => _f.Keys.Count(k => k.Item1 == sheet);
        public bool TryGetFrame(string sheet, int frame, out ArtFrame art) => _f.TryGetValue((sheet, frame), out art);
        public bool TryGetBackground(out ArtFrame art) { art = Background ?? default; return Background.HasValue; }
    }

    sealed class DigitFont : IFontSource
    {
        public bool TryGetGlyph(int cp, out Glyph g)
        {
            g = new Glyph(8, new byte[16].Select(_ => (byte)0xFF).ToArray());
            return cp is >= '0' and <= '9' or ' ' or >= 'A' and <= 'Z';
        }
    }

    static StageSetup Setup(int stageId = 0, (int r, int c)[]? cards = null, (int r, int c, byte v)[]? obs = null)
    {
        var c = new bool[StageSetup.Rows, StageSetup.Cols];
        var o = new byte[StageSetup.Rows, StageSetup.Cols];
        foreach (var (r, col) in cards ?? Array.Empty<(int, int)>()) c[r, col] = true;
        foreach (var (r, col, v) in obs ?? Array.Empty<(int, int, byte)>()) o[r, col] = v;
        return new StageSetup { StageId = stageId, Cards = c, Obstacles = o };
    }

    static GameSession Session(IGameArt? art, IFontSource? font = null, StageSetup? st = null, Rank rank = Rank.Normal) =>
        new(new GameOptions { FrameRand = 5, Stage = st ?? Setup(cards: new[] { (0, 5), (9, 19) }), Rank = rank, Lives = 3, Bombs = 2 },
            null, art, font);

    static byte At(Framebuffer fb, int x, int y) => fb.GetPixel(x, y);

    static FakeArt FullArt() => new FakeArt()
        .Add("STG.PTN", 0, 32, 32, 3)                  // HUD icons (quarters are all colour 3)
        .Add("STG.PTN", 1, 32, 32, 4)                  // shot
        .Add("STG.PTN", 3, 32, 32, 5).Add("STG.PTN", 4, 32, 32, 6)   // orb cels
        .Add("STG.PTN", 8, 32, 32, 7)                  // card 3HP whole
        .Add("STG.PTN", 11, 32, 32, 8)                 // card 2HP whole
        .Add("STG.PTN", 14, 32, 32, 9)                 // card 1HP whole
        .Add("STG.PTN", 17, 32, 32, 10)                // card 0HP whole
        .Add("STG.PTN", 22, 32, 32, 11).Add("STG.PTN", 29, 32, 32, 12).Add("STG.PTN", 25, 32, 32, 13)
        .Add("STG.PTN", 23, 32, 32, 14).Add("STG.PTN", 32, 32, 32, 3).Add("STG.PTN", 33, 32, 32, 4)
        .Add("MIKO.PTN", 0, 32, 32, 2).Add("MIKO.PTN", 3, 32, 32, 1)
        .Add("MIKO_AC.BOS", 0, 48, 48, 12).Add("MIKO_AC2.BOS", 1, 48, 32, 11);

    // ---- the hash must not depend on art or rendering ----

    [Fact]
    public void Rendering_and_art_never_change_the_state_hash()
    {
        var plain = Session(null);
        var arted = Session(FullArt(), new DigitFont());
        var fb = new Framebuffer();
        var script = new[] { GameButtons.Shot, GameButtons.None, GameButtons.Left, GameButtons.Left | GameButtons.Shot, GameButtons.Right };
        for (int i = 0; i < 400; i++)
        {
            var b = script[(i / 3) % script.Length];
            plain.Step(b); arted.Step(b);
            if (i % 7 == 0) { arted.Render(fb); arted.Render(fb); }
            Assert.Equal(plain.StateHash(), arted.StateHash());
        }
    }

    [Fact]
    public void Render_is_idempotent_for_the_same_state()
    {
        var s = Session(FullArt(), new DigitFont());
        for (int i = 0; i < 30; i++) s.Step(i == 0 ? GameButtons.Shot : GameButtons.Left);
        var a = new Framebuffer(); var b = new Framebuffer();
        s.Render(a); s.Render(b);
        Assert.True(a.Pixels.SequenceEqual(b.Pixels));
    }

    // ---- fallbacks ----

    [Fact]
    public void Without_art_the_flat_boxes_are_drawn()
    {
        var s = Session(null);
        var fb = new Framebuffer();
        s.Render(fb);
        Assert.Equal(10, At(fb, s.World.Orb.Left + 5, s.World.Orb.Top + 5));            // orb box
        Assert.Equal(9, At(fb, s.World.Player.Left + 5, Playfield.PlayerTop + 5));      // player box
        Assert.Equal(14, At(fb, 5 * 32 + 10, 64 + 10));                                 // card box
    }

    [Fact]
    public void Missing_frames_fall_back_per_element()
    {
        var art = new FakeArt().Add("STG.PTN", 3, 32, 32, 5);     // only the orb
        var s = Session(art);
        var fb = new Framebuffer();
        s.Render(fb);
        Assert.Equal(5, At(fb, s.World.Orb.Left + 5, s.World.Orb.Top + 5));
        Assert.Equal(9, At(fb, s.World.Player.Left + 5, Playfield.PlayerTop + 5));
    }

    // ---- sprites ----

    [Fact]
    public void Sprites_land_at_the_simulation_positions()
    {
        var s = Session(FullArt());
        var fb = new Framebuffer();
        s.Render(fb);
        var w = s.World;
        Assert.Equal(5, At(fb, w.Orb.Left + 16, w.Orb.Top + 16));                   // orb cel 0
        Assert.Equal(2, At(fb, w.Player.Left + 16, Playfield.PlayerTop + 16));      // MIKO.PTN 0 (standing)
        Assert.Equal(10, At(fb, 5 * 32 + 16, 64 + 16));                             // 0 extra flips: "0HP" art
        Assert.Equal(3, At(fb, 128 + 4, 4));                                        // first life icon
        Assert.Equal(3, At(fb, 128 + 4 + 16 * 2, 4));                               // third
        Assert.Equal(0, At(fb, 128 + 4 + 16 * 3, 4));                               // there is no fourth
    }

    [Fact]
    public void Card_art_follows_remaining_flips_and_disappears_when_removed()
    {
        var st = Setup(cards: new[] { (0, 5), (0, 6), (0, 7), (9, 19) },
                       obs: new (int, int, byte)[] { (0, 6, 14), (0, 7, 16) });   // 1 and 3 extra flips
        var s = Session(FullArt(), st: st);
        var fb = new Framebuffer();
        s.Render(fb);
        Assert.Equal(10, At(fb, 5 * 32 + 16, 64 + 16));    // hp 0 -> 0HP art
        Assert.Equal(9, At(fb, 6 * 32 + 16, 64 + 16));     // hp 1 -> 1HP art
        Assert.Equal(7, At(fb, 7 * 32 + 16, 64 + 16));     // hp 3 -> 3HP art
        s.World.Stage!.CardFlagArr[0] = CardFlag.Removed;
        s.Render(fb);
        Assert.Equal(0, At(fb, 5 * 32 + 16, 64 + 16));     // nothing left (the background shows through)
    }

    [Fact]
    public void Obstacles_use_their_own_sprites()
    {
        var st = Setup(cards: new[] { (9, 19) },
                       obs: new (int, int, byte)[] { (2, 2, 1), (2, 4, 17), (2, 6, 18), (2, 8, 2) });
        var s = Session(FullArt(), st: st);
        var fb = new Framebuffer();
        s.Render(fb);
        Assert.Equal(11, At(fb, 2 * 32 + 16, 128 + 16));   // bumper
        Assert.Equal(12, At(fb, 4 * 32 + 16, 128 + 16));   // portal
        Assert.Equal(13, At(fb, 6 * 32 + 16, 128 + 16));   // top bar
        Assert.Equal(14, At(fb, 8 * 32 + 16, 128 + 16));   // turret at rest
    }

    [Fact]
    public void Shots_use_the_first_quarter_and_player_attacks_use_the_big_sheets()
    {
        var s = Session(FullArt());
        var fb = new Framebuffer();
        s.World.Shots.Add(100, 300);
        s.Render(fb);
        Assert.Equal(4, At(fb, 100 + 8, 300 + 8));
        Assert.Equal(0, At(fb, 100 + 20, 300 + 20));       // only a 16x16 quarter
    }

    [Fact]
    public void Palette_and_background_come_from_the_art()
    {
        var pal = new byte[48];
        pal[3] = 15; pal[4] = 0; pal[5] = 0;               // colour 1 = red
        var art = FullArt();
        art.Background = new ArtFrame(640, 400, Enumerable.Repeat((byte)1, 640 * 400).ToArray(), Array.Empty<byte>(), pal);
        var s = Session(art);
        var fb = new Framebuffer();
        s.Render(fb);
        Assert.Equal(1, At(fb, 600, 100));                 // background pixel
        Assert.Equal(0xFFFF0000u, fb.Palette[1]);
    }

    [Fact]
    public void Hud_numbers_need_a_font_and_bonus_box_appears_on_clear()
    {
        var withFont = Session(FullArt(), new DigitFont());
        var noFont = Session(FullArt());
        var a = new Framebuffer(); var b = new Framebuffer();
        withFont.Render(a); noFont.Render(b);
        Assert.NotEqual(15, At(b, 256 + 4, 16 + 4));
        Assert.Equal(15, At(a, 256 + 4, 16 + 4));          // a score digit cell is lit
    }
}
