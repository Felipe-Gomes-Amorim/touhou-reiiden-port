using Th01.Assets.Model;

namespace Th01.Assets.Formats;

/// <summary>
/// Links strings to the code that uses them in a Borland-built DOS executable. The data group's segment is
/// found among the segment values of the MZ relocations (the one whose offsets are hit most by 16-bit
/// immediates); every <c>mov r16, imm16</c> / <c>push imm16</c> in the code that points at a text run is a reference.
/// The compiler emits code in source order, so the references in code order replay the script's order.
/// </summary>
public static class ExeStringRefs
{
    public sealed record Result(int HeaderSize, int DataBase, IReadOnlyList<StringRef> Refs);

    /// <summary>Returns null if the file is not an MZ executable or no data segment explains any reference.</summary>
    public static Result? Scan(ReadOnlySpan<byte> exe, IReadOnlyList<TextRun> runs)
    {
        if (exe.Length < 0x1C || exe[0] != 'M' || exe[1] != 'Z') return null;
        int header = BitConverter.ToUInt16(exe[8..]) * 16;
        int relocCount = BitConverter.ToUInt16(exe[6..]), relocOffset = BitConverter.ToUInt16(exe[0x18..]);
        if (header >= exe.Length || relocOffset + relocCount * 4 > exe.Length) return null;

        var segments = new Dictionary<int, int>();
        for (int i = 0; i < relocCount; i++)
        {
            int off = BitConverter.ToUInt16(exe[(relocOffset + i * 4)..]);
            int seg = BitConverter.ToUInt16(exe[(relocOffset + i * 4 + 2)..]);
            int at = header + seg * 16 + off;
            if (at + 1 >= exe.Length) continue;
            int value = BitConverter.ToUInt16(exe[at..]);
            segments[value] = segments.GetValueOrDefault(value) + 1;
        }

        Result? best = null;
        int bestDistinct = 0;
        foreach (int seg in segments.OrderByDescending(kv => kv.Value).Take(64).Select(kv => kv.Key))
        {
            int baseOffset = header + seg * 16;
            if (baseOffset >= exe.Length) continue;
            var refs = Collect(exe, header, baseOffset, runs);
            int distinct = refs.Select(r => r.DataOffset).Distinct().Count();
            if (distinct > bestDistinct) { bestDistinct = distinct; best = new Result(header, baseOffset, refs); }
        }
        return best;
    }

    private static List<StringRef> Collect(ReadOnlySpan<byte> exe, int header, int baseOffset, IReadOnlyList<TextRun> runs)
    {
        var byData = new Dictionary<int, TextRun>();
        foreach (var r in runs)
            if (r.Offset >= baseOffset && r.Offset - baseOffset <= ushort.MaxValue) byData[r.Offset - baseOffset] = r;

        var refs = new List<StringRef>();
        for (int p = header; p + 2 < baseOffset && p + 2 < exe.Length; p++)
        {
            byte op = exe[p];
            if (!(op is >= 0xB8 and <= 0xBF or 0x68)) continue;
            int word = BitConverter.ToUInt16(exe[(p + 1)..]);
            if (byData.TryGetValue(word, out var run)) refs.Add(new StringRef(p - header, word, run));
        }
        return refs;
    }

    public static bool IsImageName(string text) =>
        text.EndsWith(".GRP", StringComparison.OrdinalIgnoreCase) || text.EndsWith(".GRF", StringComparison.OrdinalIgnoreCase);

    /// <summary>
    /// Groups references into scenes: each reference to an image name starts a scene; the text strings that follow
    /// (more than 3 characters, not file names, not repeats of the previous line) are its lines.
    /// </summary>
    public static IReadOnlyList<CutsceneScene> Scenes(IReadOnlyList<StringRef> refs)
    {
        var scenes = new List<CutsceneScene>();
        string? image = null;
        int at = 0;
        var lines = new List<TextRun>();
        void Flush()
        {
            if (image is not null) scenes.Add(new CutsceneScene(image, at, lines.ToArray()));
            lines = new List<TextRun>();
        }
        foreach (var r in refs)
        {
            string t = r.Run.Text;
            if (IsImageName(t)) { Flush(); image = t; at = r.CodeOffset; }
            else if (image is not null && !IsFileName(t) && t.Length > 3 && (lines.Count == 0 || lines[^1].Offset != r.Run.Offset))
                lines.Add(r.Run);
        }
        Flush();
        return scenes;
    }

    private static bool IsFileName(string t) =>
        t.Length <= 12 && t.Contains('.') && !t.Contains(' ') && t.All(c => char.IsLetterOrDigit(c) || c is '.' or '_');
}
