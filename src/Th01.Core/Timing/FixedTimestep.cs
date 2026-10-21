namespace Th01.Core.Timing;

/// <summary>
/// Converts wall-clock time (supplied by the platform layer) into a whole number of
/// simulation ticks. Integer microseconds only; the core never reads a clock itself.
/// With the default rate the conversion is exact (no drift from rounding the tick length).
/// Also provides pause and single-step for debugging.
/// </summary>
public sealed class FixedTimestep
{
    // A tick is complete when _accumulator reaches _threshold; each elapsed microsecond adds _perMicrosecond.
    readonly long _perMicrosecond;
    readonly long _threshold;
    readonly int _maxTicksPerUpdate;
    long _accumulator;
    bool _stepRequested;

    /// <summary>Default rate: <see cref="TickRate.Hz"/>, exact.</summary>
    public FixedTimestep(int maxTicksPerUpdate = 5)
    {
        _perMicrosecond = TickRate.TicksPerThousandSeconds;
        _threshold = 1_000_000_000L;
        _maxTicksPerUpdate = maxTicksPerUpdate;
    }

    /// <summary>Custom whole-microsecond tick length (tests, experiments).</summary>
    public FixedTimestep(long tickMicroseconds, int maxTicksPerUpdate = 5)
    {
        if (tickMicroseconds <= 0) throw new ArgumentOutOfRangeException(nameof(tickMicroseconds));
        _perMicrosecond = 1;
        _threshold = tickMicroseconds;
        _maxTicksPerUpdate = maxTicksPerUpdate;
    }

    /// <summary>While paused, <see cref="Advance"/> returns 0 (except for requested single steps) and time does not accumulate.</summary>
    public bool Paused { get; set; }

    /// <summary>Ask for exactly one tick on the next <see cref="Advance"/> while paused (debug frame advance).</summary>
    public void RequestStep() => _stepRequested = true;

    /// <summary>Adds elapsed time and returns how many ticks to run now.</summary>
    public int Advance(long elapsedMicroseconds)
    {
        if (Paused)
        {
            if (!_stepRequested) return 0;
            _stepRequested = false;
            return 1;
        }
        _stepRequested = false;

        _accumulator += elapsedMicroseconds * _perMicrosecond;
        long ticks = _accumulator / _threshold;
        if (ticks > _maxTicksPerUpdate)
        {
            // Too far behind (breakpoint, window drag): drop the backlog instead of spiralling.
            ticks = _maxTicksPerUpdate;
            _accumulator = 0;
        }
        else
        {
            _accumulator -= ticks * _threshold;
        }
        return (int)ticks;
    }
}
