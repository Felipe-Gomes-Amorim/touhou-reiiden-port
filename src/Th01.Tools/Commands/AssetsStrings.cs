using System.Text;

namespace Th01.Tools.Commands;

/// <summary>
/// scan-strings &lt;exe&gt; [csv-out]: catalogues NUL-terminated Shift-JIS strings (offset, size, characters).
/// The text itself is never printed or written. The CSV (offset,bytes,chars,confidence,class) must stay outside the repo.
/// </summary>
public static class AssetsStrings
{
    internal sealed record Hit(int Offset, int Bytes, int Chars, int DoubleByte, string Confidence, string Class);

    public static int Run(string[] args)
    {
        if (args.Length < 2) { Console.Error.WriteLine("Usage: Th01.Tools scan-strings <file.exe|com> [csv-out]"); return 2; }
        Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
        byte[] exe = File.ReadAllBytes(args[1]);
        var hits = Scan(exe);

        Console.WriteLine($"{args[1]}: {exe.Length:N0} bytes, {hits.Count} Shift-JIS string(s)");
        foreach (var g in hits.GroupBy(h => h.Confidence).OrderBy(g => g.Key))
            Console.WriteLine($"  {g.Key,-7} {g.Count(),5} string(s), {g.Sum(h => h.Bytes):N0} bytes");
        foreach (var g in hits.GroupBy(h => h.Class).OrderByDescending(g => g.Count()))
            Console.WriteLine($"  class {g.Key,-9} {g.Count(),5}");
        var high = hits.Where(h => h.Confidence == "high").ToList();
        if (high.Count > 0)
            Console.WriteLine($"  high-confidence strings lie between offsets 0x{high.Min(h => h.Offset):X} and 0x{high.Max(h => h.Offset + h.Bytes):X}");

        if (args.Length > 2)
        {
            var sb = new StringBuilder("offset,bytes,chars,confidence,class\n");
            foreach (var h in hits) sb.Append($"0x{h.Offset:X},{h.Bytes},{h.Chars},{h.Confidence},{h.Class}\n");
            File.WriteAllText(args[2], sb.ToString());
            Console.WriteLine($"  catalogue written to {Path.GetFullPath(args[2])}");
        }
        return 0;
    }

    /// <summary>Finds the NUL-terminated Shift-JIS strings of an executable image.</summary>
    internal static List<Hit> Scan(byte[] exe)
    {
        Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
        var sjis = Encoding.GetEncoding(932, EncoderFallback.ExceptionFallback, DecoderFallback.ExceptionFallback);
        var hits = new List<Hit>();
        int start = 0;
        for (int i = 0; i <= exe.Length; i++)
        {
            if (i < exe.Length && exe[i] != 0) continue;
            if (i > start) TryAdd(exe, start, i - start, sjis, hits);
            start = i + 1;
        }
        return hits;
    }

    private static void TryAdd(byte[] exe, int offset, int length, Encoding sjis, List<Hit> hits)
    {
        if (length < 2) return;
        // every byte must belong to printable ASCII, CR/LF/TAB, half-width katakana or a valid double-byte character
        int p = offset, end = offset + length, doubles = 0, chars = 0, kana = 0, kanji = 0, full = 0;
        while (p < end)
        {
            byte b = exe[p];
            if (b is >= 0x20 and <= 0x7E or 0x0D or 0x0A or 0x09) { p++; chars++; continue; }
            if (b is >= 0xA1 and <= 0xDF) { p++; chars++; continue; }
            bool lead = b is (>= 0x81 and <= 0x84) or (>= 0x88 and <= 0x9F) or (>= 0xE0 and <= 0xEA);
            if (!lead || p + 1 >= end) return;
            byte t = exe[p + 1];
            if (t < 0x40 || t == 0x7F || t > 0xFC) return;
            string s;
            try { s = sjis.GetString(exe, p, 2); } catch (DecoderFallbackException) { return; }
            if (s.Length != 1) return;
            char c = s[0];
            if (c is >= '぀' and <= 'ヿ') kana++;
            else if (c is >= '一' and <= '鿿') kanji++;
            else full++;
            doubles++; chars++; p += 2;
        }
        if (doubles == 0) return;
        string conf = length >= 6 && doubles >= 2 ? "high" : length >= 4 ? "medium" : "low";
        string cls = kanji > 0 && kana == 0 ? "kanji" : kana > 0 && kanji == 0 ? "kana" : kana > 0 ? "mixed" : "symbols";
        hits.Add(new Hit(offset, length, chars, doubles, conf, cls));
    }
}
