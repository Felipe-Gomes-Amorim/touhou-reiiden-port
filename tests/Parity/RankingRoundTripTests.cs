using Th01.Assets.Formats;
using Th01.Core.Game;

namespace Th01.Parity.Tests;

/// <summary>
/// Round trip HiScoreTable (Core) &lt;-&gt; ScoreFile (Assets) &lt;-&gt; REYHI*.DAT on disk. The adapter below is the
/// reference conversion for the platform wiring (GameFlow, owned by T1); copy it as is.
/// </summary>
public class RankingRoundTripTests
{
    static class Adapter
    {
        static readonly (ScoreRoute Route, ushort Kanji)[] Routes =
        {
            (ScoreRoute.None, ScoreFileIO.RouteNone), (ScoreRoute.Shrine, ScoreFileIO.RouteShrine),
            (ScoreRoute.Makai, ScoreFileIO.RouteMakai), (ScoreRoute.Jigoku, ScoreFileIO.RouteJigoku),
            (ScoreRoute.Clear, ScoreFileIO.RouteClear),
        };

        public static HiScoreTable ToTable(ScoreFile f)
        {
            var t = new HiScoreTable();
            for (int i = 0; i < HiScoreTable.Places; i++)
            {
                var e = t.Entries[i];
                e.Name = (ushort[])f.Names[i].Clone();
                e.Score = f.Scores[i];
                e.Stage = f.Stages[i];
                e.Route = Routes.FirstOrDefault(r => r.Kanji == f.Routes[i]).Route;   // unknown kanji -> None
            }
            return t;
        }

        public static ScoreFile ToFile(HiScoreTable t)
        {
            var f = new ScoreFile();
            for (int i = 0; i < HiScoreTable.Places; i++)
            {
                var e = t.Entries[i];
                Array.Copy(e.Name, f.Names[i], HiScoreTable.NameKanji);
                f.Scores[i] = e.Score;
                f.Stages[i] = (short)e.Stage;
                f.Routes[i] = Routes.First(r => r.Route == e.Route).Kanji;
            }
            return f;
        }
    }

    static void AssertSame(HiScoreTable a, HiScoreTable b)
    {
        for (int i = 0; i < HiScoreTable.Places; i++)
        {
            Assert.Equal(a.Entries[i].Name, b.Entries[i].Name);
            Assert.Equal(a.Entries[i].Score, b.Entries[i].Score);
            Assert.Equal(a.Entries[i].Stage, b.Entries[i].Stage);
            Assert.Equal(a.Entries[i].Route, b.Entries[i].Route);
        }
    }

    static string TempFile() => Path.Combine(Path.GetTempPath(), "th01-ranking-" + Guid.NewGuid().ToString("N") + ".DAT");

    [Fact]
    public void DefaultFile_ToTable_ToFile_IsIdentical()
    {
        var file = ScoreFileIO.CreateDefault();
        Assert.Equal(ScoreFileIO.Serialize(file), ScoreFileIO.Serialize(Adapter.ToFile(Adapter.ToTable(file))));
    }

    [Fact]
    public void InsertedEntry_SurvivesSaveAndLoad()
    {
        var table = Adapter.ToTable(ScoreFileIO.CreateDefault());
        ushort[] name = [0x8260, 0x8261, 0x8262, 0x8140, 0x8140, 0x8140, 0x8140, 0x8140];   // full-width A B C + spaces
        int place = table.FindPlace(750);
        Assert.Equal(3, place);                                                              // default scores 1000..100
        table.Insert(place, name, 750, HiScoreTable.ClearedJigoku, ScoreRoute.Clear);

        string path = TempFile();
        try
        {
            ScoreFileIO.Save(path, Adapter.ToFile(table));
            var loaded = Adapter.ToTable(ScoreFileIO.Parse(File.ReadAllBytes(path)));
            AssertSame(table, loaded);
            Assert.Equal(750, loaded.Entries[3].Score);
            Assert.Equal(ScoreRoute.Clear, loaded.Entries[3].Route);
            Assert.Equal(200, loaded.Entries[9].Score);                                          // 100 dropped off the end
        }
        finally { File.Delete(path); File.Delete(path + ".bak"); }
    }

    [Fact]
    public void SecondSave_KeepsFirstBackup()
    {
        string path = TempFile();
        try
        {
            ScoreFileIO.Save(path, ScoreFileIO.CreateDefault());
            var t = Adapter.ToTable(ScoreFileIO.CreateDefault());
            t.Insert(0, t.Entries[0].Name, 5000, 10, ScoreRoute.Makai);
            ScoreFileIO.Save(path, Adapter.ToFile(t));
            var t2 = Adapter.ToTable(ScoreFileIO.Parse(File.ReadAllBytes(path)));
            t2.Insert(0, t2.Entries[0].Name, 9000, 11, ScoreRoute.Jigoku);
            ScoreFileIO.Save(path, Adapter.ToFile(t2));

            Assert.Equal(ScoreFileIO.Serialize(ScoreFileIO.CreateDefault()), File.ReadAllBytes(path + ".bak"));
            Assert.Equal(9000, Adapter.ToTable(ScoreFileIO.Parse(File.ReadAllBytes(path))).Entries[0].Score);
            Assert.False(File.Exists(path + ".tmp"));
        }
        finally { File.Delete(path); File.Delete(path + ".bak"); }
    }

    [Fact]
    public void CorruptFile_ThrowsAndLeavesFileUntouched()
    {
        string path = TempFile();
        try
        {
            byte[] junk = [1, 2, 3, 4];
            File.WriteAllBytes(path, junk);
            Assert.ThrowsAny<Exception>(() => ScoreFileIO.Parse(File.ReadAllBytes(path)));   // caller falls back to CreateDefault
            Assert.Equal(junk, File.ReadAllBytes(path));
        }
        finally { File.Delete(path); File.Delete(path + ".bak"); }
    }

    [SkippableFact]
    public void RealGameFiles_RoundTripByteForByte()
    {
        string? dir = Th01.Assets.Config.GameDataConfig.Resolve();
        Skip.If(dir is null || !Directory.Exists(dir), "TH01_DATA_PATH not set.");
        var files = Directory.GetFiles(dir!, "REYHI*.DAT");
        Skip.If(files.Length == 0, "No REYHI*.DAT in the game folder.");
        foreach (var f in files)
        {
            byte[] bytes = File.ReadAllBytes(f);
            var file = ScoreFileIO.Parse(bytes);
            Assert.Equal(bytes.Take(ScoreDat.Size).ToArray(), ScoreFileIO.Serialize(Adapter.ToFile(Adapter.ToTable(file))));
        }
    }
}
