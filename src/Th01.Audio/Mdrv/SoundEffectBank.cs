using Th01.Assets.Formats;

namespace Th01.Audio.Mdrv;

/// <summary>One sound effect: one stream per part (kind 0x4001 = 1 part, 0x4002 = 2 parts).</summary>
public sealed record SoundEffect(int Index, IReadOnlyList<DecodedTrack> Parts);

/// <summary>
/// A .MDE file decoded. Each effect starts with one little-endian pointer per part (absolute file offsets);
/// every part is a command stream like a music track (ending in FF) played as SSG-like tone/noise.
/// </summary>
public sealed class SoundEffectBank
{
    public IReadOnlyList<SoundEffect> Effects { get; }

    SoundEffectBank(List<SoundEffect> effects) { Effects = effects; }

    public static SoundEffectBank Load(byte[] mde)
    {
        var file = MdeFile.Parse(mde);
        var list = new List<SoundEffect>();
        for (int i = 0; i < file.Effects.Count; i++)
        {
            var e = file.Effects[i];
            int parts = Math.Clamp(file.Kinds[i] & 0xFF, 1, 4);
            var streams = new List<DecodedTrack>();
            for (int p = 0; p < parts; p++)
            {
                int ptrAt = e.Offset + p * 2;
                if (ptrAt + 2 > mde.Length) break;
                int start = BitConverter.ToUInt16(mde, ptrAt);
                int end = e.Offset + e.Length;
                if (start < e.Offset + parts * 2 || start >= end) continue;
                streams.Add(TrackDecoder.Decode(mde, start, end - start, i * 4 + p, ChannelKind.Ssg));
            }
            list.Add(new SoundEffect(i, streams));
        }
        return new SoundEffectBank(list);
    }

    /// <summary>
    /// Core ids passed to <c>ISoundSink.PlaySe</c> are 1-based (as <c>snd_se_play</c> in the original): id n plays effect n-1.
    /// The ZIGOKU.MDE bank has 17 effects and the Core uses ids 1..17, which supports this (ASSUMPTION).
    /// </summary>
    public SoundEffect? ForSeId(int id) => id >= 1 && id <= Effects.Count ? Effects[id - 1] : null;
}
