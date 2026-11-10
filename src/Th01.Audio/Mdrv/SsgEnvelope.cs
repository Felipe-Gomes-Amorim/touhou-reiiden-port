namespace Th01.Audio.Mdrv;

/// <summary>
/// One software envelope preset of a song (6 bytes in the file): the level a note starts at, the attack rate, the decay rate
/// and the level it decays to (sustain level), the rate at which the sustain fades away, and the release rate. Rates are
/// level units (0..255) taken off or put on per tick.
/// </summary>
public readonly record struct SsgEnvelopePreset(byte Initial, byte Attack, byte Decay, byte Sustain, byte SustainRate, byte Release)
{
    public const int Size = 6;

    public static SsgEnvelopePreset Parse(ReadOnlySpan<byte> b) => new(b[0], b[1], b[2], b[3], b[4], b[5]);
}

/// <summary>
/// The SSG channels have no hardware envelope in this driver: each note runs a small state machine once per tick that moves a
/// level (0..255) towards a target, in up to three stages (attack to full level, decay to the sustain level, fade of the
/// sustain to silence), and a fourth stage once the key is released. The audible SSG level is then (volume + 1) * level / 256.
/// The state machine below follows the behaviour observed in the driver; the preset values come from the song file.
/// A stage ends when its level reaches or passes its target; a rate of 0 never moves the level, so such a stage only ends if the level
/// already is at (or past) the target, and a release rate of 0 holds the note at its level until the next note on the channel starts.
/// The note is over (level 0) when the sustain fade or the release reaches its target.
/// </summary>
public static class SsgEnvelope
{
    /// <summary>
    /// Level (0..255) at the end of every tick of a note, for <paramref name="totalTicks"/> ticks, with the key released at tick
    /// <paramref name="gateTicks"/> (the release stage then takes over from whatever stage was running).
    /// </summary>
    public static byte[] Levels(SsgEnvelopePreset p, int gateTicks, int totalTicks)
    {
        var levels = new byte[Math.Max(0, totalTicks)];
        // Per stage: step size, target level. Every step happens once per tick (the step interval of every stage in the files is one tick).
        (int Rate, int Target)[] stages = [(p.Attack, 255), (p.Decay, p.Sustain), (p.SustainRate, 0), (p.Release, 0)];
        int stage = 0, level = p.Initial;
        bool running = true;
        for (int t = 0; t < levels.Length; t++)
        {
            if (t == gateTicks) { stage = 3; running = true; }   // key off: release
            if (running)
            {
                var (rate, target) = stages[stage];
                bool up = stage == 0;
                int next = up ? level + rate : level - rate;
                bool reached = up ? next > 255 || next >= target : next < 0 || next <= target;
                if (reached)
                {
                    level = target;
                    if (level == 0 || stage >= 2) running = false;   // silent, or the last stage of the note is done
                    else stage++;
                }
                else level = next;
            }
            levels[t] = (byte)level;
        }
        return levels;
    }

    /// <summary>The 0..15 SSG output level for a channel volume and an envelope level.</summary>
    public static int OutputLevel(int volume, int envelopeLevel) => Math.Min(15, (Math.Max(0, volume) + 1) * envelopeLevel >> 8);
}
