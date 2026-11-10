using Th01.Audio.Mdrv;

namespace Th01.Audio.Synth;

/// <summary>Renders a decoded .MDT song or a .MDE effect to PCM, offline (no real-time playback yet).</summary>
public static class MdrvRenderer
{
    // Gains at the loudest channel volume; chosen so that the usual volumes of the songs keep the level they had before the
    // volume curves followed the driver (FM volume 0x7A, SSG volume 13).
    const double FmGain = 0.34, SsgGain = 0.12, DrumGain = 0.3;
    /// <summary>Effect gain: a full-volume square tone peaks at -14.5 dBFS, as in the recordings of the original's effects.</summary>
    const double EffectGain = 0.195;
    const int MaxEnvelopeTicks = 1200;   // an envelope that never dies out is cut after about 20 s

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
                    NoteSynth.Fm(buf, start, song.Voices[n.Voice], NoteSynth.Frequency(n.Semitone, n.Detune * 4.0), gate, FmGain * NoteSynth.FmVolumeGain(n.Volume), n.Pan);
                break;
            case ChannelKind.Ssg:
                RenderSsgNote(buf, start, gate, n, song.SsgPresets,
                    (song.TickToSeconds(n.StartTick + 1) - song.TickToSeconds(n.StartTick)) * NoteSynth.SampleRate, SsgGain, sweep: false);
                break;
            default:
                NoteSynth.Drum(buf, start, n.DrumId, DrumGain);
                break;
        }
    }

    // An SSG note: shaped by its envelope preset when it has one (the envelope runs once per tick and may ring on until the next
    // note of the channel starts, or until endTicks when the stream ends first), otherwise a plain gate at the note's volume.
    static void RenderSsgNote(StereoBuffer buf, int start, int gate, NoteEvent n, IReadOnlyList<SsgEnvelopePreset> presets,
                              double framesPerTick, double gain, bool sweep, int endTicks = 0)
    {
        double freq = NoteSynth.SsgFrequency(n.Semitone, n.Detune);
        int sweepDelta = sweep ? n.SweepDelta : 0;
        double secondsPerTick = framesPerTick / NoteSynth.SampleRate;
        if (n.EnvelopePreset >= 0 && n.EnvelopePreset < presets.Count)
        {
            int total = Math.Clamp(n.SpanTicks > 0 ? n.SpanTicks : n.DurationTicks + 96, 1, MaxEnvelopeTicks);
            if (endTicks > 0) total = Math.Clamp(Math.Min(total, endTicks), 1, MaxEnvelopeTicks);
            var levels = SsgEnvelope.Levels(presets[n.EnvelopePreset], n.DurationTicks, total);
            int last = levels.Length;
            while (last > 1 && NoteSynth.SsgVolumeGain(SsgEnvelope.OutputLevel(n.Volume, levels[last - 1])) == 0) last--;   // trailing silence
            var gains = new double[last];
            for (int t = 0; t < last; t++) gains[t] = NoteSynth.SsgVolumeGain(SsgEnvelope.OutputLevel(n.Volume, levels[t]));
            NoteSynth.Ssg(buf, start, freq, gate, gain, n.Tone, n.Noise, n.NoisePeriod, sweepDelta, secondsPerTick, n.Semitone, gains, framesPerTick);
        }
        else
            NoteSynth.Ssg(buf, start, freq, gate, gain * NoteSynth.SsgVolumeGain(n.Volume), n.Tone, n.Noise, n.NoisePeriod, sweepDelta, secondsPerTick, n.Semitone);
    }

    /// <summary>
    /// Seconds per tick of sound effects when no song has set a tempo: the start-up file (INIT.MDT) sets 0x41. The effects run on
    /// the driver's one timer, so once a song has set a tempo they tick at that song's speed (see the overload taking a tick length).
    /// </summary>
    public static readonly double EffectSecondsPerTick = TempoMap.SecondsPerTick(0x41);

    /// <summary>Renders one effect of a <see cref="SoundEffectBank"/> (all of its parts mixed) at the start-up tempo.</summary>
    public static StereoBuffer RenderEffect(SoundEffect effect) => RenderEffect(effect, EffectSecondsPerTick);

    /// <summary>
    /// Renders an effect with the given tick length. A part is silenced the moment its stream ends, whatever its envelope would
    /// still do: there is no release tail after the last command.
    /// </summary>
    public static StereoBuffer RenderEffect(SoundEffect effect, double secondsPerTick)
    {
        int ticks = effect.Parts.Count == 0 ? 0 : effect.Parts.Max(p => p.TotalTicks);
        var buf = new StereoBuffer((int)((ticks * secondsPerTick + 0.05) * NoteSynth.SampleRate));
        foreach (var part in effect.Parts)
        {
            foreach (var n in part.Notes)
            {
                int start = (int)(n.StartTick * secondsPerTick * NoteSynth.SampleRate);
                int gate = Math.Max(1, (int)(n.DurationTicks * secondsPerTick * NoteSynth.SampleRate));
                RenderSsgNote(buf, start, gate, n, effect.Presets, secondsPerTick * NoteSynth.SampleRate, EffectGain, sweep: true,
                    endTicks: Math.Max(1, part.TotalTicks - n.StartTick));
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
