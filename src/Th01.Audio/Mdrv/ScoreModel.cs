namespace Th01.Audio.Mdrv;

public enum ChannelKind { Fm, Ssg, Drum }

/// <summary>
/// One note of a decoded track. Times are in ticks (96 per whole note, 24 per quarter).
/// Semitone = octave * 12 + note, where the original byte is 0xON (octave, note 0..B).
/// </summary>
public sealed record NoteEvent(
    int StartTick, int DurationTicks, int Semitone, int Volume, int Voice, int Detune,
    bool Tone = true, bool Noise = false, int NoisePeriod = 0, int SweepDelta = 0, int DrumId = 0);

/// <summary>Tempo byte (command E9) that takes effect at a tick.</summary>
public readonly record struct TempoChange(int Tick, int Tempo);

public sealed class DecodedTrack
{
    public required int TrackId { get; init; }
    public required ChannelKind Kind { get; init; }
    public required IReadOnlyList<NoteEvent> Notes { get; init; }
    public required IReadOnlyList<TempoChange> Tempos { get; init; }
    /// <summary>Length of the track after expanding repeats, up to the end/loop command (F3 or FF).</summary>
    public required int TotalTicks { get; init; }
    /// <summary>True when the track ended with F3 (jump back = the music loops), false for FF.</summary>
    public required bool Loops { get; init; }
    /// <summary>Commands that this decoder skipped without knowing their meaning (opcode -> count).</summary>
    public required IReadOnlyDictionary<int, int> Ignored { get; init; }
}
