using Th01.Assets.Model;

namespace Th01.Assets.Formats;

/// <summary>
/// STAGE?.DAT: the object layout of the stages of one scene. Header of 22 bytes, then 250 bytes per
/// stage (five per scene in the original). Obstacle bytes are kept raw (values 14..16 encode card HP).
/// ref: th01/formats/stagedat.hpp
/// </summary>
public static class StageDat
{
    public const int HeaderSize = 22, StageSize = 50 + 200, StagesPerScene = 5;

    public static IReadOnlyList<StageLayout> Load(ReadOnlySpan<byte> data)
    {
        if (data.Length < HeaderSize + StageSize || !data[..5].SequenceEqual("STAGE"u8))
            throw new InvalidDataException("Not a STAGE?.DAT file (missing STAGE header or no stage data).");
        int count = Math.Min((data.Length - HeaderSize) / StageSize, StagesPerScene);
        var list = new List<StageLayout>(count);
        for (int s = 0; s < count; s++)
        {
            var st = data.Slice(HeaderSize + s * StageSize, StageSize);
            var layout = new StageLayout();
            for (int row = 0; row < StageLayout.Rows; row++)
            {
                for (int col = 0; col < StageLayout.Columns; col++)
                {
                    // 4 cards per byte in the low nibble, leftmost card in bit 3.
                    layout.Cards[row, col] = (st[row * 5 + col / 4] & (8 >> (col % 4))) != 0;
                    layout.Obstacles[row, col] = st[50 + row * StageLayout.Columns + col];
                }
            }
            list.Add(layout);
        }
        return list;
    }
}

/// <summary>One REYHI*.DAT file: 10 places. Names are raw Shift-JIS bytes (decoded, not converted).</summary>
public sealed record ScoreTable(byte[][] NameBytes, int[] Scores, short[] Stages, byte[][] RouteBytes);

/// <summary>REYHI*.DAT. Name bytes are stored with +0x9C added (mod 256). ref: th01/formats/scoredat.hpp</summary>
public static class ScoreDat
{
    public const int Places = 10, NameBytes = 16, RouteBytes = 2, NameKey = 0x9C;
    public const int Size = 7 + Places * NameBytes + Places * 4 + Places * 2 + Places * RouteBytes;

    public static ScoreTable Load(ReadOnlySpan<byte> data)
    {
        if (data.Length < Size || !data[..7].SequenceEqual("HISCORE"u8))
            throw new InvalidDataException("Not a REYHI*.DAT file (missing HISCORE header or too short).");
        var names = new byte[Places][];
        var routes = new byte[Places][];
        var scores = new int[Places];
        var stages = new short[Places];
        int p = 7;
        for (int i = 0; i < Places; i++, p += NameBytes)
        {
            names[i] = new byte[NameBytes];
            for (int c = 0; c < NameBytes; c++) names[i][c] = (byte)(data[p + c] - NameKey);
        }
        for (int i = 0; i < Places; i++, p += 4) scores[i] = BitConverter.ToInt32(data.Slice(p, 4));
        for (int i = 0; i < Places; i++, p += 2) stages[i] = BitConverter.ToInt16(data.Slice(p, 2));
        for (int i = 0; i < Places; i++, p += RouteBytes) routes[i] = data.Slice(p, RouteBytes).ToArray();
        return new ScoreTable(names, scores, stages, routes);
    }
}

/// <summary>REIIDEN.CFG options. ref: th01/formats/cfg.hpp</summary>
public sealed record GameOptions(sbyte Rank, sbyte BgmMode, sbyte CreditBombs, sbyte CreditLivesExtra);

public static class ReiidenCfg
{
    public const int Size = 7 + 4;

    public static GameOptions Load(ReadOnlySpan<byte> data)
    {
        if (data.Length < Size || !data[..7].SequenceEqual("REIIDEN"u8))
            throw new InvalidDataException("Not a REIIDEN.CFG file (missing REIIDEN id or too short).");
        return new GameOptions((sbyte)data[7], (sbyte)data[8], (sbyte)data[9], (sbyte)data[10]);
    }
}
