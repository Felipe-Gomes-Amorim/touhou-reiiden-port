namespace Th01.Assets.Formats;

/// <summary>One track (MDT) or effect (MDE) of an MDRV2 file: only its position, the commands are not decoded.</summary>
public sealed record MdrvTrack(int Id, int Offset, int Length);

/// <summary>
/// Structure of a .MDT music file as far as it is observable (docs/AUDIO.md): a header, a table of tracks, a
/// comment text, the tracks, a voice table and a trailer. Command bytes inside tracks are NOT interpreted.
/// </summary>
public sealed class MdtFile
{
    public const int TableStart = 4, VoiceFieldsOffset = 0x3A, TextStart = 0x40, VoiceSize = 32, EndMarkerId = 0x10;

    public int Magic { get; }
    public IReadOnlyList<MdrvTrack> Tracks { get; }
    /// <summary>Raw bytes between 0x40 and the first track (Shift-JIS comment, usually ending in '$').</summary>
    public ReadOnlyMemory<byte> Text { get; }
    public int VoiceTableOffset { get; }
    public int VoiceCount { get; }
    public ReadOnlyMemory<byte> Trailer { get; }

    private MdtFile(int magic, List<MdrvTrack> tracks, ReadOnlyMemory<byte> text, int voiceOffset, int voiceCount, ReadOnlyMemory<byte> trailer)
    {
        Magic = magic; Tracks = tracks; Text = text; VoiceTableOffset = voiceOffset; VoiceCount = voiceCount; Trailer = trailer;
    }

    public static MdtFile Parse(ReadOnlyMemory<byte> data)
    {
        var d = data.Span;
        if (d.Length < TextStart) throw new InvalidDataException("MDT is shorter than its header.");
        int magic = BitConverter.ToUInt16(d[..2]);
        var entries = new List<(int Id, int Offset)>();
        int p = TableStart;
        while (true)
        {
            if (p + 4 > VoiceFieldsOffset) throw new InvalidDataException("MDT track table has no end marker.");
            int id = BitConverter.ToUInt16(d.Slice(p, 2)), off = BitConverter.ToUInt16(d.Slice(p + 2, 2));
            p += 4;
            entries.Add((id, off));
            if (id == EndMarkerId) break;
        }
        int voiceStart = BitConverter.ToUInt16(d.Slice(VoiceFieldsOffset, 2)), voiceEnd = BitConverter.ToUInt16(d.Slice(VoiceFieldsOffset + 2, 2));
        if (voiceEnd < voiceStart || voiceEnd > d.Length || (voiceEnd - voiceStart) % VoiceSize != 0)
            throw new InvalidDataException("MDT voice table range is invalid.");
        var tracks = new List<MdrvTrack>();
        for (int i = 0; i < entries.Count; i++)
        {
            int start = entries[i].Offset, end = i + 1 < entries.Count ? entries[i + 1].Offset : voiceStart;
            if (start < TextStart || end < start || end > d.Length) throw new InvalidDataException($"MDT track {entries[i].Id:X2} points outside the file.");
            tracks.Add(new MdrvTrack(entries[i].Id, start, end - start));
        }
        int firstTrack = tracks[0].Offset;
        return new MdtFile(magic, tracks, data[TextStart..firstTrack], voiceStart, (voiceEnd - voiceStart) / VoiceSize, data[voiceEnd..]);
    }
}

/// <summary>Structure of a .MDE sound-effect file: a table of effects; each starts with pointers, then commands ending in 0xFF.</summary>
public sealed class MdeFile
{
    public const int TableStart = 0x10;

    public int Version { get; }
    /// <summary>Entry kind per effect (0x4001 = one part, 0x4002 = two parts; hypothesis: low byte = number of parts).</summary>
    public IReadOnlyList<int> Kinds { get; }
    public IReadOnlyList<MdrvTrack> Effects { get; }
    public ReadOnlyMemory<byte> Trailer { get; }

    private MdeFile(int version, List<int> kinds, List<MdrvTrack> effects, ReadOnlyMemory<byte> trailer)
    {
        Version = version; Kinds = kinds; Effects = effects; Trailer = trailer;
    }

    public static MdeFile Parse(ReadOnlyMemory<byte> data)
    {
        var d = data.Span;
        if (d.Length < TableStart) throw new InvalidDataException("MDE is shorter than its header.");
        int version = BitConverter.ToUInt16(d[..2]), count = BitConverter.ToUInt16(d.Slice(2, 2)), end = BitConverter.ToUInt16(d.Slice(4, 2));
        if (count == 0 || TableStart + count * 4 > d.Length || end > d.Length) throw new InvalidDataException("MDE header is inconsistent.");
        var kinds = new List<int>();
        var offs = new List<int>();
        for (int i = 0; i < count; i++)
        {
            kinds.Add(BitConverter.ToUInt16(d.Slice(TableStart + i * 4, 2)));
            offs.Add(BitConverter.ToUInt16(d.Slice(TableStart + i * 4 + 2, 2)));
        }
        var effects = new List<MdrvTrack>();
        for (int i = 0; i < count; i++)
        {
            int next = i + 1 < count ? offs[i + 1] : end;
            if (offs[i] < TableStart + count * 4 || next < offs[i] || next > d.Length) throw new InvalidDataException($"MDE effect {i} points outside the file.");
            effects.Add(new MdrvTrack(i, offs[i], next - offs[i]));
        }
        return new MdeFile(version, kinds, effects, data[end..]);
    }
}
