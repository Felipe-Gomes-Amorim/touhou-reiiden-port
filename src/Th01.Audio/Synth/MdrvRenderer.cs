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

    /// <summary>Length of the slices a song is rendered in (and, for streaming, the granularity of "ready" audio).</summary>
    public const int ChunkFrames = 2 * NoteSynth.SampleRate;

    internal readonly record struct Placed(int Channel, ChannelKind Kind, NoteEvent Note, int Start, int Gate);

    /// <summary>Puts every note at its start frame and groups them by chunk of <see cref="ChunkFrames"/>; notes past <paramref name="frames"/> are dropped.</summary>
    internal static List<Placed>[] Place(MdrvSong song, IEnumerable<(ChannelKind Kind, IEnumerable<NoteEvent> Notes)> channels, int frames)
    {
        var chunks = new List<Placed>[frames / ChunkFrames + 1];
        for (int i = 0; i < chunks.Length; i++) chunks[i] = [];
        int channel = 0;
        foreach (var (kind, notes) in channels)
        {
            foreach (var n in notes)
            {
                int start = (int)(song.TickToSeconds(n.StartTick) * NoteSynth.SampleRate);
                if (start >= frames) break;
                int gate = Math.Max(1, (int)((song.TickToSeconds(n.StartTick + n.DurationTicks) - song.TickToSeconds(n.StartTick)) * NoteSynth.SampleRate));
                chunks[start / ChunkFrames].Add(new Placed(channel, kind, n, start, gate));
            }
            channel++;
        }
        return chunks;
    }

    /// <summary>Adds the notes that start in chunk <paramref name="chunk"/> (with their release tails) to <paramref name="target"/>; channels are rendered in parallel.</summary>
    internal static void RenderChunk(MdrvSong song, List<Placed> notes, int chunk, StereoBuffer target)
    {
        int origin = chunk * ChunkFrames;
        var merge = new object();
        Parallel.ForEach(notes.GroupBy(p => p.Channel), group =>
        {
            int length = group.Max(p => p.Start - origin + p.Gate) + NoteSynth.SampleRate + 1;
            var local = new StereoBuffer(length);
            foreach (var p in group) RenderNote(song, local, p.Kind, p.Note, p.Start - origin, p.Gate);
            lock (merge)
            {
                int count = Math.Min(length, target.Frames - origin);
                for (int i = 0; i < count; i++) { target.Left[origin + i] += local.Left[i]; target.Right[origin + i] += local.Right[i]; }
            }
        });
    }

    /// <summary>Mixes already-placed notes (tick positions relative to the song) into a buffer of <paramref name="seconds"/>.</summary>
    public static StereoBuffer RenderNotes(MdrvSong song, IEnumerable<(ChannelKind Kind, IEnumerable<NoteEvent> Notes)> channels, double seconds)
    {
        var buf = new StereoBuffer((int)(seconds * NoteSynth.SampleRate));
        var chunks = Place(song, channels, buf.Frames);
        for (int c = 0; c < chunks.Length; c++)
            if (chunks[c].Count > 0) RenderChunk(song, chunks[c], c, buf);
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

    /// <summary>
    /// Seconds per tick of sound effects. ASSUMPTION: the effects run on the driver's one timer, which INIT.MDT (loaded at
    /// start-up, the only thing in it is a tempo command) sets to 0x41; so they tick like a song of tempo 0x41.
    /// </summary>
    public static readonly double EffectSecondsPerTick = 60.0 / (0x41 * MdrvSong.TempoScale * 24.0);

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

    internal static short Clip(double x) => (short)(Math.Tanh(x) * 32000);
}
