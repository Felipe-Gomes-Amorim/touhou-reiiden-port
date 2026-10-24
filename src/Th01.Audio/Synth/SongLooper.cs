using Th01.Audio.Mdrv;

namespace Th01.Audio.Synth;

/// <summary>
/// Rendered music ready to stream: interleaved 16-bit stereo. When <see cref="Loops"/> is set, playback runs
/// 0..<see cref="EndFrame"/> and then wraps to <see cref="LoopStartFrame"/> forever (the loop region has the tails of the
/// previous pass folded in). While a song is still being rendered in the background only the first
/// <see cref="ReadyFrames"/> frames may be played.
/// </summary>
public sealed class LoopedPcm
{
    int _ready;

    public short[] Pcm { get; }
    public int LoopStartFrame { get; }
    public int EndFrame { get; }
    public bool Loops { get; }
    /// <summary>False when some loop length does not divide the longest one: those channels drift against the rest.</summary>
    public bool PeriodsMatch { get; }
    public int Frames => Pcm.Length / 2;
    public int ReadyFrames => Volatile.Read(ref _ready);
    public bool Complete => ReadyFrames >= EndFrame;

    /// <param name="ready">Frames already rendered; defaults to all of them.</param>
    public LoopedPcm(short[] pcm, int loopStartFrame, int endFrame, bool loops, bool periodsMatch, int? ready = null)
    {
        Pcm = pcm; LoopStartFrame = loopStartFrame; EndFrame = endFrame; Loops = loops; PeriodsMatch = periodsMatch;
        _ready = ready ?? endFrame;
    }

    internal void SetReady(int frames) => Volatile.Write(ref _ready, frames);

    /// <summary>Blocks until the background render has finished (or <paramref name="timeoutMs"/> passed).</summary>
    public bool WaitComplete(int timeoutMs = Timeout.Infinite)
    {
        var sw = System.Diagnostics.Stopwatch.StartNew();
        while (!Complete)
        {
            if (timeoutMs != Timeout.Infinite && sw.ElapsedMilliseconds > timeoutMs) return false;
            Thread.Sleep(2);
        }
        return true;
    }
}

/// <summary>
/// Unrolls the F3 loops of every track: a track plays from its start up to F3, then continues from its loop point,
/// independently of the other tracks (as the driver does). Needs equal loop lengths to stay in sync; that is checked
/// by <see cref="LoopedPcm.PeriodsMatch"/> (every loop length divides the longest one).
/// The render runs in the background in time order, so playback can begin as soon as the first chunk is done.
/// </summary>
public static class SongLooper
{
    const double TailSeconds = 1.2;

    /// <summary>Renders the whole song and waits for it.</summary>
    public static LoopedPcm Render(MdrvSong song, double maxSeconds = 400)
    {
        var pcm = Start(song, default, maxSeconds);
        pcm.WaitComplete();
        return pcm;
    }

    /// <summary>
    /// Returns at once with a <see cref="LoopedPcm"/> whose <see cref="LoopedPcm.ReadyFrames"/> grows while a background
    /// task renders it. Cancelling stops the task (the result then never completes).
    /// </summary>
    public static LoopedPcm Start(MdrvSong song, CancellationToken cancel = default, double maxSeconds = 400)
    {
        var looping = song.Tracks.Where(t => t.Loops && t.LoopStartTick >= 0 && t.TotalTicks > t.LoopStartTick && t.Notes.Count > 0).ToList();
        int endTick, loopTick = 0;
        bool loops = looping.Count > 0, match = true;
        if (loops)
        {
            int period = looping.Max(t => t.TotalTicks - t.LoopStartTick);
            match = looping.All(t => period % (t.TotalTicks - t.LoopStartTick) == 0);
            loopTick = looping.Max(t => t.LoopStartTick);
            endTick = loopTick + period;
            if (song.TickToSeconds(endTick) > maxSeconds) { loops = false; endTick = song.TotalTicks; }   // too long: play once
        }
        else endTick = song.TotalTicks;

        double seconds = song.TickToSeconds(endTick) + (loops ? 0 : 2.0);
        seconds = Math.Min(seconds, maxSeconds);
        int end = (int)(seconds * NoteSynth.SampleRate);
        int start = loops ? (int)(song.TickToSeconds(loopTick) * NoteSynth.SampleRate) : 0;
        var result = new LoopedPcm(new short[end * 2], start, end, loops, match, ready: 0);

        var channels = song.Tracks.Select(t => (t.Kind, loops ? Unroll(t, endTick) : t.Notes)).ToList();
        Task.Run(() => Fill(song, channels, result, cancel));
        return result;
    }

    static void Fill(MdrvSong song, List<(ChannelKind Kind, IEnumerable<NoteEvent> Notes)> channels, LoopedPcm result, CancellationToken cancel)
    {
        int end = result.EndFrame, chunk = MdrvRenderer.ChunkFrames;
        // Render intro + one period plus a tail; what runs past the end is added back onto the loop start,
        // which is what the notes of the previous pass leave there when the music wraps.
        int tail = result.Loops ? (int)(TailSeconds * NoteSynth.SampleRate) : 0;
        int total = end + tail;
        var acc = new StereoBuffer(total);
        var placed = MdrvRenderer.Place(song, channels, total);
        int converted = 0;
        for (int c = 0; c < placed.Length; c++)
        {
            if (cancel.IsCancellationRequested) return;
            if (placed[c].Count > 0) MdrvRenderer.RenderChunk(song, placed[c], c, acc);
            // Everything before the start of the next chunk can no longer change.
            int final = Math.Min(end, (c + 1) * chunk);
            Convert(acc, result.Pcm, converted, final);
            converted = Math.Max(converted, final);
            if (final < end) result.SetReady(final);
        }
        if (result.Loops)
        {
            int n = Math.Min(tail, end - result.LoopStartFrame);
            for (int i = 0; i < n; i++)
            {
                acc.Left[result.LoopStartFrame + i] += acc.Left[end + i];
                acc.Right[result.LoopStartFrame + i] += acc.Right[end + i];
            }
            Convert(acc, result.Pcm, result.LoopStartFrame, result.LoopStartFrame + n);
        }
        Convert(acc, result.Pcm, converted, end);
        result.SetReady(end);
    }

    static void Convert(StereoBuffer acc, short[] pcm, int from, int to)
    {
        for (int i = from; i < to; i++)
        {
            pcm[i * 2] = MdrvRenderer.Clip(acc.Left[i]);
            pcm[i * 2 + 1] = MdrvRenderer.Clip(acc.Right[i]);
        }
    }

    /// <summary>The notes of a track repeated through its loop until <paramref name="endTick"/>.</summary>
    public static IEnumerable<NoteEvent> Unroll(DecodedTrack t, int endTick)
    {
        foreach (var n in t.Notes) yield return n;
        if (!t.Loops || t.LoopStartTick < 0 || t.TotalTicks <= t.LoopStartTick) yield break;
        int len = t.TotalTicks - t.LoopStartTick;
        var body = t.Notes.Where(n => n.StartTick >= t.LoopStartTick).ToList();
        if (body.Count == 0) yield break;
        for (int k = 1; t.TotalTicks + (k - 1) * len < endTick; k++)
            foreach (var n in body)
                yield return n with { StartTick = n.StartTick + k * len };
    }
}
