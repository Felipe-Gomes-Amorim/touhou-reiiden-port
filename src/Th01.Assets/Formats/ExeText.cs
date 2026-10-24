using Th01.Assets.Model;
using Th01.Assets.Text;

namespace Th01.Assets.Formats;

/// <summary>
/// Finds Shift-JIS text in a DOS executable. Runs end at a 0 byte or at the first byte that is not valid
/// Shift-JIS; ASCII-only runs shorter than <see cref="MinAscii"/> and Japanese runs shorter than
/// <see cref="MinJapanese"/> characters are dropped as noise.
/// </summary>
public static class ExeText
{
    public const int MinAscii = 4;
    public const int MinJapanese = 5;
    public const int MinKanaPercent = 40;

    public static CutsceneText Scan(string source, ReadOnlySpan<byte> exe)
    {
        var runs = new List<TextRun>();
        int i = 0;
        while (i < exe.Length)
        {
            int end = RunEnd(exe, i);
            if (end > i)
            {
                if (TextCodec.TryDecode(exe[i..end], out string text) && Keep(text)) runs.Add(new TextRun(i, end - i, text));
                i = end;
            }
            else i++;
        }
        return new CutsceneText(source, runs);
    }

    private static bool IsHalfKana(byte b) => b is >= 0xA1 and <= 0xDF;

    private static bool Keep(string text)
    {
        bool japanese = text.Any(c => c > 0x7F);
        int chars = text.Count(c => !char.IsLowSurrogate(c));
        if (japanese)
        {
            // Code bytes often decode as plausible kanji; real dialogue is rich in kana and punctuation.
            int kana = text.Count(c => c is >= '　' and <= 'ヿ' or >= '＀' and <= '￯');
            return chars >= MinJapanese && kana * 100 >= chars * MinKanaPercent;
        }
        return chars >= MinAscii && text.All(c => c is >= ' ' and < (char)0x7F);
    }

    /// <summary>End (exclusive) of the text run that starts at <paramref name="start"/>; start itself if none.</summary>
    private static int RunEnd(ReadOnlySpan<byte> d, int start)
    {
        int i = start;
        while (i < d.Length)
        {
            byte b = d[i];
            if (b is >= 0x20 and < 0x7F || b == (byte)'\n' || b == (byte)'\r' || IsHalfKana(b)) i++;
            else if (TextCodec.IsLeadByte(b) && i + 1 < d.Length && TextCodec.IsTrailByte(d[i + 1])) i += 2;
            else break;
        }
        return i;
    }
}
