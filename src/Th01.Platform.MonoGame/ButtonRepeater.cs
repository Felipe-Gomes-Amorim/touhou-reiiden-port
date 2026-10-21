using Th01.Core.Input;

namespace Th01.Platform.MonoGame;

/// <summary>
/// Turns a held-state signal into press + auto-repeat (for menus): fires on the first tick, then after
/// <c>delayTicks</c>, then every <c>intervalTicks</c>. Counts ticks, not wall time, so it is deterministic.
/// </summary>
public sealed class ButtonRepeater
{
    readonly int _delay, _interval;
    readonly int[] _held = new int[16];

    public ButtonRepeater(int delayTicks = 20, int intervalTicks = 4)
    {
        if (delayTicks < 1) throw new ArgumentOutOfRangeException(nameof(delayTicks));
        if (intervalTicks < 1) throw new ArgumentOutOfRangeException(nameof(intervalTicks));
        _delay = delayTicks;
        _interval = intervalTicks;
    }

    /// <summary>Call once per tick with the currently held buttons; returns the buttons that "fire" this tick.</summary>
    public GameButtons Update(GameButtons held)
    {
        GameButtons fired = GameButtons.None;
        for (int bit = 0; bit < _held.Length; bit++)
        {
            if ((((ushort)held >> bit) & 1) == 0) { _held[bit] = 0; continue; }
            int n = _held[bit]++;
            if (n == 0 || (n >= _delay && (n - _delay) % _interval == 0)) fired |= (GameButtons)(1 << bit);
        }
        return fired;
    }
}
