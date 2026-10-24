using Th01.Assets.Formats;

namespace Th01.Audio.Mdrv;

/// <summary>A .MDT music file decoded into per-channel note lists plus its FM voices.</summary>
public sealed class MdrvSong
{
    /// <summary>
    /// Seconds per tick = 60 / (tempo byte * 48): the tempo byte is quarter notes per minute and a quarter note is 48 ticks
    /// (the factor 2 * 24 below). ASSUMPTION: the exact timer formula of the driver is not known; this one gives believable
    /// song lengths (1-3 minutes). TODO(T5): calibrate vs emulator.
    /// </summary>
    public const double TempoScale = 2.0;

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
        string comment = System.Text.Encoding.ASCII.GetString(file.Text.Span).Replace('\r', ' ').Replace('\n', ' ').Trim();
        return new MdrvSong(tracks, voices, tempos, comment);
    }

    public int TotalTicks => Tracks.Count == 0 ? 0 : Tracks.Max(t => t.TotalTicks);

    /// <summary>Converts a tick position to seconds using the tempo changes of the song (default tempo 0x48).</summary>
    public double TickToSeconds(int tick) => TempoMap.ToSeconds(Tempos, tick, TempoScale);
}

public static class TempoMap
{
    public const int DefaultTempo = 0x48;

    public static double ToSeconds(IReadOnlyList<TempoChange> tempos, int tick, double scale)
    {
        double seconds = 0;
        int at = 0, tempo = DefaultTempo;
        foreach (var c in tempos)
        {
            if (c.Tick >= tick) break;
            seconds += (c.Tick - at) * Spt(tempo, scale);
            at = c.Tick; tempo = c.Tempo;
        }
        return seconds + (tick - at) * Spt(tempo, scale);
    }

    static double Spt(int tempo, double scale) => 60.0 / (Math.Max(1, tempo) * scale * 24.0);
}
