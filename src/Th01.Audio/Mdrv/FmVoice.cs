namespace Th01.Audio.Mdrv;

/// <summary>One FM operator, with the raw register-style bytes of the voice record.</summary>
public readonly record struct FmOperator(int DetuneMultiple, int TotalLevel, int KeyScaleAttack, int AmDecay, int SustainRate, int SustainReleaseLevel)
{
    public int Multiple => DetuneMultiple & 0x0F;
    public int Detune => (DetuneMultiple >> 4) & 0x07;
    public int Attack => KeyScaleAttack & 0x1F;
    public int Decay => AmDecay & 0x1F;
    public int Sustain => SustainRate & 0x1F;
    public int SustainLevel => SustainReleaseLevel >> 4;
    public int Release => SustainReleaseLevel & 0x0F;
}

/// <summary>
/// A 32-byte voice record of an MDT. Layout as observed (docs of the original driver were not consulted):
/// [3] 0x78 (unknown), [4] pan(bits 7-6) | feedback(5-3) | algorithm(2-0), [6..9] DT/MUL, [10..13] TL, [14..17] KS/AR,
/// [18..21] AM/DR, [22..25] SR, [26..29] SL/RR; the other bytes are 0 or leftovers.
/// ASSUMPTION: operators are stored in register order (slot 1, 3, 2, 4) like the YM2608 registers 0x30/0x34/0x38/0x3C.
/// </summary>
public sealed class FmVoice
{
    public const int Size = 32;

    public int Algorithm { get; }
    public int Feedback { get; }
    public bool Left { get; }
    public bool Right { get; }
    /// <summary>Operators in slot order S1, S2, S3, S4 (the signal-flow order of the algorithm diagrams).</summary>
    public IReadOnlyList<FmOperator> Operators { get; }
    public bool IsEmpty { get; }

    FmVoice(int algorithm, int feedback, bool left, bool right, FmOperator[] ops, bool empty)
    {
        Algorithm = algorithm; Feedback = feedback; Left = left; Right = right; Operators = ops; IsEmpty = empty;
    }

    public static FmVoice Parse(ReadOnlySpan<byte> rec)
    {
        if (rec.Length < Size) throw new ArgumentException("A voice record has 32 bytes.", nameof(rec));
        byte[] r = rec[..Size].ToArray();
        bool empty = r[4] == 0 && r[6] == 0 && r[10] == 0;
        FmOperator Stored(int i) => new(r[6 + i], r[10 + i], r[14 + i], r[18 + i], r[22 + i], r[26 + i]);
        // stored order S1, S3, S2, S4 -> slot order S1, S2, S3, S4
        var ops = new[] { Stored(0), Stored(2), Stored(1), Stored(3) };
        int b = r[4];
        bool pan = (b & 0xC0) != 0;
        return new FmVoice(b & 7, (b >> 3) & 7, !pan || (b & 0x80) != 0, !pan || (b & 0x40) != 0, ops, empty);
    }
}
