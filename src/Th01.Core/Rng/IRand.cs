namespace Th01.Core.Rng;

/// <summary>
/// The original RNG: master.lib's irand(), a 32-bit LCG (the Borland C++ 3.1 rand()).
/// Returns 0..32767. See docs/RECON.md section 4.
/// </summary>
public struct IRand
{
    // ref: libs/master.lib/random.asm (as read from the ReC98 reference clone)
    const uint Multiplier = 0x015A4E35;

    uint _seed;

    public IRand(uint seed) => _seed = seed;

    public uint Seed => _seed;

    public int Next()
    {
        unchecked { _seed = _seed * Multiplier + 1; }
        return (int)((_seed >> 16) & 0x7FFF);
    }
}
