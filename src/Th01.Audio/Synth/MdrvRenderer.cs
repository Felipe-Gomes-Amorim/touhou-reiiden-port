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
        return RenderNotes(song, song.Tracks.Select(t => (t.Kind, (IEnumerable<NoteEvent>)t.Notes)), total);
    }

    const int ChunkFrames = 10 * NoteSynth.SampleRate;

    readonly record struct Placed(ChannelKind Kind, NoteEvent Note, int Start, int Gate);

    /// <summary>
    /// Mixes already-placed notes (tick positions relative to the song) into a buffer of <paramref name="seconds"/>.
    /// Notes are independent, so ten-second chunks are rendered in parallel and added up.
    /// </summary>
    public static StereoBuffer RenderNotes(MdrvSong song, IEnumerable<(ChannelKind Kind, IEnumerable<NoteEvent> Notes)> channels, double seconds)
    {
        var buf = new StereoBuffer((int)(seconds * NoteSynth.SampleRate));
        var chunks = new Dictionary<int, List<Placed>>();
        foreach (var (kind, notes) in channels)
        {
            foreach (var n in notes)
            {
                int start = (int)(song.TickToSeconds(n.StartTick) * NoteSynth.SampleRate);
                if (start >= buf.Frames) break;
                int gate = Math.Max(1, (int)((song.TickToSeconds(n.StartTick + n.DurationTicks) - song.TickToSeconds(n.StartTick)) * NoteSynth.SampleRate));
                int chunk = start / ChunkFrames;
                if (!chunks.TryGetValue(chunk, out var list)) chunks[chunk] = list = [];
                list.Add(new Placed(kind, n, start, gate));
            }
        }
        var merge = new object();
        Parallel.ForEach(chunks, kv =>
        {
            int origin = kv.Key * ChunkFrames;
            int length = kv.Value.Max(p => p.Start - origin + p.Gate) + NoteSynth.SampleRate + 1;
            var local = new StereoBuffer(length);
            foreach (var p in kv.Value) RenderNote(song, local, p.Kind, p.Note, p.Start - origin, p.Gate);
            lock (merge)
            {
                int count = Math.Min(length, buf.Frames - origin);
                for (int i = 0; i < count; i++) { buf.Left[origin + i] += local.Left[i]; buf.Right[origin + i] += local.Right[i]; }
            }
        });
        return buf;
    }

    static void RenderNote(MdrvSong song, StereoBuffer buf, ChannelKind kind, NoteEvent n, int start, int gate)
    {
        switch (kind)
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
