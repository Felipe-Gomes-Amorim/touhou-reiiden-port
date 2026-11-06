namespace Th01.Core.Game;

/// <summary>
/// Timing and geometry of the full-screen results screen shown after a boss (the original calls it "TOTLE").
/// The stage stays on screen for 50 frames, then the CLEAR3 picture slides in row by row (8 more rows join every 2
/// frames; even rows slide to the left and odd rows to the right, 32 dots at a time, 20 steps per row), and the numbers
/// follow one after the other, every digit counting through two loops of 0..9 before it settles. Checked against the
/// running original (T5, MEASURED-GAMEPLAY 16c/16d): the sweep front grows 8 rows per 2 ticks, the trailing edge follows
/// 40 ticks later, the picture is whole 140 ticks after the start, and the digits run ~106 ticks.
/// ref: th01/main/bonus.cpp totle_animate(), totle_pagetrans_animate(), totle_metric_digit_animate()
/// </summary>
public static class TotleTimeline
{
    public const int SceneTicks = 50;                 // frame_delay(50) before the picture starts to slide in
    public const int StepTicks = 2;                   // frame_delay(2) per slide step
    public const int RowsPerStep = 8;
    public const int StepsPerRow = 640 / 32;          // the row is done after 20 chunks of 32 dots
    const int ScreenRows = 400;
    public const int Steps = ScreenRows / RowsPerStep + StepsPerRow;   // 50 groups + 20 steps = 70
    public const int SlideDots = 32;

    public const int PictureAt = SceneTicks + Steps * StepTicks;   // the slide is over: stage number appears
    public const int TimeAt = PictureAt + 30;                      // frame_delay(30), then the metrics one by one
    public const int StageNumberLeft = 224, StageNumberTop = 14;
    public const int MetricLeft = 224, DigitW = 16;
    public const int TimeTop = 86, ComboTop = 107, ResourcesTop = 128, StageMetricTop = 149, TotalTop = 196;

    /// <summary>How many slide steps the given row has had after <paramref name="elapsed"/> ticks (0..<see cref="StepsPerRow"/>).</summary>
    public static int RowSteps(int elapsed, int row)
    {
        int completed = (elapsed - SceneTicks) / StepTicks;        // slide steps done so far
        if (elapsed < SceneTicks) return 0;
        return Math.Clamp(completed - 1 - row / RowsPerStep, 0, StepsPerRow);
    }

    /// <summary>Progress of the whole slide, 0..<see cref="Steps"/> (the palette follows it).</summary>
    public static int SlideProgress(int elapsed) => elapsed < SceneTicks ? 0 : Math.Min(Steps, (elapsed - SceneTicks) / StepTicks);

    // ---- the digits (totle_metric_digit_animate) ----

    /// <summary>
    /// One digit step is delay(4): 4 ms, i.e. 0.226 of a tick (the draw and the sound call add a little; the measured total of
    /// ~81 ticks for ~400 steps in the original gives about the same). In thousandths of a tick.
    /// </summary>
    public const int StepMilli = 226;
    /// <summary>frame_delay(5) after each group of digits.</summary>
    public const int GroupDelayMilli = 5 * 1000;

    /// <param name="Place">Column (can be -1) of the 16x16 numeral, counted from x = 224.</param>
    /// <param name="Top">Row of the numeral.</param>
    /// <param name="Digit">The value it settles on.</param>
    /// <param name="StartMilli">When it starts counting, in thousandths of a tick after <see cref="TimeAt"/>.</param>
    public readonly record struct DigitSlot(int Place, int Top, int Digit, int StartMilli)
    {
        /// <summary>It counts 0..9 twice and stops on its value: 10 + value delays.</summary>
        public int Steps => 10 + Digit % 10;
    }

    /// <summary>The digits in the order the screen counts them, with the moment each one starts; <paramref name="endMilli"/> is when the last group delay is over.</summary>
    public static DigitSlot[] Slots(StageBonus b, out int endMilli)
    {
        var list = new List<DigitSlot>(27);
        int now = 0;
        void Group(int top, int first, params int[] digits)
        {
            for (int i = 0; i < digits.Length; i++)
            {
                var slot = new DigitSlot(first + i, top, digits[i], now);
                list.Add(slot);
                now += slot.Steps * StepMilli;
            }
            now += GroupDelayMilli;
        }
        int[] Four(int v) => [v / 1000 % 10, v / 100 % 10, v / 10 % 10, v % 10, 0];
        Group(TimeTop, 0, Four(b.Time));
        Group(ComboTop, 0, Four(b.CardCombo));
        Group(ResourcesTop, 0, Four(b.Resources));
        int s = b.StageNumber;
        Group(StageMetricTop, -1, s / 10000 % 10, s / 1000 % 10, s / 100 % 10, s / 10 % 10, 0, 0);
        uint t = b.Total;
        Group(TotalTop, -1, (int)(t / 100000 % 10), (int)(t / 10000 % 10), (int)(t / 1000 % 10), (int)(t / 100 % 10), (int)(t / 10 % 10), (int)(t % 10));
        endMilli = now;
        return [.. list];
    }

    /// <summary>The tick the keys start to be sensed: when the last group delay of the numbers is over.</summary>
    public static int InputAt(StageBonus b)
    {
        Slots(b, out int end);
        return TimeAt + (end + 999) / 1000;
    }

    /// <summary>How many delay steps of the digits are over <paramref name="milli"/> thousandths of a tick after <see cref="TimeAt"/>.</summary>
    public static int StepsDone(DigitSlot[] slots, int milli)
    {
        int n = 0;
        foreach (var s in slots) n += Math.Clamp((milli - s.StartMilli) / StepMilli, 0, s.Steps);
        return n;
    }

    /// <summary>
    /// What a digit shows <paramref name="done"/> steps into its count, or -1 for nothing yet / a blank numeral. Past the first
    /// loop the original indexes the numeral sheet without "% 10", so 10..17 show 2 3 0 1 6 7 4 5 and 18, 19 show nothing.
    /// </summary>
    public static int Numeral(int done, int steps, int value)
    {
        if (done >= steps) return value % 10;                  // settled: the proper numeral is put last
        int i = done % 20, m = i % 10;
        int v = m / 4 * 4 + i % 4;
        return v < 10 ? v : -1;
    }

    // ---- the palette (totle_pagetrans_animate) ----

    /// <summary>
    /// How many palette levels the picture's colours have been stepped towards after <paramref name="elapsed"/> ticks:
    /// one step every second iteration (every 16 rows) once the first rows are done, i.e. from 44 ticks into the sweep, every
    /// 4 ticks, 25 times. Measured: the menu palette starts to fall 39-44 ticks in, one level per 4 ticks.
    /// </summary>
    public static int PaletteSteps(int elapsed)
    {
        int k = (elapsed - SceneTicks) / StepTicks - 1;          // last iteration that has ended
        if (k < 21) return 0;
        return Math.Min(25, (k - 21) / 2 + 1);
    }
}
