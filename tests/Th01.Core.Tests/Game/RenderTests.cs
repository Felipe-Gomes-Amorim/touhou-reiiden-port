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
        Assert.NotEqual(7, At(b, 256 + 4, 16 + 4));
        Assert.Equal(7, At(a, 256 + 4, 16 + 4));          // a score digit cell is lit
    }

    // ---- embedded 1-bit sprites ----

    /// <summary>A mono frame: index 1 where lit (the platform may also provide alpha instead).</summary>
    static ArtFrame Mono(int w, int h, Func<int, int, bool> lit, bool useAlpha)
    {
        var px = new byte[w * h]; var al = new byte[w * h];
        for (int y = 0; y < h; y++)
            for (int x = 0; x < w; x++) { bool on = lit(x, y); px[y * w + x] = (byte)(on ? 1 : 0); al[y * w + x] = (byte)(on ? 1 : 0); }
        return new ArtFrame(w, h, px, useAlpha ? al : Array.Empty<byte>(), Array.Empty<byte>());
    }

    sealed class MonoArt : IGameArt
    {
        public readonly Dictionary<(string, int), ArtFrame> F = new();
        public int FrameCount(string sheet) => 0;
        public bool TryGetFrame(string sheet, int frame, out ArtFrame art) => F.TryGetValue((sheet, frame), out art);
        public bool TryGetBackground(out ArtFrame art) { art = default; return false; }
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Pellets_use_the_embedded_sprites_in_cel_order_and_fall_back_to_boxes(bool useAlpha)
    {
        var art = new MonoArt();
        art.F[("pellet", 0)] = Mono(8, 8, (x, y) => true, useAlpha);                       // solid
        art.F[("pellet", 1)] = Mono(8, 8, (x, y) => x == 0, useAlpha);                     // "ring" stand-in
        art.F[("pellet", 2)] = Mono(8, 8, (x, y) => y == 0, useAlpha);                     // "dotted ring" stand-in
        var s = new GameSession(new GameOptions { FrameRand = 5, Stage = Setup(cards: new[] { (9, 19) }) }, null, art);
        var w = s.World;
        w.Pellets.AddGroup(200, 200, PelletGroup.One, 16);
        var fb = new Framebuffer();
        s.Render(fb);
        Assert.Equal(7, At(fb, 203, 205));                     // solid sprite lit everywhere
        w.Pellets.DecayFrame[0] = 1;                           // first decay cel
        s.Render(fb);
        Assert.Equal(7, At(fb, 200, 205));                    // column 0 lit
        Assert.Equal(0, At(fb, 203, 205));
        w.Pellets.DecayFrame[0] = 15;                          // second decay cel
        s.Render(fb);
        Assert.Equal(7, At(fb, 203, 200));                     // row 0 lit
        Assert.Equal(0, At(fb, 203, 205));
        w.Pellets.DecayFrame[0] = 20;                          // finished: nothing drawn
        s.Render(fb);
        Assert.Equal(0, At(fb, 203, 200));

        var plain = new GameSession(new GameOptions { FrameRand = 5, Stage = Setup(cards: new[] { (9, 19) }) });
        plain.World.Pellets.AddGroup(200, 200, PelletGroup.One, 16);
        plain.Render(fb);
        Assert.Equal(15, At(fb, 203, 205));                    // box fallback keeps working
    }

    [Fact]
    public void Spawn_clouds_use_the_cloud_sprite_and_particles_are_single_dots()
    {
        var art = new MonoArt();
        art.F[("pellet_c", 0)] = Mono(16, 16, (x, y) => true, false);
        var s = new GameSession(new GameOptions { FrameRand = 5, Stage = Setup(cards: new[] { (9, 19) }) }, null, art);
        s.World.Pellets.SpawnWithCloud = true;
        s.World.Pellets.AddGroup(300, 200, PelletGroup.One, 16);
        for (int k = 0; k < 2; k++) s.World.Pellets.Update();     // cloud frame 3: first cloud cel
        var fb = new Framebuffer();
        s.Render(fb);
        Assert.Equal(7, At(fb, 296, 196));                       // 16x16 cloud centred on the 8x8 pellet

        s.World.Particles.Init();
        s.World.Rng = new Th01.Core.Rng.IRand(3);
        s.World.Particles.Update(ParticleOrigin.Top); s.World.Particles.Update(ParticleOrigin.Top);
        var (x, y) = s.World.Particles.PixelPos(1);
        s.Render(fb);
        Assert.Equal(7, At(fb, x, y));
    }

    [Fact]
    public void New_effects_do_not_change_the_hash()
    {
        var art = new MonoArt();
        art.F[("pellet", 0)] = Mono(8, 8, (x, y) => true, false);
        var a = new GameSession(new GameOptions { FrameRand = 5, Stage = Setup(cards: new[] { (9, 19) }) });
        var b = new GameSession(new GameOptions { FrameRand = 5, Stage = Setup(cards: new[] { (9, 19) }) }, null, art);
        foreach (var g in new[] { a, b })
        {
            g.World.Pellets.AddGroup(200, 200, PelletGroup.One, 16);
            g.World.Missiles.Add(100, 100, 8, 8);
            g.World.Lasers[0].Spawn(50, 100, 50, 300, 64, 9, 20, 1);
            g.World.Particles.Init();
        }
        var fb = new Framebuffer();
        for (int i = 0; i < 50; i++) { a.Step(GameButtons.None); b.Step(GameButtons.None); b.Render(fb); }
        Assert.Equal(a.StateHash(), b.StateHash());
    }

    [Fact]
    public void Sparkles_use_the_red_invincibility_cels_of_shape8x8_and_fall_back_to_boxes()
    {
        var art = new MonoArt();
        for (int c = 0; c < 4; c++) { int cc = c; art.F[("shape8x8", 3 + c)] = Mono(8, 8, (x, y) => x == cc, false); }
        var s = new GameSession(new GameOptions { FrameRand = 5, Stage = Setup(cards: new[] { (9, 19) }) }, null, art);
        var w = s.World;
        w.Sparkles.Update(true);
        // force a known state: sparkle 0 drawn this tick with frame value 2 (stored 3) -> cel 1; sparkle 1 frame 6 -> cel 2
        w.Sparkles.Left[0] = 100; w.Sparkles.Top[0] = 300; w.Sparkles.Frame[0] = 3;
        w.Sparkles.Left[1] = 200; w.Sparkles.Top[1] = 300; w.Sparkles.Frame[1] = 7;
        for (int i = 2; i < 8; i++) w.Sparkles.Frame[i] = 0;
        var fb = new Framebuffer();
        s.Render(fb);
        Assert.Equal(10, At(fb, 101, 303));              // column 1 lit in red (V_RED = 10)
        Assert.Equal(0, At(fb, 100, 303));
        Assert.Equal(10, At(fb, 202, 303));              // frame 6 plays cel 8 - 6 = 2
        Assert.Equal(0, At(fb, 201, 303));
        var plain = new GameSession(new GameOptions { FrameRand = 5, Stage = Setup(cards: new[] { (9, 19) }) });
        plain.World.Sparkles.Update(true);
        plain.World.Sparkles.Left[0] = 100; plain.World.Sparkles.Top[0] = 300; plain.World.Sparkles.Frame[0] = 3;
        plain.Render(fb);
        Assert.Equal(14, At(fb, 100, 300));              // box fallback
    }

    [Fact]
    public void Lasers_are_bars_as_wide_as_their_width_and_boss_hp_is_a_dithered_bar()
    {
        var s = new GameSession(new GameOptions { FrameRand = 5, Stage = Setup(cards: new[] { (9, 19) }) });
        var w = s.World;
        w.Lasers[0].Spawn(300, 100, 300, 300, 8 * 4, 7, 100, 4);       // 4 px wide, 4 px per frame
        w.Lasers[0].Update();
        var fb = new Framebuffer();
        s.Render(fb);
        Assert.Equal(7, At(fb, 300, 102));
        Assert.Equal(7, At(fb, 303, 102));
        Assert.Equal(0, At(fb, 304, 102));

        var b = new GameSession(new GameOptions { FrameRand = 5, Stage = new StageSetup { StageId = 4, Cards = new bool[10, 20], Obstacles = new byte[10, 20] } });
        var boss = b.World.Boss!;
        boss.Hp = 8;
        b.Render(fb);
        Assert.Equal(10, At(fb, 128, 48));               // points 0 and 1: red
        Assert.Equal(10, At(fb, 128 + 8 * 1 + 3, 60));
        Assert.Equal(10, At(fb, 128 + 8 * 3, 48));       // points 2..5: red/white dither, even column red
        Assert.Equal(7, At(fb, 128 + 8 * 3 + 1, 48));
        Assert.Equal(7, At(fb, 128 + 8 * 6, 48));        // points 6 and 7: white
        Assert.Equal(0, At(fb, 128 + 8 * 8, 48));        // no 9th point
        Assert.Equal(0, At(fb, 128 + 7, 48));            // the 8th column of a point is the gap
    }
}
