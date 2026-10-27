using Th01.Core.Game;

namespace Th01.Assets.Text;

/// <summary>
/// Reads the title, option and music-test texts from the user's OP.EXE. The strings sit next to each other in the
/// executable's string pool; they are found through anchors that are not game text (file names and printf formats),
/// and anything unexpected gives <see cref="TitleTexts.Default"/>. Padding spaces are kept exactly, and no text of the
/// game is stored in this repository. ref: th01/shiftjis/op.hpp, rank.h, resident.hpp, shiftjis/fns.hpp
/// </summary>
public static class TitleTextLoader
{
    public const string ExeName = "OP.EXE";

    // Layout of the pool, in order: file names, hit key, 4 main, 5 option, 4 ranks, 7 BGM modes, "%s%s",
    // 2 music choices, 15 titles, "%s%.2d", 14 file names, a message, "CON", the goodbye message.
    private const int Main = TitleTexts.MainChoiceCount, Options = TitleTexts.OptionChoiceCount, Ranks = TitleTexts.RankCount;
    private const int BgmAll = 7, Choices = TitleTexts.MusicChoiceCount, Titles = TitleTexts.MusicTrackCount;
    private const int RestFiles = TitleTexts.MusicTrackCount - 1;

    /// <summary>Loads the texts from OP.EXE, or the defaults when the file is missing or does not look as expected.</summary>
    public static TitleTexts Load(IAssetSource source, string exeName = ExeName)
    {
        ArgumentNullException.ThrowIfNull(source);
        if (!source.Exists(exeName)) return TitleTexts.Default;
        using var s = source.Open(exeName);
        using var ms = new MemoryStream();
        s.CopyTo(ms);
        return FromExe(ms.ToArray());
    }

    public static TitleTexts FromExe(ReadOnlySpan<byte> exe)
    {
        var segs = Split(exe);
        for (int i = 3; i + 1 < segs.Count; i++)
        {
            if (!Is(segs[i], "op_win.grp") || !Is(segs[i + 1], "reiiden")) continue;
            if (!Is(segs[i - 1], "REIIDEN3.grp") || !Is(segs[i - 2], "REIIDEN2.grp")) continue;
            var t = TryBuild(segs, i);
            if (t is not null) return t;
        }
        return TitleTexts.Default;
    }

    private static TitleTexts? TryBuild(List<byte[]> segs, int opWin)
    {
        int p = opWin + 2;                       // first string after "reiiden": the hit key
        int need = 1 + Main + Options + Ranks + BgmAll;
        if (p + need >= segs.Count || !Is(segs[p + need], "%s%s")) return null;
        int hit = p; p++;
        var main = Take(segs, ref p, Main);
        var opts = Take(segs, ref p, Options);
        var ranks = Take(segs, ref p, Ranks);
        var bgm = Take(segs, ref p, BgmAll);
        p++;                                      // "%s%s"
        if (p + Choices + Titles >= segs.Count || !Is(segs[p + Choices + Titles], "%s%.2d")) return null;
        var choices = Take(segs, ref p, Choices);
        var titles = Take(segs, ref p, Titles);
        p++;                                      // "%s%.2d"
        if (p + RestFiles > segs.Count || !EndsMdt(segs[opWin - 3])) return null;
        var files = new List<byte[]> { segs[opWin - 3] };
        files.AddRange(Take(segs, ref p, RestFiles));
        if (files.Any(f => !EndsMdt(f))) return null;

        // After the files: a message, "CON" and the goodbye message (optional: the defaults cover a missing one).
        byte[]? goodbye = p + 2 < segs.Count && Is(segs[p + 1], "CON") ? segs[p + 2] : null;

        var d = TitleTexts.Default;
        var all = new List<byte[]> { segs[hit] };
        all.AddRange(main); all.AddRange(opts); all.AddRange(ranks); all.AddRange(bgm.Take(TitleTexts.BgmModeCount));
        all.AddRange(choices); all.AddRange(titles); all.AddRange(files);
        if (goodbye is not null) all.Add(goodbye);
        var text = new List<string>();
        foreach (var b in all)
        {
            if (!TextCodec.TryDecode(b, out string s)) return null;
            text.Add(s);
        }

        int k = 0;
        string One() => text[k++];
        string[] Many(int n) => Enumerable.Range(0, n).Select(_ => One()).ToArray();
        var result = new TitleTexts
        {
            HitKey = One(),
            MainChoices = Many(Main),
            OptionChoices = Many(Options),
            Ranks = Many(Ranks),
            BgmModes = Many(TitleTexts.BgmModeCount),
            MusicChoices = Many(Choices),
            MusicTitles = Many(Titles),
            MusicFiles = Many(TitleTexts.MusicTrackCount),
            Goodbye = goodbye is not null ? One() : d.Goodbye,
        };
        return result with { FullwidthText = result.MainChoices.Any(s => s.Any(c => c > 0x7F)) };
    }

    private static byte[][] Take(List<byte[]> segs, ref int p, int n)
    {
        var r = segs.GetRange(p, n).ToArray();
        p += n;
        return r;
    }

    private static bool Is(byte[] seg, string ascii) =>
        seg.Length == ascii.Length && seg.Select((b, i) => char.ToLowerInvariant((char)b) == char.ToLowerInvariant(ascii[i])).All(x => x);

    private static bool EndsMdt(byte[] seg) =>
        seg.Length > 4 && seg[^4] == (byte)'.' && char.ToLowerInvariant((char)seg[^3]) == 'm' &&
        char.ToLowerInvariant((char)seg[^2]) == 'd' && char.ToLowerInvariant((char)seg[^1]) == 't';

    // Non-empty NUL-separated pieces, in file order.
    private static List<byte[]> Split(ReadOnlySpan<byte> exe)
    {
        var list = new List<byte[]>();
        int start = 0;
        for (int i = 0; i <= exe.Length; i++)
        {
            if (i < exe.Length && exe[i] != 0) continue;
            if (i > start) list.Add(exe[start..i].ToArray());
            start = i + 1;
        }
        return list;
    }
}
