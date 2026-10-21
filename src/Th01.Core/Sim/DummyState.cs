using Th01.Core.Input;
using Th01.Core.Rng;

namespace Th01.Core.Sim;

/// <summary>
/// Placeholder state used to prove the determinism pipeline (Step + StateHash).
/// Not gameplay. Replaced by real state in later phases.
/// </summary>
public sealed class DummyState
{
    IRand _rng;
    int _x = 320;
    int _y = 200;
    int _noise;
    long _tick;

    public DummyState(uint seed) => _rng = new IRand(seed);

    public long Tick => _tick;

    public void Step(GameButtons input)
    {
        if ((input & GameButtons.Left) != 0) _x -= 4;
        if ((input & GameButtons.Right) != 0) _x += 4;
        if ((input & GameButtons.Up) != 0) _y -= 4;
        if ((input & GameButtons.Down) != 0) _y += 4;
        _x = Math.Clamp(_x, 0, 639);
        _y = Math.Clamp(_y, 0, 399);
        if ((input & GameButtons.Shot) != 0) _noise ^= _rng.Next();
        _tick++;
    }

    /// <summary>FNV-1a over every field, in a fixed order and little-endian layout.</summary>
    public ulong Hash()
    {
        var h = new Fnv1a();
        h.Add(_rng.Seed);
        h.Add(_x);
        h.Add(_y);
        h.Add(_noise);
        h.Add(_tick);
        return h.Value;
    }

    struct Fnv1a
    {
        public ulong Value = 14695981039346656037UL;
        public Fnv1a() { }

        public void Add(long v)
        {
            for (int i = 0; i < 8; i++)
            {
                Value ^= (byte)(v >> (i * 8));
                Value *= 1099511628211UL;
            }
        }
    }
}
