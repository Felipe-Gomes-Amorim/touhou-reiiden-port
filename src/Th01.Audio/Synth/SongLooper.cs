using Th01.Audio.Mdrv;

namespace Th01.Audio.Synth;

/// <summary>
/// Rendered music ready to stream: interleaved 16-bit stereo. When <see cref="Loops"/> is set, playback runs
/// 0..<see cref="EndFrame"/> and then wraps to <see cref="LoopStartFrame"/> forever (the region in between is
/// rendered after one full pass of every track, so the tails of the previous pass are already in it).
/// </summary>
public sealed record LoopedPcm(short[] Pcm, int LoopStartFrame, int EndFrame, bool Loops, bool PeriodsMatch)
{
    public int Frames => Pcm.Length / 2;
}

/// <summary>
/// Unrolls the F3 loops of every track: a track plays from its start up to F3, then continues from its loop point,
/// independently of the other tracks (as the driver does). Needs equal loop lengths to stay in sync; that is checked
/// by <see cref="LoopedPcm.PeriodsMatch"/> (every loop length divides the longest one).
/// </summary>
public static class SongLooper
{
    public static LoopedPcm Render(MdrvSong song, double maxSeconds = 400)
    {
        var looping = song.Tracks.Where(t => t.Loops && t.LoopStartTick >= 0 && t.TotalTicks > t.LoopStartTick && t.Notes.Count > 0).ToList();
        if (looping.Count == 0)
        {
            var once = MdrvRenderer.RenderSong(song, maxSeconds);
            return new LoopedPcm(MdrvRenderer.ToPcm16(once), 0, once.Frames, false, true);
        }

        int period = looping.Max(t => t.TotalTicks - t.LoopStartTick);
        bool match = looping.All(t => period % (t.TotalTicks - t.LoopStartTick) == 0);
        int introEnd = looping.Max(t => t.LoopStartTick);
        int endTick = introEnd + period, loopTick = introEnd;
        double seconds = song.TickToSeconds(endTick);
        if (seconds > maxSeconds)
        {
            // Too long to render in full: play once, without looping.
            var once = MdrvRenderer.RenderSong(song, maxSeconds);
            return new LoopedPcm(MdrvRenderer.ToPcm16(once), 0, once.Frames, false, match);
        }

        // Render intro + one period plus a tail; the tail that runs past the end is added back onto the loop start,
        // which is what the notes of the previous pass leave there when the music wraps.
        var channels = song.Tracks.Select(t => (t.Kind, Unroll(t, endTick)));
        const double TailSeconds = 1.2;
        var buf = MdrvRenderer.RenderNotes(song, channels, seconds + TailSeconds);
        int end = (int)(seconds * NoteSynth.SampleRate), start = (int)(song.TickToSeconds(loopTick) * NoteSynth.SampleRate);
        int tail = Math.Min(buf.Frames - end, end - start);
        for (int i = 0; i < tail; i++) { buf.Left[start + i] += buf.Left[end + i]; buf.Right[start + i] += buf.Right[end + i]; }
        var pcm = MdrvRenderer.ToPcm16(buf);
        return new LoopedPcm(pcm[..(end * 2)], start, end, true, match);
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
