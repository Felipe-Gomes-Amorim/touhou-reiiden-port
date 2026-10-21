using Th01.Assets.Disk;

namespace Th01.Assets.Tests;

public class GameInstallerTests
{
    static string Temp() => Directory.CreateTempSubdirectory("th01-inst-").FullName;

    [Fact]
    public void Prepare_ExtractsGameFilesFromHdi_AndSkipsDosBootFiles()
    {
        string dir = Temp();
        try
        {
            string hdi = Path.Combine(dir, "game.hdi");
            File.WriteAllBytes(hdi, SyntheticHdi.Build(SyntheticHdi.FakeGameFiles()));
            string data = GameInstaller.Prepare(hdi, Path.Combine(dir, "cache"));
            Assert.Empty(GameInstaller.MissingRequired(data));
            Assert.False(File.Exists(Path.Combine(data, "IO.SYS")));
            Assert.Equal("fake OP.EXE", File.ReadAllText(Path.Combine(data, "OP.EXE")));
            Assert.True(File.Exists(Path.Combine(data, ExpectedFiles.ArchiveName)));
        }
        finally { Directory.Delete(dir, true); }
    }

    [Fact]
    public void Prepare_RejectsImageWithoutTheGame_AndLeavesNoCache()
    {
        string dir = Temp();
        try
        {
            string hdi = Path.Combine(dir, "other.hdi");
            File.WriteAllBytes(hdi, SyntheticHdi.Build(SyntheticHdi.FakeGameFiles(complete: false)));
            var ex = Assert.Throws<GameFileException>(() => GameInstaller.Prepare(hdi, Path.Combine(dir, "cache")));
            Assert.Contains("OP.EXE", ex.Message);
            Assert.False(Directory.Exists(Path.Combine(dir, "cache", "data")));
            Assert.False(Directory.Exists(Path.Combine(dir, "cache", "data.tmp")));
        }
        finally { Directory.Delete(dir, true); }
    }

    [Fact]
    public void Prepare_RejectsGarbageHdi()
    {
        string dir = Temp();
        try
        {
            string hdi = Path.Combine(dir, "bad.hdi");
            File.WriteAllBytes(hdi, new byte[50_000]);
            Assert.Throws<GameFileException>(() => GameInstaller.Prepare(hdi, Path.Combine(dir, "cache")));
        }
        finally { Directory.Delete(dir, true); }
    }

    [Fact]
    public void Prepare_AcceptsFolderAndAnyFileInsideIt()
    {
        string dir = Temp();
        try
        {
            foreach (var (name, bytes) in SyntheticHdi.FakeGameFiles()) File.WriteAllBytes(Path.Combine(dir, name), bytes);
            Assert.Equal(dir, GameInstaller.Prepare(dir, Path.Combine(dir, "cache")));
            Assert.Equal(dir, GameInstaller.Prepare(Path.Combine(dir, "REIIDEN.EXE"), Path.Combine(dir, "cache")));
        }
        finally { Directory.Delete(dir, true); }
    }

    [Fact]
    public void Prepare_ReportsMissingPath() =>
        Assert.Throws<GameFileException>(() =>
            GameInstaller.Prepare(Path.Combine(Path.GetTempPath(), "does-not-exist.hdi"), Path.GetTempPath()));
}
