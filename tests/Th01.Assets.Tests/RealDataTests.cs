using Th01.Assets.Config;
using Th01.Assets.Formats;

namespace Th01.Assets.Tests;

/// <summary>Run only when TH01_DATA_PATH points at the extracted game files.</summary>
public class RealDataTests
{
    private static string Folder()
    {
        string? path = GameDataConfig.Resolve();
        Skip.If(path is null || !Directory.Exists(path),
            $"Game data not configured. Set {GameDataConfig.EnvVar} or create {GameDataConfig.FileName}.");
        return path!;
    }

    private static byte[] Pack(string name)
    {
        using var fs = File.OpenRead(Path.Combine(Folder(), ExpectedFiles.ArchiveName));
        return PackFile.Read(fs, name);
    }

    [SkippableFact]
    public void Archive_MatchesExpectedEntryList()
    {
        using var fs = File.OpenRead(Path.Combine(Folder(), ExpectedFiles.ArchiveName));
        Assert.Equal(ExpectedFiles.PackEntries, PackFile.List(fs));
    }

    [SkippableFact]
    public void AllPackEntries_ParseWithTheirReader()
    {
        using var fs = File.OpenRead(Path.Combine(Folder(), ExpectedFiles.ArchiveName));
        foreach (var e in PackFile.Entries(fs))
        {
            byte[] d = PackFile.Read(fs, e);
            switch (Path.GetExtension(e.Name).ToUpperInvariant())
            {
                case ".PTN": Assert.NotEmpty(PtnSprites.Load(d).Frames); break;
                case ".BOS": Assert.NotEmpty(BosSprites.Load(d).Frames); break;
                case ".GRC": Assert.NotEmpty(GrcSprites.Load(d).Frames); break;
                case ".DAT": Assert.Equal(5, StageDat.Load(d).Count); break;
            }
        }
    }

    [SkippableFact]
    public void Ptn_HasTransparentAndOpaquePixels()
    {
        var f = PtnSprites.Load(Pack("MIKO.PTN")).Frames[0];
        Assert.Contains((byte)0, f.Alpha);
        Assert.Contains((byte)1, f.Alpha);
    }

    [SkippableFact]
    public void Bos_HasTransparentAndOpaquePixels()
    {
        var f = BosSprites.Load(Pack("BOSS1.BOS")).Frames[0];
        Assert.Contains((byte)0, f.Alpha);
        Assert.Contains((byte)1, f.Alpha);
    }

    [SkippableFact]
    public void StageDat_HasCardsAndBumpers()
    {
        var s = StageDat.Load(Pack("STAGE1.DAT"));
        int cards = 0;
        foreach (bool c in s[0].Cards) if (c) cards++;
        Assert.InRange(cards, 1, 200);
        Assert.All(s, l => Assert.All(l.Obstacles.Cast<byte>(), o => Assert.True(o <= 21)));
    }

    [SkippableFact]
    public void Grz_Loads16Images() =>
        Assert.Equal(16, GrzImage.LoadGrz(File.ReadAllBytes(Path.Combine(Folder(), "BOSS8.GRZ"))).Count);

    [SkippableFact]
    public void MaskGrf_HasInkAndBackground()
    {
        var img = GrfImage.LoadFirstPlane(File.ReadAllBytes(Path.Combine(Folder(), "MASK.GRF")));
        Assert.Contains((byte)1, img.Pixels);
        Assert.Contains((byte)0, img.Pixels);
    }

    [SkippableFact]
    public void Cfg_AndScoreFilesLoad()
    {
        string cfg = Path.Combine(Folder(), "REIIDEN.CFG"), score = Path.Combine(Folder(), "REYHINO.DAT");
        // The game creates both on first run, so a clean Japanese install does not have them.
        Skip.IfNot(File.Exists(cfg) && File.Exists(score), "REIIDEN.CFG / REYHINO.DAT are not in the game folder.");
        Assert.InRange(ReiidenCfg.Load(File.ReadAllBytes(cfg)).Rank, 0, 3);
        Assert.Equal(ScoreDat.Places, ScoreDat.Load(File.ReadAllBytes(score)).Scores.Length);
    }
}

public class RealFolderInventoryTests
{
    [SkippableFact]
    public void FolderMatchesExpectedFiles()
    {
        string? path = GameDataConfig.Resolve();
        Skip.If(path is null || !Directory.Exists(path), $"Game data not configured. Set {GameDataConfig.EnvVar}.");
        var onDisk = Directory.GetFiles(path!).Select(Path.GetFileName).ToHashSet(StringComparer.OrdinalIgnoreCase);
        var expected = ExpectedFiles.All.Select(f => f.Name).ToHashSet(StringComparer.OrdinalIgnoreCase);

        // everything on the disk (apart from text documents) is known
        var unknown = onDisk.Where(n => !expected.Contains(n!) && !n!.EndsWith(".TXT", StringComparison.OrdinalIgnoreCase)).ToList();
        Assert.True(unknown.Count == 0, "Files not in ExpectedFiles: " + string.Join(", ", unknown));

        // nothing required is missing; the only absent files are the optional ones
        Assert.All(ExpectedFiles.All.Where(f => f.Required), f => Assert.Contains(f.Name, onDisk, StringComparer.OrdinalIgnoreCase));
        var absent = expected.Where(n => !onDisk.Contains(n)).OrderBy(n => n).ToList();
        Assert.Equal(["REIIDEN.CFG", "REYHINO.DAT", "ST7.MDT"], absent);
    }
}
