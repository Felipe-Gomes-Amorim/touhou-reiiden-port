namespace Th01.Core.Game;

/// <summary>
/// The ONLY place in the simulation that uses floating point (approved by T1, see docs/REQUESTS.md).
/// The original keeps the Orb's vertical force and velocity in <c>double</c>; fixed point would diverge at the
/// truncation boundaries. Rules: IEEE-754 binary64, only + - * and explicit truncation toward zero; no
/// Math.* functions. The original runs on x87 (wider temporaries): T5 verifies the possible 1-ulp difference.
/// ref: th01/main/player/orb.cpp (orb_velocity_y_update, orb_force_new)
/// </summary>
public sealed class OrbYAxis
{
    const double CoefficientOfRestitution = 0.78;
    public const double ForceStart = -8.0;

    /// <summary>Initial value of the force currently acting on the orb.</summary>
    public double Force;

    /// <summary>Clamped (+-16) copy of the last velocity; the movement itself uses the unclamped value.</summary>
    public double VelocityY;

    // Fixed-point views (value * 256, truncated toward zero) for traces; keeps float out of the callers.
    public long ForceQ8 => (long)(Force * 256.0);
    public long VelocityYQ8 => (long)(VelocityY * 256.0);

    /// <summary>Raw bits of <see cref="VelocityY"/> for carry-over between stages.</summary>
    public long VelocityYBits
    {
        get => BitConverter.DoubleToInt64Bits(VelocityY);
        set => VelocityY = BitConverter.Int64BitsToDouble(value);
    }

    public void StageStart() => Force = ForceStart; // VelocityY is NOT reset in the original

    /// <summary>Stores the clamped velocity, returns the UNCLAMPED velocity truncated toward zero.</summary>
    public int Update(int forceFrame)
    {
        double g = forceFrame / 5 + Force;
        VelocityY = g > 16.0 ? 16.0 : g < -16.0 ? -16.0 : g;
        return (int)g;
    }

    public void BounceFromSurface() => Force = -VelocityY * CoefficientOfRestitution;
    public void BounceFromTop(int forceFrame) => Force = -VelocityY - forceFrame / 4;
    public void Shot() => Force = -10.0 + VelocityY / 2.0;
    public void Immediate(int force) => Force = force;
    public void Reflect() => Force = -VelocityY;

    public void Hash(ref StateHasher h)
    {
        h.Add(Force);
        h.Add(VelocityY);
    }
}
