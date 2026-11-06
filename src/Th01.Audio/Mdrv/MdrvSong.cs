using Th01.Assets.Formats;
using Th01.Assets.Text;

namespace Th01.Audio.Mdrv;

/// <summary>A .MDT music file decoded into per-channel note lists plus its FM voices.</summary>
public sealed class MdrvSong
{
    public IReadOnlyList<DecodedTrack> Tracks { get; }
    public IReadOnlyList<FmVoice> Voices { get; }
    public IReadOnlyList<TempoChange> Tempos { get; }
    public string Comment { get; }

    MdrvSong(List<DecodedTrack> tracks, List<FmVoice> voices, List<TempoChange> tempos, string comment)
    {
        Tracks = tracks; Voices = voices; Tempos = tempos; Comment = comment;
    }

    public static ChannelKind KindOf(int trackId) => trackId switch
    {
        0x42 => ChannelKind.Drum,
        0x40 or 0x41 => ChannelKind.Ssg,
        _ => ChannelKind.Fm,
    };

    public static MdrvSong Load(byte[] mdt)
    {
        var file = MdtFile.Parse(mdt);
        var tracks = new List<DecodedTrack>();
        foreach (var t in file.Tracks)
        {
            if (t.Id == MdtFile.EndMarkerId) continue;
            tracks.Add(TrackDecoder.Decode(mdt, t.Offset, t.Length, t.Id, KindOf(t.Id)));
        }
        var voices = new List<FmVoice>();
        for (int i = 0; i < file.VoiceCount; i++)
            voices.Add(FmVoice.Parse(mdt.AsSpan(file.VoiceTableOffset + i * FmVoice.Size, FmVoice.Size)));
        var tempos = tracks.Select(t => t.Tempos).FirstOrDefault(t => t.Count > 0)?.ToList() ?? [];
        // The header text is Shift-JIS (the music titles are Japanese); invalid bytes fall back to a lossy Latin-1 view.
        string raw = TextCodec.TryDecode(file.Text.Span, out string sjis) ? sjis : System.Text.Encoding.Latin1.GetString(file.Text.Span);
        string comment = raw.Replace('\r', ' ').Replace('\n', ' ').Trim();
        return new MdrvSong(tracks, voices, tempos, comment);
    }

    public int TotalTicks => Tracks.Count == 0 ? 0 : Tracks.Max(t => t.TotalTicks);

    /// <summary>Converts a tick position to seconds using the tempo changes of the song (default tempo 0x48).</summary>
    public double TickToSeconds(int tick) => TempoMap.ToSeconds(Tempos, tick);
}

public static class TempoMap
{
    public const int DefaultTempo = 0x48;

    /// <summary>Length of one step of the sound chip's second timer (1152 chip clocks at 3.9936 MHz), which paces the driver.</summary>
    public const double TimerStepSeconds = 1152 / 3_993_600.0;

    /// <summary>
    /// Seconds per tick for a tempo byte. The tempo command programs the chip timer to count floor(4340 / tempo) steps per tick
    /// (255 steps for tempo values below 17), and every timer interrupt advances all channels by one tick. With 48 ticks to a
    /// quarter note this is about 1.25 / tempo seconds, so the tempo byte is quarter notes per minute.
    /// </summary>
    public static double SecondsPerTick(int tempo) => (tempo < 0x11 ? 255 : 4340 / tempo) * TimerStepSeconds;

    public static double ToSeconds(IReadOnlyList<TempoChange> tempos, int tick)
    {
        double seconds = 0;
        int at = 0, tempo = DefaultTempo;
        foreach (var c in tempos)
        {
            if (c.Tick >= tick) break;
            seconds += (c.Tick - at) * SecondsPerTick(tempo);
            at = c.Tick; tempo = c.Tempo;
        }
        return seconds + (tick - at) * SecondsPerTick(tempo);
    }
}
