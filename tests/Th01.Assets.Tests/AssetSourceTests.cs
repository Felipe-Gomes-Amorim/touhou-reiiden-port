using Th01.Assets;
using Th01.Assets.Config;

namespace Th01.Assets.Tests;

public class AssetSourceTests
{
    [Fact]
    public void FolderSource_ReportsSizesCaseInsensitively()
    {
        string dir = Directory.CreateTempSubdirectory("th01-test-").FullName;
        try
        {
            File.WriteAllBytes(Path.Combine(dir, "Op.Exe"), new byte[123]);
            var src = new FolderAssetSource(dir);
            Assert.True(src.Exists("OP.EXE"));
            Assert.Equal(123, src.GetSize("op.exe"));
            Assert.Null(src.GetSize("NOPE.DAT"));
            Assert.Throws<FileNotFoundException>(() => src.Open("NOPE.DAT"));
        }
        finally { Directory.Delete(dir, true); }
    }

    [SkippableFact]
    public void RealGameFolder_HasRequiredFiles()
    {
        string? path = GameDataConfig.Resolve();
        Skip.If(path is null || !Directory.Exists(path),
            $"Game data not configured. Set {GameDataConfig.EnvVar} or create {GameDataConfig.FileName} with {{\"gameDataPath\": \"...\"}}.");
        var src = new FolderAssetSource(path!);
        foreach (var f in ExpectedFiles.All.Where(f => f.Required))
            Assert.True(src.Exists(f.Name), $"Missing required file {f.Name}");
    }
}
