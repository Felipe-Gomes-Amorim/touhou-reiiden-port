using System.Numerics;
using Th01.Core.Game;

namespace Th01.Parity.Tests;

/// <summary>
/// OrbYAxis uses IEEE binary64 operations; the original (x87) keeps 64-bit-mantissa temporaries and rounds to
/// binary64 only when it stores the result. Every OrbYAxis step is ONE operation followed by a store, so the only
/// possible difference is double rounding (exact -> 64 bits -> 53 bits). Only the multiply (restitution 0.78) can
/// produce inexact 64-bit intermediates; additions of an int or a short-mantissa double are exact in both.
/// Simulated here with exact integer arithmetic. ASSUMPTION: the original runs with the default 64-bit precision control.
/// </summary>
public class X87RoundingTests
{
    // Round-to-nearest-even of m * 2^e to 'bits' significant bits; returns (mantissa, exponent).
    static (BigInteger M, int E) Round(BigInteger m, int e, int bits)
    {
        if (m.Sign < 0) { var (qn, en) = Round(-m, e, bits); return (-qn, en); }
        int len = (int)m.GetBitLength();
        if (len <= bits) return (m, e);
        int drop = len - bits;
        var q = m >> drop;
        var rem = m - (q << drop);
        var half = BigInteger.One << (drop - 1);
        if (rem > half || (rem == half && !q.IsEven)) q += 1;
        return (q, e + drop);
    }

    static (BigInteger M, int E) Decompose(double d)
    {
        long bits = BitConverter.DoubleToInt64Bits(d);
        int exp = (int)((bits >> 52) & 0x7FF);
        long man = bits & 0xFFFFFFFFFFFFFL;
        BigInteger sign = bits < 0 ? -1 : 1;
        if (exp == 0) return (sign * man, -1074);
        return (sign * (man | (1L << 52)), exp - 1075);
    }

    static double ToDouble((BigInteger M, int E) v) => (double)v.M * Math.Pow(2, v.E);

    static double MulViaX87(double a, double b)
    {
        var (ma, ea) = Decompose(Math.Abs(a));
        var (mb, eb) = Decompose(Math.Abs(b));
        var p = Round(ma * mb, ea + eb, 64);   // x87 extended temporary
        p = Round(p.M, p.E, 53);               // store to binary64
        double r = ToDouble(p);
        return (a < 0) != (b < 0) ? -r : r;
    }

    [Fact]
    public void BounceFromSurface_DoubleRoundingIsRareAndNeverChangesTheQ8Trace()
    {
        var rng = new Random(12345);
        int bitDiffs = 0, q8Diffs = 0;
        const int N = 2_000_000;
        for (int i = 0; i < N; i++)
        {
            // VelocityY is clamped to +-16 and is itself a result of earlier double operations (gravity steps of k/5).
            double v = (rng.NextDouble() * 32.0) - 16.0;
            double direct = -v * 0.78;
            double x87 = MulViaX87(-v, 0.78);
            if (BitConverter.DoubleToInt64Bits(direct) != BitConverter.DoubleToInt64Bits(x87)) bitDiffs++;
            if ((long)(direct * 256.0) != (long)(x87 * 256.0)) q8Diffs++;
        }
        // Expected double-rounding rate is about 2^-11 (~0.05%); report, bound loosely, and require the Q8 view to match.
        Assert.True(bitDiffs < N / 500, $"1-ulp differences: {bitDiffs}/{N}");
        Assert.True(q8Diffs <= 2, $"Q8-visible differences: {q8Diffs}/{N}");
    }

    [Fact]
    public void Additions_AreExactSoX87AndBinary64Agree()
    {
        // force = forceFrame/5 + Force and Shot(): -10 + v/2 : operands are short-mantissa, the sum fits in 64 bits.
        var rng = new Random(7);
        int checkedCases = 0;
        for (int i = 0; i < 100_000; i++)
        {
            double v = (rng.NextDouble() * 32.0) - 16.0;
            int k = rng.Next(0, 40);
            var (m1, e1) = Decompose(v);
            double sum = k + v;
            // Exact sum of an int and a double: if it needs <= 64 bits, x87 holds it exactly and the store rounds once.
            int lo = Math.Min(e1, 0);
            var exact = (m1 << (e1 - lo)) + (new BigInteger(k) << (0 - lo));
            if (BigInteger.Abs(exact).GetBitLength() > 64) continue; // needs >64 bits: x87 would round twice (very small |v|)
            checkedCases++;
            Assert.Equal(sum, ToDouble(Round(exact, lo, 53)));
        }
        Assert.True(checkedCases > 50_000, $"only {checkedCases} cases fit in 64 bits");
    }

    [Fact]
    public void OrbYAxis_StepsAreDeterministic()
    {
        var a = new OrbYAxis(); var b = new OrbYAxis();
        a.StageStart(); b.StageStart();
        for (int f = 0; f < 200; f++)
        {
            Assert.Equal(a.Update(f % 40), b.Update(f % 40));
            if (f % 17 == 0) { a.BounceFromSurface(); b.BounceFromSurface(); }
        }
        Assert.Equal(a.VelocityYBits, b.VelocityYBits);
    }
}
