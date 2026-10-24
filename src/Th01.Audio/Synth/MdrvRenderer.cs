using Th01.Audio.Mdrv;

namespace Th01.Audio.Synth;

/// <summary>Renders a decoded .MDT song or a .MDE effect to PCM, offline (no real-time playback yet).</summary>
public static class MdrvRenderer
{
    const double FmGain = 0.22, SsgGain = 0.07, DrumGain = 0.3;

    /// <summary>Plays the tracks once from the start (loops are not followed) for at most <paramref name="maxSeconds"/>.</summary>
    public static StereoBuffer RenderSong(MdrvSong song, double maxSeconds = 600)
    {
        double total = Math.Min(maxSeconds, song.TickToSeconds(song.TotalTicks) + 2.5);
        var buf = new StereoBuffer((int)(total * NoteSynth.SampleRate));
        foreach (var track in song.Tracks)
        {
            foreach (var n in track.Notes)
            {
                int start = (int)(song.TickToSeconds(n.StartTick) * NoteSynth.SampleRate);
                if (start >= buf.Frames) break;
                int gate = Math.Max(1, (int)((song.TickToSeconds(n.StartTick + n.DurationTicks) - song.TickToSeconds(n.StartTick)) * NoteSynth.SampleRate));
                switch (track.Kind)
                {
                    case ChannelKind.Fm:
                        if (n.Voice < song.Voices.Count && !song.Voices[n.Voice].IsEmpty)
                            NoteSynth.Fm(buf, start, song.Voices[n.Voice], NoteSynth.Frequency(n.Semitone, n.Detune * 4.0), gate, FmGain * n.Volume / 127.0);
                        break;
                    case ChannelKind.Ssg:
                        NoteSynth.Ssg(buf, start, NoteSynth.Frequency(n.Semitone, n.Detune * 4.0), gate, SsgGain * Math.Min(n.Volume, 15) / 15.0, n.Tone, n.Noise, n.NoisePeriod);
                        break;
                    default:
                        NoteSynth.Drum(buf, start, n.DrumId, DrumGain);
                        break;
                }
            }
        }
        return buf;
    }

    /// <summary>Seconds per tick of sound effects. ASSUMPTION: 1/48 s (no tempo command in the .MDE files).</summary>
    public const double EffectSecondsPerTick = 1.0 / 48;

    /// <summary>Renders one effect of a <see cref="SoundEffectBank"/> (all of its parts mixed).</summary>
    public static StereoBuffer RenderEffect(SoundEffect effect)
    {
        double seconds = effect.Parts.Count == 0 ? 0 : effect.Parts.Max(p => p.TotalTicks) * EffectSecondsPerTick;
        var buf = new StereoBuffer((int)((seconds + 0.6) * NoteSynth.SampleRate));
        foreach (var part in effect.Parts)
        {
            foreach (var n in part.Notes)
            {
                int start = (int)(n.StartTick * EffectSecondsPerTick * NoteSynth.SampleRate);
                int gate = Math.Max(1, (int)(n.DurationTicks * EffectSecondsPerTick * NoteSynth.SampleRate));
                NoteSynth.Ssg(buf, start, NoteSynth.Frequency(n.Semitone, n.Detune * 4.0), gate, 0.25 * Math.Min(n.Volume, 15) / 15.0,
                    n.Tone, n.Noise, n.NoisePeriod, n.SweepDelta, EffectSecondsPerTick, n.Semitone);
            }
        }
        return buf;
    }

    /// <summary>Converts to interleaved 16-bit PCM with a soft clip.</summary>
    public static short[] ToPcm16(StereoBuffer buf, double gain = 1.0)
    {
        var pcm = new short[buf.Frames * 2];
        for (int i = 0; i < buf.Frames; i++)
        {
            pcm[i * 2] = Clip(buf.Left[i] * gain);
            pcm[i * 2 + 1] = Clip(buf.Right[i] * gain);
        }
        return pcm;
    }

    static short Clip(double x) => (short)(Math.Tanh(x) * 32000);
}
