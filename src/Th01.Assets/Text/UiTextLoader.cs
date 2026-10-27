using Th01.Core.Game;

namespace Th01.Assets.Text;

/// <summary>
/// Reads the labels of the route menu and of the stage bonus box out of the user's own REIIDEN.EXE, so a Japanese copy
/// shows the Japanese text and a translated copy its translation, with nothing of the game's text kept in this program
/// (the two short search keys below only locate the strings). When a string table cannot be found the defaults of
/// <see cref="UiTexts"/> are kept.
/// </summary>
public static class UiTextLoader
{
    // Fullwidth "ROUND" and "STAGE" in Shift-JIS: the first characters of the first string of each table.
    static readonly byte[] RoundKey = [0x82, 0x71, 0x82, 0x6E, 0x82, 0x74, 0x82, 0x6D, 0x82, 0x63];
    static readonly byte[] StageKey = [0x82, 0x72, 0x82, 0x73, 0x82, 0x60, 0x82, 0x66, 0x82, 0x64];

    public static UiTexts Load(IAssetSource assets)
    {
        const string exe = "REIIDEN.EXE";
        if (!assets.Exists(exe)) return UiTexts.Default;
        try
        {
            using var s = assets.Open(exe);
            using var ms = new MemoryStream();
            s.CopyTo(ms);
            return FromExe(ms.ToArray());
        }
        catch (IOException e)
        {
            Console.Error.WriteLine($"{exe} could not be read for its labels: {e.Message}");
            return UiTexts.Default;
        }
    }

    public static UiTexts FromExe(ReadOnlySpan<byte> exe)
    {
        var texts = UiTexts.Default;

        // Route menu: ROUND n CLEAR, Congratulation, Select next Round, Makai, Hell: five strings in a row.
        int at = FindAtStringStart(exe, RoundKey);
        if (at >= 0 && ReadStrings(exe, at, 5) is { } route)
            texts = texts with
            {
                RouteRound = route[0], RouteCongrats = route[1], RouteSelect = route[2], RouteMakai = route[3], RouteJigoku = route[4],
            };

        // Stage bonus: title, the digit buffer, then subtitle, time, combo, resources, stage, total and hit-key labels.
        // The title is 17 fullwidth characters (34 bytes) and the digit buffer 6 (12 bytes), which tells it from the other uses of "STAGE".
        for (int from = 0; ; from = at + 1)
        {
            at = FindAtStringStart(exe[from..], StageKey);
            if (at < 0) break;
            at += from;
            if (StringLength(exe, at) != 34) continue;
            var table = ReadStrings(exe, at, 9);
            if (table is null || StringLength(exe, NextStringStart(exe, at)) != 12) continue;
            texts = texts with
            {
                BonusTitle = table[0], BonusSubtitle = table[2], BonusTime = table[3], BonusCardCombo = table[4],
                BonusResources = table[5], BonusStage = table[6], BonusTotal = table[7], BonusHitKey = table[8],
                FullwidthNumerals = true,
            };
            break;
        }
        return texts;
    }

    static int FindAtStringStart(ReadOnlySpan<byte> exe, byte[] key)
    {
        int from = 0;
        while (from < exe.Length)
        {
            int i = exe[from..].IndexOf(key);
            if (i < 0) return -1;
            i += from;
            if (i == 0 || exe[i - 1] == 0) return i;
            from = i + 1;
        }
        return -1;
    }

    static int StringLength(ReadOnlySpan<byte> exe, int start)
    {
        if (start < 0 || start >= exe.Length) return -1;
        int n = exe[start..].IndexOf((byte)0);
        return n;
    }

    static int NextStringStart(ReadOnlySpan<byte> exe, int start)
    {
        int len = StringLength(exe, start);
        return len < 0 ? -1 : start + len + 1;
    }

    /// <summary>The <paramref name="count"/> NUL-terminated strings that follow each other from <paramref name="start"/>.</summary>
    static string[]? ReadStrings(ReadOnlySpan<byte> exe, int start, int count)
    {
        var result = new string[count];
        int p = start;
        for (int i = 0; i < count; i++)
        {
            int len = StringLength(exe, p);
            if (len <= 0 || !TextCodec.TryDecode(exe.Slice(p, len), out result[i])) return null;
            p += len + 1;
        }
        return result;
    }
}
