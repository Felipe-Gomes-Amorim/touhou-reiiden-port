namespace Th01.Core.Game;

/// <summary>Screen edge (or corner) the ambient particles fly in from; they travel towards the opposite side.</summary>
public enum ParticleOrigin
{
    Top = 0, TopRight = 1, Right = 2, BottomRight = 3, Bottom = 4, BottomLeft = 5, Left = 6, TopLeft = 7,
}

/// <summary>
/// The ambient particle stream of the second-act bosses (single white dots drifting across the whole screen). It is
/// decoration, but it draws two random numbers per spawn, so it is part of the RNG sequence of the fight.
///
/// The original keeps its 40 particles in parallel arrays inside one structure and indexes slot 40 once, on the very
/// first spawn cycle (the spawn counter is incremented before it is tested and compared with ">"). That access lands on
/// neighbouring arrays and corrupts them. To reproduce this bit for bit, the structure is kept as a byte block with the
/// original's field offsets (16-bit values, little endian) instead of as typed arrays.
/// ref: th01/main/particle.cpp, particle.hpp
/// </summary>
public sealed class ParticleField
{
    public const int Count = 40;
    const int ScreenW = 640, ScreenH = 400;

    // Field offsets inside the block (spawn_interval, velocity_base_max, then arrays of 2-byte values, then bytes).
    const int OffInterval = 0;
    const int OffBaseMax = 2;
    const int OffX = 4;
    const int OffY = OffX + 2 * Count;
    const int OffVx = OffY + 2 * Count;
    const int OffVy = OffVx + 2 * Count;
    const int OffAlive = OffVy + 2 * Count;
    const int OffBase = OffAlive + Count;
    const int OffCycle = OffBase + Count;
    const int Size = OffCycle + 2;

    readonly GameWorld _w;
    readonly byte[] _m = new byte[Size];

    public ParticleField(GameWorld w) => _w = w;

    short S16(int off) => (short)(_m[off] | (_m[off + 1] << 8));
    void W16(int off, int v) { _m[off] = (byte)v; _m[off + 1] = (byte)(v >> 8); }

    public int SpawnInterval => S16(OffInterval);
    public int VelocityBaseMax => S16(OffBaseMax);
    public int SpawnCycle => _m[OffCycle];

    public bool Alive(int i) => _m[OffAlive + i] != 0;
    /// <summary>Position in screen pixels (arithmetic shift of the 12.4 fixed-point value).</summary>
    public (int x, int y) PixelPos(int i) => (S16(OffX + 2 * i) >> 4, S16(OffY + 2 * i) >> 4);
    public (int x, int y) Velocity(int i) => (S16(OffVx + 2 * i), S16(OffVy + 2 * i));
    public int VelocityBase(int i) => _m[OffBase + i];
    public (int x, int y) RawPos(int i) => (S16(OffX + 2 * i), S16(OffY + 2 * i));

    /// <summary>Forgets every particle and restores the two tunables (the spawn counter is NOT reset).</summary>
    public void Init()
    {
        for (int i = 0; i < Count; i++) _m[OffAlive + i] = 0;
        W16(OffInterval, 2);
        W16(OffBaseMax, 10);
    }

    /// <summary>One frame: maybe spawn one particle, move all, retire those that left the screen.</summary>
    public void Update(ParticleOrigin origin)
    {
        var rng = _w;
        // The counter is bumped first (so slot 0 is skipped on the very first lap) and the wrap uses ">".
        _m[OffCycle]++;
        int interval = SpawnInterval;
        if (_m[OffCycle] > interval * Count) _m[OffCycle] = 0;

        if (_m[OffCycle] % interval == 0)
        {
            int i = (byte)(_m[OffCycle] / interval);   // 0..40
            if (_m[OffAlive + i] == 0)
            {
                _m[OffAlive + i] = 1;
                _m[OffBase + i] = (byte)(rng.Rng.Next() % VelocityBaseMax + 1);
                int vb = _m[OffBase + i];
                Spawn(i, origin, vb);
            }
        }

        for (int i = 0; i < Count; i++)
        {
            if (_m[OffAlive + i] == 0) continue;
            int x = (short)(S16(OffX + 2 * i) + S16(OffVx + 2 * i));
            int y = (short)(S16(OffY + 2 * i) + S16(OffVy + 2 * i));
            W16(OffX + 2 * i, x);
            W16(OffY + 2 * i, y);
            if (!(x >= 0 && y <= (ScreenH - 1) * 16 && x <= (ScreenW - 1) * 16 && y >= 0)) _m[OffAlive + i] = 0;
        }
    }

    void Spawn(int i, ParticleOrigin origin, int vb)
    {
        void X(int v) => W16(OffX + 2 * i, v);
        void Y(int v) => W16(OffY + 2 * i, v);
        void Vx(int v) => W16(OffVx + 2 * i, v);
        void Vy(int v) => W16(OffVy + 2 * i, v);
        const int right = (ScreenW - 1) * 16, bottom = (ScreenH - 1) * 16;

        if (i % 2 == 0)
        {
            switch (origin)
            {
                case ParticleOrigin.Top: case ParticleOrigin.TopRight: case ParticleOrigin.TopLeft:
                    X(_w.Rng.Next() % ScreenW << 4); Y(0); break;
                case ParticleOrigin.BottomRight: case ParticleOrigin.Bottom: case ParticleOrigin.BottomLeft:
                    X(_w.Rng.Next() % ScreenW << 4); Y(bottom); break;
                case ParticleOrigin.Right: case ParticleOrigin.Left:
                    X(origin == ParticleOrigin.Right ? right : 0); Y(_w.Rng.Next() % ScreenH << 4); break;
            }
        }
        else
        {
            switch (origin)
            {
                case ParticleOrigin.TopRight: case ParticleOrigin.Right: case ParticleOrigin.BottomRight:
                    X(right); Y(_w.Rng.Next() % ScreenH << 4); break;
                case ParticleOrigin.BottomLeft: case ParticleOrigin.Left: case ParticleOrigin.TopLeft:
                    X(0); Y(_w.Rng.Next() % ScreenH << 4); break;
                case ParticleOrigin.Top: case ParticleOrigin.Bottom:
                    X(_w.Rng.Next() % ScreenW << 4); Y(origin == ParticleOrigin.Top ? 0 : bottom); break;
            }
        }

        // 0.8125 px and 0.5 px per step are 13 and 8 subpixels.
        switch (origin)
        {
            case ParticleOrigin.Top: Vy(vb << 4); Vx(0); break;
            case ParticleOrigin.TopRight: Vx(-(vb * 13)); Vy(vb * 8); break;
            case ParticleOrigin.Right: Vx(-vb << 4); Vy(0); break;
            case ParticleOrigin.BottomRight: Vx(-(vb * 13)); Vy(-vb * 8); break;
            case ParticleOrigin.Bottom: Vy(-vb << 4); Vx(0); break;
            case ParticleOrigin.BottomLeft: Vx(vb * 13); Vy(-vb * 8); break;
            case ParticleOrigin.Left: Vx(vb << 4); Vy(0); break;
            case ParticleOrigin.TopLeft: Vx(vb * 13); Vy(vb * 8); break;
        }
    }

    public void Hash(ref StateHasher h)
    {
        for (int k = 0; k < Size; k += 2) h.Add(_m[k] | (_m[k + 1] << 8));
    }
}
