namespace Th01.Core.Game;

/// <summary>
/// Timing and geometry of the full-screen results screen shown after a boss (the original calls it "TOTLE").
/// The stage stays on screen for 50 frames, then the CLEAR3 picture slides in row by row (8 more rows join every 2
/// frames; even rows slide to the left and odd rows to the right, 32 dots at a time, 20 steps per row), and the numbers
/// follow one after the other. All of it from the source; not yet measured against the running original (TODO(T5)).
/// ref: th01/main/bonus.cpp totle_animate(), totle_pagetrans_animate()
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
    public const int ComboAt = TimeAt + 5;                         // frame_delay(5) after each
    public const int ResourcesAt = ComboAt + 5;
    public const int StageMetricAt = ResourcesAt + 5;
    public const int TotalAt = StageMetricAt + 5;
    public const int InputAt = TotalAt + 5;                        // the keys are sensed only now

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
}
