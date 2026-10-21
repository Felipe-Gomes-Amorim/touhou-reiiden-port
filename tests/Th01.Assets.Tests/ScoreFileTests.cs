using Th01.Assets.Config;
using Th01.Assets.Formats;

namespace Th01.Assets.Tests;

public class ScoreFileTests
{
    [Fact]
    public void DefaultTable_HasTheFileLayoutAndRoundTrips()
    {
        byte[] bytes = ScoreFileIO.Serialize(ScoreFileIO.CreateDefault());
        Assert.Equal(ScoreDat.Size, bytes.Length);
        Assert.Equal("HISCORE"u8.ToArray(), bytes[..7]);
        Assert.Equal(unchecked((byte)(0x93 + 0x9C)), bytes[7]); // names carry +0x9C
        var back = ScoreFileIO.Parse(bytes);
        Assert.Equal(1000, back.Scores[0]);
        Assert.Equal(100, back.Scores[9]);
        Assert.Equal(10, back.Stages[0]);
        Assert.Equal(ScoreFileIO.RouteNone, back.Routes[3]);
        Assert.Equal(bytes, ScoreFileIO.Serialize(back));
    }

    [Fact]
    public void RoundTripKeepsEveryField()
    {
        var f = ScoreFileIO.CreateDefault();
        f.Names[2] = [0x8260, 0x8261, 0x2020, 0x2020, 0x8140, 0x8140, 0xE040, 0xFC4F];
        f.Scores[2] = int.MaxValue;
        f.Stages[2] = HiScoreStage(50);
        f.Routes[2] = ScoreFileIO.RouteClear;
        var back = ScoreFileIO.Parse(ScoreFileIO.Serialize(f));
        Assert.Equal(f.Names[2], back.Names[2]);
        Assert.Equal(int.MaxValue, back.Scores[2]);
        Assert.Equal(50, back.Stages[2]);
        Assert.Equal(ScoreFileIO.RouteClear, back.Routes[2]);
    }

    private static short HiScoreStage(int v) => (short)v;

    [Fact]
    public void RejectsWhatTheGameCouldNotReadBack()
    {
        var f = ScoreFileIO.CreateDefault();
        f.Names[0][0] = 0x0000;
        Assert.Throws<ArgumentException>(() => ScoreFileIO.Serialize(f));
        f = ScoreFileIO.CreateDefault(); f.Names[0][1] = 0x8100; // trail byte below 0x40
        Assert.Throws<ArgumentException>(() => ScoreFileIO.Serialize(f));
        f = ScoreFileIO.CreateDefault(); f.Scores[4] = -1;
        Assert.Throws<ArgumentException>(() => ScoreFileIO.Serialize(f));
        f = ScoreFileIO.CreateDefault(); f.Routes[1] = 0;
        Assert.Throws<ArgumentException>(() => ScoreFileIO.Serialize(f));
        f = ScoreFileIO.CreateDefault(); f.Names[5] = new ushort[3];
        Assert.Throws<ArgumentException>(() => ScoreFileIO.Serialize(f));
    }

    [Fact]
    public void Save_KeepsTheOriginalAsBackup()
    {
        string dir = Directory.CreateTempSubdirectory("th01-score-").FullName;
        try
        {
            string path = Path.Combine(dir, "REYHINO.DAT");
            byte[] original = [1, 2, 3];
            File.WriteAllBytes(path, original);
            var f = ScoreFileIO.CreateDefault();
            ScoreFileIO.Save(path, f);
            Assert.Equal(original, File.ReadAllBytes(path + ".bak"));
            Assert.Equal(ScoreDat.Size, File.ReadAllBytes(path).Length);
            f.Scores[0] = 5;
            ScoreFileIO.Save(path, f); // the first backup must survive
            Assert.Equal(original, File.ReadAllBytes(path + ".bak"));
            Assert.Equal(5, ScoreFileIO.Parse(File.ReadAllBytes(path)).Scores[0]);
            Assert.False(File.Exists(path + ".tmp"));
        }
        finally { Directory.Delete(dir, true); }
    }

    [SkippableFact]
    public void RealFile_IfPresent_ParsesAndRoundTrips()
    {
        string? folder = GameDataConfig.Resolve();
        Skip.If(folder is null);
        string path = Path.Combine(folder!, "REYHINO.DAT");
        Skip.IfNot(File.Exists(path), "REYHINO.DAT is not in the game folder (the game creates it).");
        byte[] bytes = File.ReadAllBytes(path);
        Assert.Equal(bytes.Length >= ScoreDat.Size ? bytes[..ScoreDat.Size] : bytes, ScoreFileIO.Serialize(ScoreFileIO.Parse(bytes)));
    }
}
