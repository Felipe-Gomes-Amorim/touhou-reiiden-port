using Th01.Assets.Config;
using Th01.Assets.Text;

namespace Th01.Assets.Tests;

public class GameDataCheckTests
{
    private static MemoryAssetSource Good(bool fullwidth = false, bool withArchive = true)
    {
        var src = new MemoryAssetSource();
        byte[] op = TitleTextLoaderTests.FakeExe();
        if (fullwidth) op = TextCodec.Encode(TextCodec.Decode(op).Replace("m3", "ＡＢ"));
        src.Add("OP.EXE", op);
        src.Add("REIIDEN.EXE", [1, 2, 3]);
        src.Add("ST0.GRP", [1]);
        if (withArchive)
            src.Add(ExpectedFiles.ArchiveName, PackBuilder.Build(ExpectedFiles.PackEntries.Select(n => (n, new byte[] { 1, 2, 3 }, false)).ToArray()));
        return src;
    }

    [Fact]
    public void CompleteCopy_IsValidAndTheEditionComesFromTheTexts()
    {
        var r = GameDataCheck.Inspect(Good());
        Assert.True(r.Valid);
        Assert.Empty(r.Problems);
        Assert.Equal(GameEdition.Translated, r.Edition);
        Assert.Contains("REIIDEN.EXE", r.EssentialFilesFound);
        Assert.Equal(4, r.EssentialFilesFound.Count);
        Assert.False(r.FromDiskImage);
        Assert.Equal(GameEdition.Japanese, GameDataCheck.Inspect(Good(fullwidth: true)).Edition);
        var patched = Good(fullwidth: true);
        patched.Add("ST7.MDT", [1]);
        Assert.Equal(GameEdition.Translated, GameDataCheck.Inspect(patched).Edition); // fullwidth menu text alone does not mean Japanese
    }

    [Fact]
    public void MissingFiles_AreReportedWithCodes()
    {
        var r = GameDataCheck.Inspect(Good(withArchive: false));
        Assert.False(r.Valid);
        var p = Assert.Single(r.Problems);
        Assert.Equal(GameProblemCode.MissingFile, p.Code);
        Assert.Equal(ExpectedFiles.ArchiveName, p.File);
        Assert.DoesNotContain(ExpectedFiles.ArchiveName, r.EssentialFilesFound);
        Assert.Equal(GameEdition.Translated, r.Edition);
    }

    [Fact]
    public void EmptyRequiredFile_IsUnreadable()
    {
        var src = Good();
        src.Add("ST0.GRP", []);
        var r = GameDataCheck.Inspect(src);
        Assert.False(r.Valid);
        Assert.Contains(r.Problems, p => p.Code == GameProblemCode.UnreadableFile && p.File == "ST0.GRP");
    }

    [Fact]
    public void BrokenAndIncompleteArchives()
    {
        var bad = Good(withArchive: false);
        bad.Add(ExpectedFiles.ArchiveName, new byte[10]);
        Assert.Contains(GameDataCheck.Inspect(bad).Problems, p => p.Code == GameProblemCode.ArchiveInvalid);

        var few = Good(withArchive: false);
        few.Add(ExpectedFiles.ArchiveName, PackBuilder.Build(("STAGE0.DAT", [1, 2], false)));
        var r = GameDataCheck.Inspect(few);
        Assert.False(r.Valid);
        Assert.Equal(ExpectedFiles.PackEntries.Count - 1, r.Problems.Count(p => p.Code == GameProblemCode.ArchiveEntryMissing));
    }

    [Fact]
    public void UnknownTexts_GiveAWarningButStayValid()
    {
        var src = Good();
        src.Add("OP.EXE", [1, 2, 3]);
        var r = GameDataCheck.Inspect(src);
        Assert.True(r.Valid);
        Assert.Equal(GameEdition.Unknown, r.Edition);
        var w = Assert.Single(r.Problems);
        Assert.Equal(GameProblemCode.EditionUnknown, w.Code);
        Assert.False(w.IsError);
    }

    [Fact]
    public void Paths_Folder_File_Missing_And_BadImage()
    {
        string dir = Path.Combine(Path.GetTempPath(), "th01-check-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        try
        {
            Assert.Equal(GameProblemCode.MissingFile, GameDataCheck.Inspect(dir).Problems[0].Code);
            File.WriteAllBytes(Path.Combine(dir, "OP.EXE"), [1]);
            Assert.Contains("OP.EXE", GameDataCheck.Inspect(Path.Combine(dir, "OP.EXE")).EssentialFilesFound); // a file: its folder is checked

            Assert.Equal(GameProblemCode.NotFound, GameDataCheck.Inspect(Path.Combine(dir, "nope.hdi")).Problems[0].Code);
            Assert.Equal(GameProblemCode.NotFound, GameDataCheck.Inspect("").Problems[0].Code);

            string junk = Path.Combine(dir, "junk.hdi");
            File.WriteAllBytes(junk, new byte[20000]);
            var r = GameDataCheck.Inspect(junk);
            Assert.False(r.Valid);
            Assert.True(r.FromDiskImage);
            Assert.Equal(GameProblemCode.DiskImageUnrecognized, r.Problems[0].Code);
            File.WriteAllBytes(junk, [1, 2, 3]);
            Assert.Equal(GameProblemCode.DiskImageUnrecognized, GameDataCheck.Inspect(junk).Problems[0].Code);
        }
        finally { Directory.Delete(dir, true); }
    }

    [Fact]
    public void SyntheticDiskImage_IsReadInMemory()
    {
        string path = Path.Combine(Path.GetTempPath(), "th01-check-" + Guid.NewGuid().ToString("N") + ".hdi");
        try
        {
            File.WriteAllBytes(path, SyntheticHdi.Build(SyntheticHdi.FakeGameFiles()));
            var r = GameDataCheck.Inspect(path);
            Assert.True(r.FromDiskImage);
            Assert.DoesNotContain(r.Problems, p => p.Code == GameProblemCode.DiskImageUnrecognized);
            File.WriteAllBytes(path, SyntheticHdi.Build(SyntheticHdi.FakeGameFiles(complete: false)));
            Assert.Contains(GameDataCheck.Inspect(path).Problems, p => p.Code == GameProblemCode.MissingFile);
        }
        finally { File.Delete(path); }
    }

    [SkippableFact]
    public void RealFolder_IsValid()
    {
        string? folder = GameDataConfig.Resolve();
        Skip.If(folder is null || !File.Exists(Path.Combine(folder!, ExpectedFiles.ArchiveName)), $"Game data not configured. Set {GameDataConfig.EnvVar}.");
        var r = GameDataCheck.Inspect(folder!);
        Assert.True(r.Valid, string.Join("; ", r.Problems.Select(p => p.Message)));
        Assert.NotEqual(GameEdition.Unknown, r.Edition);
        Assert.Equal(4, r.EssentialFilesFound.Count);
    }

    [SkippableFact]
    public void RealDiskImages_AreValidWithTheRightEdition()
    {
        string? folder = GameDataConfig.Resolve();
        string? root = folder is null ? null : Path.GetDirectoryName(folder.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar));
        Skip.If(root is null, $"Game data not configured. Set {GameDataConfig.EnvVar}.");
        var images = Directory.GetFiles(root!, "*.hdi");
        Skip.If(images.Length == 0, "No .hdi next to the game data folder.");
        bool any = false;
        foreach (string hdi in images)
        {
            var r = GameDataCheck.Inspect(hdi);
            if (!r.Valid) continue; // other images in the folder may be unrelated
            any = true;
            Assert.True(r.FromDiskImage);
            Assert.NotEqual(GameEdition.Unknown, r.Edition);
            string n = Path.GetFileName(hdi);
            if (n.Contains("T-En") || n.Equals("th01e.hdi", StringComparison.OrdinalIgnoreCase)) Assert.Equal(GameEdition.Translated, r.Edition);
            if (n.Equals("th01.hdi", StringComparison.OrdinalIgnoreCase)) Assert.Equal(GameEdition.Japanese, r.Edition);
        }
        Skip.IfNot(any, "No valid game image found.");
    }
}
