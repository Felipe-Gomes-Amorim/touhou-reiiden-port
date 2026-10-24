using Th01.Assets.Formats;
using Th01.Assets.Text;

namespace Th01.Audio.Playback;

/// <summary>
/// State of the title screen's music test (op_01.cpp, music_update_and_render): left/right moves through the 15 tracks of
/// <see cref="BgmTracks.MusicRoom"/> with wrap-around, OK plays the selected one. The title of each track is read from the
/// comment at the start of its own .MDT file when the screen is shown (nothing of it is stored in this repository).
/// </summary>
public sealed class MusicRoom
{
    public int Index { get; private set; }
    public int Count => BgmTracks.MusicRoom.Count;
    public string Track => BgmTracks.MusicRoom[Index];
    /// <summary>The number the original prints next to the choice ("%.2d" of the index).</summary>
    public string Number => Index.ToString("00");

    public void Next() => Index = (Index + 1) % Count;
    public void Previous() => Index = (Index + Count - 1) % Count;

    /// <summary>
    /// First line of the comment of an .MDT (the track title), or an empty string if the file has none or cannot be read.
    /// The comment is Shift-JIS, padded with full-width spaces, and ends at a '$'.
    /// </summary>
    public static string ReadTitle(byte[] mdt)
    {
        try
        {
            var text = MdtFile.Parse(mdt).Text.Span;
            int end = text.IndexOf((byte)'$');
            if (end >= 0) text = text[..end];
            int eol = text.IndexOfAny((byte)'\r', (byte)'\n');
            if (eol >= 0) text = text[..eol];
            return TextCodec.TryDecode(text, out string s) ? s.Trim(' ', '　', '\0') : string.Empty;
        }
        catch (InvalidDataException)
        {
            return string.Empty;
        }
    }
}
