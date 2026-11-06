using Th01.Core.Game;
using Th01.Core.Graphics;
using Th01.Core.Input;
using Th01.Core.Sim;

namespace Th01.Core.Tests.Game;

/// <summary>The full-screen results picture after a boss: it slides in over the stage, then the numbers appear one by one.</summary>
public class TotleScreenTests
{
    const byte Scene = 5, Picture = 9, Digit = 12;

    sealed class Art : IGameArt
    {
        public bool WithPicture = true;
        public int FrameCount(string sheet) => 0;
        public bool TryGetBackground(out ArtFrame art)
        {
            art = new ArtFrame(640, 400, Enumerable.Repeat(Scene, 640 * 400).ToArray(), Array.Empty<byte>(), Array.Empty<byte>());
            return true;
        }

        public bool TryGetFrame(string sheet, int frame, out ArtFrame art)
        {
            art = default;
            if (sheet == "CLEAR3.GRP" && frame == 0 && WithPicture)
            {
                var pal = new byte[48];
                for (int i = 0; i < 16; i++) { pal[i * 3] = 15; pal[i * 3 + 1] = 15; pal[i * 3 + 2] = (byte)i; }   // pale picture palette
                art = new ArtFrame(640, 400, Enumerable.Repeat(Picture, 640 * 400).ToArray(), Array.Empty<byte>(), pal);
                return true;
            }
            if (sheet == "NUMB.PTN" && frame is >= 0 and <= 12)
            {
                art = new ArtFrame(32, 32, Enumerable.Repeat(Digit, 32 * 32).ToArray(), Array.Empty<byte>(), Array.Empty<byte>());
                return true;
            }
            return false;
        }
    }

    static GameSession AfterABoss(IGameArt? art)
    {
        var stage = new StageSetup
        {
            StageId = StageSetup.BossStage, Route = 0,
            Cards = new bool[StageSetup.Rows, StageSetup.Cols],
            Obstacles = new byte[StageSetup.Rows, StageSetup.Cols],
        };
        var g = new GameSession(new GameOptions { FrameRand = 3, Stage = stage, Lives = 3, Bombs = 1 }, null, art);
        for (int i = 0; i < SinGyoku.EntranceFrames; i++) g.Step(GameButtons.None);
        g.World.StageCleared = true;
        g.Step(GameButtons.None);
        Assert.Equal(SessionPhase.StageBonus, g.Phase);
        return g;
    }

    static void StepTo(GameSession g, int tick)
    {
        while (g.BonusElapsedForTests < tick) g.Step(GameButtons.None);
    }

    static Framebuffer Render(GameSession g)
    {
        var fb = new Framebuffer();
        g.Render(fb);
        return fb;
    }

    [Fact]
    public void The_timeline_follows_the_original_delays()
    {
        Assert.Equal(50, TotleTimeline.SceneTicks);
        Assert.Equal(70, TotleTimeline.Steps);                       // 50 groups of 8 rows, plus 20 steps for the last
        Assert.Equal(190, TotleTimeline.PictureAt);
        Assert.Equal(220, TotleTimeline.TimeAt);
    }

    [Fact]
    public void The_digits_count_for_about_the_measured_time()
    {
        // MEASURED (T5 16d): the screen changes every tick for ~106 ticks, then the keys are read. A typical result.
        var b = StageBonus.ComputeTotle(5, 700, 12, 3, 2);
        int ticks = TotleTimeline.InputAt(b) - TotleTimeline.TimeAt;
        Assert.InRange(ticks, 95, 125);
        // Every digit counts 10 + its value steps (two loops), five frame_delay(5) in all
        var slots = TotleTimeline.Slots(b, out int end);
        Assert.Equal(27, slots.Length);
        Assert.Equal(slots.Sum(s => s.Steps) * TotleTimeline.StepMilli + 5 * TotleTimeline.GroupDelayMilli, end);
        // after the first loop the original indexes the sheet without % 10 (bonus.cpp scramble table)
        Assert.Equal(new[] { 2, 3, 0, 1, 6, 7, 4, 5, -1, -1 }, Enumerable.Range(10, 10).Select(i => TotleTimeline.Numeral(i, 25, 0)).ToArray());
        Assert.Equal(7, TotleTimeline.Numeral(7, 17, 7));
        Assert.Equal(7, TotleTimeline.Numeral(17, 17, 7));              // settled on its value
    }

    [Fact]
    public void The_palette_steps_one_level_every_four_ticks_from_44_ticks_into_the_sweep()
    {
        int start = TotleTimeline.SceneTicks;
        Assert.Equal(0, TotleTimeline.PaletteSteps(start + 42));
        Assert.Equal(1, TotleTimeline.PaletteSteps(start + 44));
        Assert.Equal(1, TotleTimeline.PaletteSteps(start + 47));
        Assert.Equal(2, TotleTimeline.PaletteSteps(start + 48));
        Assert.Equal(15, TotleTimeline.PaletteSteps(start + 44 + 14 * 4));          // every level reachable by here (T5: done by ~94)
        Assert.Equal(25, TotleTimeline.PaletteSteps(TotleTimeline.PictureAt));
    }

    [Fact]
    public void Rows_start_sliding_from_the_top_eight_at_a_time_and_each_takes_twenty_steps()
    {
        Assert.Equal(0, TotleTimeline.RowSteps(10, 0));              // the stage is still shown
        Assert.Equal(0, TotleTimeline.RowSteps(52, 0));              // first step happens, no row has moved yet
        Assert.Equal(1, TotleTimeline.RowSteps(54, 0));
        Assert.Equal(1, TotleTimeline.RowSteps(54, 7));              // rows 0..7 move together
        Assert.Equal(0, TotleTimeline.RowSteps(54, 8));              // the next group joins one step later
        Assert.Equal(1, TotleTimeline.RowSteps(56, 8));
        Assert.Equal(20, TotleTimeline.RowSteps(92, 0));
        Assert.Equal(20, TotleTimeline.RowSteps(TotleTimeline.PictureAt, 399));   // the last row ends exactly when the numbers begin
        Assert.Equal(19, TotleTimeline.RowSteps(TotleTimeline.PictureAt - 2, 399));
    }

    [Fact]
    public void The_stage_stays_until_tick_fifty_then_the_picture_slides_in_row_by_row()
    {
        var g = AfterABoss(new Art());
        StepTo(g, 40);
        Assert.Equal(Scene, Render(g).GetPixel(300, 200));           // untouched stage picture

        StepTo(g, 54);                                               // row 0 has slid 32 dots to the left
        var fb = Render(g);
        Assert.Equal(Picture, fb.GetPixel(620, 0));                  // the new picture enters from the right (even rows)...
        Assert.Equal(Scene, fb.GetPixel(100, 0));
        Assert.Equal(Scene, fb.GetPixel(620, 1));                    // ...an odd row has not moved yet at this step
        StepTo(g, 56);
        fb = Render(g);
        Assert.Equal(Picture, fb.GetPixel(5, 1));                    // odd rows are entered from the left
        Assert.Equal(Scene, fb.GetPixel(300, 399));                  // the bottom rows have not started

        StepTo(g, TotleTimeline.PictureAt);
        fb = Render(g);
        Assert.Equal(Picture, fb.GetPixel(0, 0));
        Assert.Equal(Picture, fb.GetPixel(639, 399));
        Assert.Equal(Picture, fb.GetPixel(300, 399));
    }

    [Fact]
    public void The_palette_follows_the_slide_and_ends_on_the_picture_palette()
    {
        var g = AfterABoss(new Art());
        StepTo(g, TotleTimeline.PictureAt);
        var fb = Render(g);
        for (int i = 0; i < 16; i++)
            Assert.Equal(0xFFFFFF00u | (uint)(i * 17), fb.Palette[i]);       // 15,15,i: the picture's own colours
    }

    [Fact]
    public void Numbers_appear_one_after_the_other_at_the_original_places()
    {
        var g = AfterABoss(new Art());
        StepTo(g, TotleTimeline.PictureAt - 1);
        Assert.Equal(Picture, Render(g).GetPixel(224 + 4, 14 + 4));  // no stage number yet
        StepTo(g, TotleTimeline.PictureAt);
        Assert.Equal(Digit, Render(g).GetPixel(224 + 4, 14 + 4));    // the two 32x32 stage digits
        Assert.Equal(Digit, Render(g).GetPixel(256 + 4, 14 + 4));
        Assert.Equal(Picture, Render(g).GetPixel(224 + 4, 86 + 4));  // the time is not there yet

        StepTo(g, TotleTimeline.TimeAt);
        Assert.Equal(Digit, Render(g).GetPixel(224 + 4, 86 + 4));    // 16x16 numerals from x = 224, row 86 (counting from 0)
        Assert.Equal(Picture, Render(g).GetPixel(224 + 4, 107 + 4)); // one metric at a time, top to bottom
        StepTo(g, TotleTimeline.InputAt(g.Bonus!.Value));
        var fb = Render(g);
        Assert.Equal(Digit, fb.GetPixel(224 + 4, 107 + 4));
        Assert.Equal(Digit, fb.GetPixel(224 + 4, 128 + 4));
        Assert.Equal(Digit, fb.GetPixel(224 - 16 + 4, 149 + 4));     // the 5 digit number starts one column to the left
        Assert.Equal(Digit, fb.GetPixel(224 - 16 + 4, 196 + 4));     // so does the total
    }

    [Fact]
    public void The_route_menu_stays_on_screen_under_the_sweep_with_its_own_palette()
    {
        var g = AfterABoss(new Art());
        g.World.RouteChosen = 1;
        StepTo(g, 10);                                               // the 50 ticks before the sweep: the menu is still up
        var fb = Render(g);
        Assert.Equal(9 * 17u, fb.Palette[1] >> 16 & 0xFF);
        Assert.Equal(15 * 17u, fb.Palette[3] >> 16 & 0xFF);          // Jigoku selected: white, the other grey
        Assert.Equal(7, fb.GetPixel(128 + 4, 300 + 4));              // the cursor beside 地獄へ (plain block: no sprite in the test)
        StepTo(g, TotleTimeline.PictureAt);
        Assert.Equal(Picture, Render(g).GetPixel(128 + 4, 300 + 4)); // the sweep has replaced it
    }


    [Fact]
    public void Keys_are_ignored_until_the_numbers_are_drawn_and_then_a_release_closes_the_screen()
    {
        var g = AfterABoss(null);
        for (int i = 0; i < 20; i++) { g.Step(GameButtons.Shot); g.Step(GameButtons.None); }
        Assert.Equal(SessionPhase.StageBonus, g.Phase);
        Assert.Equal(SessionOutcome.Running, g.Outcome);
        while (g.BonusElapsedForTests < TotleTimeline.InputAt(g.Bonus!.Value)) g.Step(GameButtons.None);
        g.Step(GameButtons.Shot); g.Step(GameButtons.None);
        Assert.Equal(SessionOutcome.Cleared, g.Outcome);
    }

    [Fact]
    public void Without_the_picture_the_plain_box_is_drawn_and_nothing_throws()
    {
        var g = AfterABoss(new Art { WithPicture = false });
        StepTo(g, 80);
        var fb = Render(g);
        Assert.Equal(0, fb.GetPixel(100, 180));                       // the dithered-free fallback box (solid black here)
    }

    [Fact]
    public void Drawing_the_results_screen_does_not_change_the_simulation()
    {
        var plain = AfterABoss(null);
        var drawn = AfterABoss(new Art());
        var fb = new Framebuffer();
        for (int i = 0; i < TotleTimeline.InputAt(plain.Bonus!.Value) + 5; i++)
        {
            plain.Step(GameButtons.None); drawn.Step(GameButtons.None);
            drawn.Render(fb);
            Assert.Equal(plain.StateHash(), drawn.StateHash());
        }
    }
}
