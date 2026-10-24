using Th01.Core.Audio;
using Th01.Core.Game;
using Th01.Core.Graphics;
using Th01.Core.Input;

namespace Th01.Core.Tests.Game;

/// <summary>Drawing fixes reported while playing: SinGyoku's blue square and the lasers.</summary>
public class RenderFixTests
{
    static StageSetup BossStage(int id) => new()
    {
        StageId = id, Route = 1,
        Cards = new bool[StageSetup.Rows, StageSetup.Cols],
        Obstacles = new byte[StageSetup.Rows, StageSetup.Cols],
    };

    /// <summary>The sheets the real game has: BOSS1.BOS 8 cels, BOSS1_2.BOS 3, BOSS1_3.BOS 5 (96x96), laser_s 8 bars.</summary>
    sealed class SheetArt : IGameArt
    {
        public readonly Dictionary<(string, int), ArtFrame> Frames = new();
        public readonly List<(string, int)> Missed = new();
        public int FrameCount(string sheet) => 0;
        public bool TryGetFrame(string sheet, int frame, out ArtFrame art)
        {
            if (Frames.TryGetValue((sheet, frame), out art)) return true;
            Missed.Add((sheet, frame));
            return false;
        }
        public bool TryGetBackground(out ArtFrame art) { art = default; return false; }

        public void AddSheet(string name, int count, int w, int h, byte color)
        {
            for (int i = 0; i < count; i++)
                Frames[(name, i)] = new ArtFrame(w, h, Enumerable.Repeat(color, w * h).ToArray(), Array.Empty<byte>(), Array.Empty<byte>());
        }
    }

    static SheetArt SinGyokuArt()
    {
        var art = new SheetArt();
        art.AddSheet("BOSS1.BOS", 8, 96, 96, 3);
        art.AddSheet("BOSS1_2.BOS", 3, 96, 96, 4);
        art.AddSheet("BOSS1_3.BOS", 5, 96, 96, 5);
        return art;
    }

    [Theory]
    [InlineData(0)]    // woman, then man (random pattern 1 and 3 use the man)
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(3)]
    public void SinGyoku_never_asks_for_a_sprite_cel_the_sheets_do_not_have(int pattern)
    {
        var art = SinGyokuArt();
        var g = new GameSession(new GameOptions { FrameRand = 7, Stage = BossStage(StageSetup.BossStage), Rank = Rank.Normal, Lives = 99 }, null, art);
        for (int i = 0; i < SinGyoku.EntranceFrames; i++) g.Step(GameButtons.None);
        var b = (SinGyoku)g.World.Boss!;
        b.Phase = 2; b.PatternCur = pattern; b.PhaseFrame = 0; b.Hp = 5;
        g.World.PlayerInvincible = true;
        g.World.PlayerInvincibilityTime = 100000;
        var fb = new Framebuffer();
        art.Missed.Clear();
        for (int i = 0; i < 270 && g.Phase == SessionPhase.Playing; i++)
        {
            g.Step(GameButtons.None);
            g.Render(fb);
        }
        Assert.DoesNotContain(art.Missed, m => m.Item1.StartsWith("BOSS1"));
    }

    [Fact]
    public void The_man_keeps_his_attack_cel_through_the_second_attack_because_cel_5_does_not_exist()
    {
        var g = new GameSession(new GameOptions { FrameRand = 7, Stage = BossStage(StageSetup.BossStage) });
        for (int i = 0; i < SinGyoku.EntranceFrames; i++) g.Step(GameButtons.None);
        var b = (SinGyoku)g.World.Boss!;
        g.World.PlayerInvincible = true;
        g.World.PlayerInvincibilityTime = 100000;
        b.Phase = 2; b.PatternCur = 1; b.PhaseFrame = 0;
        var cels = new Dictionary<int, int>();
        for (int i = 0; i < 262; i++)
        {
            g.Step(GameButtons.None);
            if (b.PersonVisible) cels[b.PhaseFrame] = b.PersonCel;
        }
        Assert.Equal(3, cels[120]);      // still
        Assert.Equal(4, cels[140]);      // attack 1 (the man's only attack cel)
        Assert.Equal(4, cels[170]);      // attack 2: PersonImage is 5, the sheet has 5 cels
        Assert.Equal(3, cels[200]);      // still again from frame 185
    }

    [Fact]
    public void The_flash_uses_the_plain_cel_on_the_first_and_last_steps_and_the_form_cel_in_between()
    {
        var g = new GameSession(new GameOptions { FrameRand = 7, Stage = BossStage(StageSetup.BossStage) });
        var b = (SinGyoku)g.World.Boss!;
        b.Phase = 2; b.PatternCur = 1;     // the man (form 1)
        int Cel(int frame) { b.PhaseFrame = frame; return b.Flashing ? b.FlashCel : -1; }
        Assert.Equal(-1, Cel(99));
        Assert.Equal(0, Cel(100));
        Assert.Equal(0, Cel(104));
        Assert.Equal(2, Cel(105));
        Assert.Equal(2, Cel(114));
        Assert.Equal(-1, Cel(115));
        Assert.Equal(-1, Cel(239));
        Assert.Equal(2, Cel(240));
        Assert.Equal(2, Cel(249));
        Assert.Equal(0, Cel(250));
        Assert.Equal(0, Cel(259));
        Assert.Equal(-1, Cel(260));
        b.PatternCur = 0;                   // the woman: form cel 1
        Assert.Equal(1, Cel(110));
    }

    [Fact]
    public void A_laser_is_drawn_from_the_embedded_bar_table_and_stops_one_cell_short_of_its_tip()
    {
        var art = new SheetArt();
        // laser_s: frame = width cel, row = x & 7, a bar of (cel + 1) dots at column "row"
        for (int cel = 0; cel < 8; cel++)
        {
            var px = new byte[16 * 8];
            for (int row = 0; row < 8; row++) for (int k = 0; k <= cel; k++) px[row * 16 + row + k] = 1;
            art.Frames[("laser_s", cel)] = new ArtFrame(16, 8, px, (byte[])px.Clone(), Array.Empty<byte>());
        }
        var g = new GameSession(new GameOptions { FrameRand = 7, Stage = BossStage(2 * StageSetup.StagesPerScene - 1) }, null, art);
        var l = g.World.Lasers[0];
        l.Spawn(100, 100, 100, 300, 48, 7, 20, 4);          // straight down, 6 px per frame, 4 dots wide
        for (int i = 0; i < 5; i++) l.Update();             // length 30
        var fb = new Framebuffer();
        g.Render(fb);
        // the column of the ray: x = 100..103; rows 100..129 minus the unflushed last cell
        Assert.Equal(7, fb.GetPixel(100, 100));
        Assert.Equal(7, fb.GetPixel(103, 128));
        Assert.Equal(0, fb.GetPixel(104, 110));
        Assert.Equal(0, fb.GetPixel(99, 110));
        Assert.Equal(0, fb.GetPixel(100, 129));              // the last cell (row 129) is not written
    }

    [Fact]
    public void Without_the_table_a_laser_falls_back_to_plain_bars()
    {
        var g = new GameSession(new GameOptions { FrameRand = 7, Stage = BossStage(2 * StageSetup.StagesPerScene - 1) });
        var l = g.World.Lasers[0];
        l.Spawn(100, 100, 100, 300, 48, 7, 20, 4);
        for (int i = 0; i < 5; i++) l.Update();
        var fb = new Framebuffer();
        g.Render(fb);
        Assert.Equal(7, fb.GetPixel(101, 120));
        Assert.Equal(0, fb.GetPixel(105, 120));
    }
}
