namespace Th01.Core.Timing;

/// <summary>
/// Counts events (ticks, frames) over one-second windows of caller-supplied time and exposes the last full window's count.
/// Used for the debug ticks/s and FPS read-outs.
/// </summary>
public sealed class RateCounter
{
    long _windowMicroseconds;
    int _count;

    /// <summary>Events counted in the last completed one-second window.</summary>
    public int PerSecond { get; private set; }

    /// <summary>Total events ever counted.</summary>
    public long Total { get; private set; }

    public void Add(int events, long elapsedMicroseconds)
    {
        _count += events;
        Total += events;
        _windowMicroseconds += elapsedMicroseconds;
        while (_windowMicroseconds >= 1_000_000)
        {
            _windowMicroseconds -= 1_000_000;
            PerSecond = _count;
            _count = 0;
        }
    }
}
