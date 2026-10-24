using System.Text;
using Th01.Assets;
using Th01.Assets.Config;
using Th01.Assets.Formats;
using Th01.Assets.Text;

namespace Th01.Tools.Commands;

/// <summary>
/// font-charset &lt;out.txt&gt;: the Unicode code points that the game's texts need, plus the base sets a font
/// must cover (all hiragana and katakana, wide digits and letters, common symbols). The output lists code points
/// only (no sentences) and contains game-derived data, so it must stay outside the repo (game-data/out/).
/// </summary>
public static class AssetsFont
{
    private static readonly string[] Executables = ["REIIDEN.EXE", "OP.EXE", "FUUIN.EXE", "ZUNSOFT.COM"];

    public static string Category(int cp) => cp switch
    {
        >= 0x3041 and <= 0x309F => "hiragana",
        >= 0x30A0 and <= 0x30FF => "katakana",
        >= 0xFF61 and <= 0xFF9F => "halfwidth",
        >= 0xFF10 and <= 0xFF19 => "wide-digit",
        (>= 0xFF21 and <= 0xFF3A) or (>= 0xFF41 and <= 0xFF5A) => "wide-latin",
        (>= 0x4E00 and <= 0x9FFF) or (>= 0x3400 and <= 0x4DBF) or 0x3005 => "kanji",
        _ => "symbol",
    };

    public static int Run(string[] args)
    {
        string? folder = GameDataConfig.Resolve();
        if (args.Length < 2 || folder is null || !Directory.Exists(folder))
        {
            Console.Error.WriteLine("Usage: Th01.Tools font-charset <out.txt> [--high-only]   (game data must be configured; keep the output out of the repo)");
            return 2;
        }

        bool highOnly = args.Contains("--high-only");
        var game = new SortedSet<int>();
        foreach (string exe in Executables)
        {
            string path = Path.Combine(folder, exe);
            if (!File.Exists(path)) continue;
            byte[] bytes = File.ReadAllBytes(path);
            foreach (var h in AssetsStrings.Scan(bytes).Where(h => highOnly ? h.Confidence == "high" : h.Confidence != "low"))
                if (TextCodec.TryDecode(bytes.AsSpan(h.Offset, h.Bytes), out string text)) AddText(game, text);
        }
        // ranking: route column and the default name the game writes
        foreach (ushort route in new[] { ScoreFileIO.RouteNone, ScoreFileIO.RouteClear, ScoreFileIO.RouteShrine, ScoreFileIO.RouteMakai, ScoreFileIO.RouteJigoku })
            AddText(game, TextCodec.ToUnicode(route) ?? "");
        foreach (ushort[] name in ScoreFileIO.CreateDefault().Names.Take(1))
            AddText(game, TextCodec.DecodeCells(name));

        var baseSet = new SortedSet<int>();
        foreach (int row in new[] { 1, 2, 3, 4, 5 })
            for (int col = 1; col <= 94; col++)
                if (TextCodec.ToUnicode(TextCodec.FromJis(row, col)) is { Length: > 0 } s) AddText(baseSet, s);

        var all = new SortedSet<int>(game);
        all.UnionWith(baseSet);
        var sb = new StringBuilder();
        sb.Append("# code point, category, source (g = used by the game texts, b = base set)\n");
        foreach (int cp in all)
            sb.Append($"U+{cp:X4}\t{Category(cp)}\t{(game.Contains(cp) ? "g" : "")}{(baseSet.Contains(cp) ? "b" : "")}\n");
        File.WriteAllText(args[1], sb.ToString());

        Console.WriteLine($"{all.Count} characters in total -> {Path.GetFullPath(args[1])}");
        Console.WriteLine($"  used by the game texts: {game.Count}; base set: {baseSet.Count}; game-only (not in the base set): {game.Count(c => !baseSet.Contains(c))}");
        foreach (var g in all.GroupBy(Category).OrderByDescending(g => g.Count()))
            Console.WriteLine($"  {g.Key,-10} total {g.Count(),5}   game {g.Count(game.Contains),5}");
        return 0;
    }

    private static void AddText(SortedSet<int> set, string text)
    {
        foreach (var rune in text.EnumerateRunes())
            if (rune.Value >= 0x80 && !Rune.IsControl(rune)) set.Add(rune.Value);
    }
}
