namespace Th01.Core.Timing;

/// <summary>
/// The original game is locked to the PC-98 VSync, not to 60 FPS. One simulation
/// tick corresponds to one VSync. See docs/RECON.md section 3.
/// </summary>
public static class TickRate
{
    // ref: th01/hardware/frmdelay.cpp, vsync.cpp (frame_delay() waits on VSync counts).
    // Confirmed: ReC98's CONTRIBUTING.md names "the native 56.423 FPS rate" for hardware frames, and its th05/end/allcast.cpp
    // converts frames to time with 56.423. This is the 640x400 24.83 kHz mode (about 440 lines per frame).
    // The value is a rounded measurement, so it is kept as an exact rational: 56423 / 1000 ticks per second.
    public const long TicksPerThousandSeconds = 56_423;

    public const double Hz = TicksPerThousandSeconds / 1000.0;

    /// <summary>Rounded tick length (17723 us), for display and for the legacy FixedTimestep constructor only.</summary>
    public const long MicrosecondsPerTick = 1_000_000_000L / TicksPerThousandSeconds;
}
