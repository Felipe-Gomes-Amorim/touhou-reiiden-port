namespace Th01.Assets.Formats;

/// <summary>
/// The high score table of one difficulty, in file terms (primitive types only; the bridge to
/// Th01.Core.Game.HiScoreTable lives in the platform layer). A kanji is the Shift-JIS code as
/// (lead byte &lt;&lt; 8) | trail byte, for example 0x8140 for the ideographic space; two ASCII bytes
/// (such as 0x2020) are allowed too.
/// </summary>
public sealed class ScoreFile
{
    public const int Places = ScoreDat.Places;
    public const int NameKanji = ScoreDat.NameBytes / 2;

    /// <summary>[place][8] kanji codes.</summary>
    public ushort[][] Names { get; }
    public int[] Scores { get; }
    /// <summary>Stage reached, or a cleared constant (40+).</summary>
    public short[] Stages { get; }
    /// <summary>Route kanji per place (see <see cref="ScoreDat"/> Route* constants).</summary>
    public ushort[] Routes { get; }

    public ScoreFile()
    {
        Names = new ushort[Places][];
        for (int i = 0; i < Places; i++) Names[i] = new ushort[NameKanji];
        Scores = new int[Places];
        Stages = new short[Places];
        Routes = new ushort[Places];
    }
}

public static class ScoreFileIO
{
    // Shift-JIS codes of the route column and of the file the game creates on first run.
    public const ushort RouteNone = 0x96B3;   // 無
    public const ushort RouteClear = 0x8AAE;  // 完
    public const ushort RouteShrine = 0x8ED0; // 社
    public const ushort RouteMakai = 0x9682;  // 魔
    public const ushort RouteJigoku = 0x926E; // 地

    /// <summary>What the game writes when REYHI*.DAT does not exist: "東方★靈異伝　　" ten times, scores 1000..100, stages 10..1, no route.</summary>
    public static ScoreFile CreateDefault()
    {
        // 東 方 ★ 靈 異 伝 and two ideographic spaces
        ushort[] name = [0x938C, 0x95FB, 0x819A, 0xE8CB, 0x88D9, 0x9360, 0x8140, 0x8140];
        var f = new ScoreFile();
        for (int i = 0; i < ScoreFile.Places; i++)
        {
            Array.Copy(name, f.Names[i], name.Length);
            f.Scores[i] = (ScoreFile.Places - i) * 100;
            f.Stages[i] = (short)(ScoreFile.Places - i);
            f.Routes[i] = RouteNone;
        }
        return f;
    }

    /// <summary>File name for a difficulty: 0 easy, 1 normal, 2 hard, 3 lunatic (ref: th01/shiftjis/fns.hpp).</summary>
    public static string FileNameFor(int rank) => rank switch
    {
        0 => "REYHIES.DAT",
        1 => "REYHINO.DAT",
        2 => "REYHIHA.DAT",
        3 => "REYHILU.DAT",
        _ => throw new ArgumentOutOfRangeException(nameof(rank), "Rank must be 0..3."),
    };

    /// <summary>Reads the table of a difficulty from a folder; a missing file gives the default table (nothing is written).</summary>
    public static ScoreFile LoadOrDefault(string folder, int rank)
    {
        string path = Path.Combine(folder, FileNameFor(rank));
        return File.Exists(path) ? Parse(File.ReadAllBytes(path)) : CreateDefault();
    }

    public static ScoreFile Parse(ReadOnlySpan<byte> data)
    {
        var raw = ScoreDat.Load(data);
        var f = new ScoreFile();
        for (int i = 0; i < ScoreFile.Places; i++)
        {
            for (int k = 0; k < ScoreFile.NameKanji; k++) f.Names[i][k] = (ushort)((raw.NameBytes[i][k * 2] << 8) | raw.NameBytes[i][k * 2 + 1]);
            f.Scores[i] = raw.Scores[i];
            f.Stages[i] = raw.Stages[i];
            f.Routes[i] = (ushort)((raw.RouteBytes[i][0] << 8) | raw.RouteBytes[i][1]);
        }
        return f;
    }

    /// <summary>Encodes the table; throws <see cref="ArgumentException"/> for anything the game could not read back.</summary>
    public static byte[] Serialize(ScoreFile file)
    {
        ArgumentNullException.ThrowIfNull(file);
        Validate(file);
        byte[] o = new byte[ScoreDat.Size];
        "HISCORE"u8.CopyTo(o);
        int p = 7;
        for (int i = 0; i < ScoreFile.Places; i++)
            for (int k = 0; k < ScoreFile.NameKanji; k++)
            {
                o[p++] = (byte)((file.Names[i][k] >> 8) + ScoreDat.NameKey);
                o[p++] = (byte)((file.Names[i][k] & 0xFF) + ScoreDat.NameKey);
            }
        for (int i = 0; i < ScoreFile.Places; i++, p += 4) BitConverter.TryWriteBytes(o.AsSpan(p), file.Scores[i]);
        for (int i = 0; i < ScoreFile.Places; i++, p += 2) BitConverter.TryWriteBytes(o.AsSpan(p), file.Stages[i]);
        for (int i = 0; i < ScoreFile.Places; i++, p += 2)
        {
            o[p] = (byte)(file.Routes[i] >> 8);
            o[p + 1] = (byte)(file.Routes[i] & 0xFF);
        }
        return o;
    }

    /// <summary>A code is valid when it is two printable ASCII bytes or one Shift-JIS double-byte character.</summary>
    public static bool IsValidKanji(ushort code)
    {
        int lead = code >> 8, trail = code & 0xFF;
        if (lead is >= 0x20 and <= 0x7E && trail is >= 0x20 and <= 0x7E) return true;
        bool leadOk = lead is (>= 0x81 and <= 0x9F) or (>= 0xE0 and <= 0xFC);
        bool trailOk = trail is >= 0x40 and <= 0xFC && trail != 0x7F;
        return leadOk && trailOk;
    }

    public static void Validate(ScoreFile file)
    {
        if (file.Names.Length != ScoreFile.Places || file.Scores.Length != ScoreFile.Places ||
            file.Stages.Length != ScoreFile.Places || file.Routes.Length != ScoreFile.Places)
            throw new ArgumentException($"A score table has exactly {ScoreFile.Places} places.");
        for (int i = 0; i < ScoreFile.Places; i++)
        {
            if (file.Names[i].Length != ScoreFile.NameKanji) throw new ArgumentException($"Place {i}: a name has exactly {ScoreFile.NameKanji} kanji.");
            foreach (ushort k in file.Names[i])
                if (!IsValidKanji(k)) throw new ArgumentException($"Place {i}: 0x{k:X4} is not a valid kanji code.");
            if (file.Scores[i] < 0) throw new ArgumentException($"Place {i}: negative score.");
            if (file.Stages[i] < 0) throw new ArgumentException($"Place {i}: negative stage.");
            if (!IsValidKanji(file.Routes[i])) throw new ArgumentException($"Place {i}: route 0x{file.Routes[i]:X4} is not a valid kanji code.");
        }
    }

    /// <summary>
    /// Writes the table to <paramref name="path"/>. An existing file is first copied to path + ".bak" (kept if a
    /// backup already exists, so the very first original survives), and the new content goes through a temporary file.
    /// </summary>
    public static void Save(string path, ScoreFile file)
    {
        byte[] data = Serialize(file);
        if (File.Exists(path))
        {
            string bak = path + ".bak";
            if (!File.Exists(bak)) File.Copy(path, bak);
        }
        string tmp = path + ".tmp";
        File.WriteAllBytes(tmp, data);
        File.Move(tmp, path, overwrite: true);
    }
}
