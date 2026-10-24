using Th01.Assets.Formats;
using Th01.Core.Game;

namespace Th01.Platform.MonoGame;

/// <summary>Where the high score tables live. One table per difficulty.</summary>
public interface IScoreStore
{
    HiScoreTable Load(Rank rank);

    /// <summary>Persists a table. Must not throw: a failure leaves the in-memory table as the only copy.</summary>
    void Save(Rank rank, HiScoreTable table);
}

/// <summary>Tables kept only for the current run (tests, or when the disk is not usable).</summary>
public sealed class MemoryScoreStore : IScoreStore
{
    public HiScoreTable Load(Rank rank) => ScoreConvert.FromFile(ScoreFileIO.CreateDefault());
    public void Save(Rank rank, HiScoreTable table) { }
}

/// <summary>
/// REYHI*.DAT files in the user's data folder (never the original disk image). The first time a table is needed, the
/// user's own file from the game folder is copied if it exists, otherwise the game's default table is used. Each
/// write keeps a ".bak" of the first version (see <see cref="ScoreFileIO.Save"/>). Any I/O problem is reported on the
/// console and the run goes on with the table in memory.
/// </summary>
public sealed class FileScoreStore(string userDir, string? gameDir = null) : IScoreStore
{
    public static string FileName(Rank rank) => rank switch
    {
        Rank.Easy => "REYHIES.DAT",
        Rank.Normal => "REYHINO.DAT",
        Rank.Hard => "REYHIHA.DAT",
        _ => "REYHILU.DAT",
    };

    public HiScoreTable Load(Rank rank)
    {
        string local = Path.Combine(userDir, FileName(rank));
        foreach (string? path in new[] { local, gameDir is null ? null : Path.Combine(gameDir, FileName(rank)) })
        {
            if (path is null || !File.Exists(path)) continue;
            try { return ScoreConvert.FromFile(ScoreFileIO.Parse(File.ReadAllBytes(path))); }
            catch (Exception e) when (e is InvalidDataException or IOException or UnauthorizedAccessException)
            {
                Console.Error.WriteLine($"{path} could not be read ({e.Message}); using the default table.");
            }
        }
        return ScoreConvert.FromFile(ScoreFileIO.CreateDefault());
    }

    public void Save(Rank rank, HiScoreTable table)
    {
        try
        {
            Directory.CreateDirectory(userDir);
            ScoreFileIO.Save(Path.Combine(userDir, FileName(rank)), ScoreConvert.ToFile(table));
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or ArgumentException)
        {
            Console.Error.WriteLine($"Could not save the high scores ({e.Message}); they stay in memory until the game closes.");
        }
    }
}

/// <summary>Bridge between the file model (Th01.Assets) and the Core's <see cref="HiScoreTable"/>.</summary>
public static class ScoreConvert
{
    public static HiScoreTable FromFile(ScoreFile f)
    {
        var t = new HiScoreTable();
        for (int i = 0; i < HiScoreTable.Places; i++)
        {
            var e = t.Entries[i];
            e.Name = (ushort[])f.Names[i].Clone();
            e.Score = f.Scores[i];
            e.Stage = f.Stages[i];
            e.Route = f.Routes[i] switch
            {
                ScoreFileIO.RouteShrine => ScoreRoute.Shrine,
                ScoreFileIO.RouteMakai => ScoreRoute.Makai,
                ScoreFileIO.RouteJigoku => ScoreRoute.Jigoku,
                ScoreFileIO.RouteClear => ScoreRoute.Clear,
                _ => ScoreRoute.None,
            };
        }
        return t;
    }

    public static ScoreFile ToFile(HiScoreTable t)
    {
        var f = new ScoreFile();
        for (int i = 0; i < HiScoreTable.Places; i++)
        {
            var e = t.Entries[i];
            f.Names[i] = (ushort[])e.Name.Clone();
            f.Scores[i] = e.Score;
            f.Stages[i] = (short)Math.Clamp(e.Stage, 0, short.MaxValue);
            f.Routes[i] = e.Route switch
            {
                ScoreRoute.Shrine => ScoreFileIO.RouteShrine,
                ScoreRoute.Makai => ScoreFileIO.RouteMakai,
                ScoreRoute.Jigoku => ScoreFileIO.RouteJigoku,
                ScoreRoute.Clear => ScoreFileIO.RouteClear,
                _ => ScoreFileIO.RouteNone,
            };
        }
        return f;
    }

    /// <summary>
    /// The name entry only offers ASCII; the file wants Shift-JIS, so a letter is stored as the full-width letter the original
    /// would show (A-Z 0x8260.., 0-9 0x824F.., space 0x8140, '-' 0x817C, '.' 0x8144).
    /// </summary>
    public static ushort EncodeNameChar(char c) => c switch
    {
        >= 'A' and <= 'Z' => (ushort)(0x8260 + (c - 'A')),
        >= '0' and <= '9' => (ushort)(0x824F + (c - '0')),
        '-' => 0x817C,
        '.' => 0x8144,
        _ => 0x8140,
    };

    /// <summary>Reverse of <see cref="EncodeNameChar"/> plus plain ASCII pairs; anything else is looked up as Shift-JIS (kanji show with the bundled Japanese font) and is '?' if it has no character.</summary>
    public static char DecodeNameCell(ushort code)
    {
        if (code is >= 0x8260 and <= 0x8279) return (char)('A' + (code - 0x8260));
        if (code is >= 0x824F and <= 0x8258) return (char)('0' + (code - 0x824F));
        return code switch
        {
            0x8140 or 0x2020 => ' ',
            0x817C => '-',
            0x8144 => '.',
            _ => (code >> 8) is >= 0x20 and < 0x7F && (code & 0xFF) == 0x20 ? (char)(code >> 8)
                : Th01.Assets.Text.TextCodec.ToUnicode(code) is { Length: 1 } one ? one[0] : '?',
        };
    }
}
