using Th01.Assets.Disk;
using Th01.Assets.Formats;
using Th01.Assets.Text;
using Th01.Core.Game;

namespace Th01.Assets;

public enum GameEdition { Unknown, Japanese, Translated }

public enum GameProblemCode
{
    /// <summary>The file or folder does not exist.</summary>
    NotFound,
    /// <summary>The path is a disk image that is not a readable Anex86 .hdi with a FAT12 partition.</summary>
    DiskImageUnrecognized,
    /// <summary>The disk image is too large to be a game image.</summary>
    DiskImageTooLarge,
    /// <summary>A required game file is not there.</summary>
    MissingFile,
    /// <summary>A required file exists but cannot be read, or is empty.</summary>
    UnreadableFile,
    /// <summary>The data archive is not a valid archive.</summary>
    ArchiveInvalid,
    /// <summary>A file the game needs is missing from the data archive.</summary>
    ArchiveEntryMissing,
    /// <summary>A file inside the data archive does not decompress.</summary>
    ArchiveEntryUnreadable,
    /// <summary>Warning: the files are fine but the language of the texts could not be told.</summary>
    EditionUnknown,
}

/// <param name="IsError">False for a warning, which does not make the copy invalid.</param>
public sealed record GameProblem(GameProblemCode Code, string Message, string? File = null, bool IsError = true);

/// <summary>Result of <see cref="GameDataCheck"/>. Immutable.</summary>
/// <param name="Valid">True when the copy has everything the game needs (no error problems).</param>
/// <param name="Edition">Translated when the English patch's extra music file is present, Japanese for the original, Unknown when the texts could not be read (see <c>DetectEdition</c>).</param>
/// <param name="Problems">Short messages with codes, errors first; warnings do not clear <see cref="Valid"/>.</param>
/// <param name="EssentialFilesFound">Names of the required files that were found and readable.</param>
/// <param name="FromDiskImage">True when the input was a .hdi image (its root files were read in memory; nothing is written).</param>
public sealed record GameDataReport(
    bool Valid, GameEdition Edition, IReadOnlyList<GameProblem> Problems, IReadOnlyList<string> EssentialFilesFound, bool FromDiskImage);

/// <summary>
/// Checks the user's own copy of the game (a folder, a file inside one, or a .hdi disk image) without writing anything.
/// Never throws for a bad input: every failure becomes a <see cref="GameProblem"/>. Nothing of the game is stored here.
/// </summary>
public static class GameDataCheck
{
    const long MaxImageBytes = 512L * 1024 * 1024;

    static readonly string[] BootFiles = ["IO.SYS", "MSDOS.SYS", "COMMAND.COM", "AUTOEXEC.BAT", "GAME.BAT"];

    /// <summary>Inspects a folder, a .hdi image, or any other file (its folder is inspected, like the launcher does).</summary>
    public static GameDataReport Inspect(string path)
    {
        if (string.IsNullOrWhiteSpace(path)) return Fail(GameProblemCode.NotFound, "No game file or folder was given.", null);
        try
        {
            if (Directory.Exists(path)) return Inspect(new FolderAssetSource(path));
            if (!File.Exists(path)) return Fail(GameProblemCode.NotFound, "The file or folder was not found.", path);
            if (!path.EndsWith(".hdi", StringComparison.OrdinalIgnoreCase))
                return Inspect(new FolderAssetSource(Path.GetDirectoryName(Path.GetFullPath(path))!));
            return InspectImage(path);
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            return Fail(GameProblemCode.UnreadableFile, "The game files could not be read: " + e.Message, path);
        }
    }

    static GameDataReport InspectImage(string path)
    {
        if (new FileInfo(path).Length > MaxImageBytes)
            return Fail(GameProblemCode.DiskImageTooLarge, "That file is too large to be a Touhou Reiiden disk image.", path, image: true);
        var source = new MemoryAssetSource();
        try
        {
            var image = new HdiImage(File.ReadAllBytes(path));
            foreach (var e in image.Enumerate())
            {
                if (e.IsDirectory || e.Path.Contains('/')) continue;
                if (BootFiles.Contains(e.Path, StringComparer.OrdinalIgnoreCase)) continue;
                source.Add(e.Path, image.ReadFile(e));
            }
        }
        catch (Exception e) when (e is InvalidDataException or ArgumentException or OverflowException or IndexOutOfRangeException)
        {
            return Fail(GameProblemCode.DiskImageUnrecognized,
                "This disk image could not be read. Only Anex86 .hdi images with a FAT12 partition are supported.", path, image: true);
        }
        return Inspect(source) with { FromDiskImage = true };
    }

    /// <summary>Inspects the files of an already opened source (a folder, or the root of a disk image).</summary>
    public static GameDataReport Inspect(IAssetSource source)
    {
        ArgumentNullException.ThrowIfNull(source);
        var problems = new List<GameProblem>();
        var found = new List<string>();

        foreach (var f in ExpectedFiles.All.Where(f => f.Required))
        {
            if (!source.Exists(f.Name))
            {
                problems.Add(new(GameProblemCode.MissingFile, $"Missing file: {f.Name}", f.Name));
                continue;
            }
            if (Readable(source, f.Name)) found.Add(f.Name);
            else problems.Add(new(GameProblemCode.UnreadableFile, $"The file cannot be read or is empty: {f.Name}", f.Name));
        }

        if (found.Contains(ExpectedFiles.ArchiveName)) CheckArchive(source, problems);

        var edition = GameEdition.Unknown;
        if (found.Contains("OP.EXE") || found.Contains("REIIDEN.EXE")) edition = DetectEdition(source);
        if (problems.Count == 0 && edition == GameEdition.Unknown)
            problems.Add(new(GameProblemCode.EditionUnknown, "The language of the game texts could not be told.", null, IsError: false));

        var ordered = problems.OrderByDescending(p => p.IsError).ToList();
        return new GameDataReport(!ordered.Any(p => p.IsError), edition, ordered, found, false);
    }

    static bool Readable(IAssetSource source, string name)
    {
        try
        {
            using var s = source.Open(name);
            return s.ReadByte() >= 0;
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException) { return false; }
    }

    static void CheckArchive(IAssetSource source, List<GameProblem> problems)
    {
        byte[] bytes;
        try
        {
            using var s = source.Open(ExpectedFiles.ArchiveName);
            using var ms = new MemoryStream();
            s.CopyTo(ms);
            bytes = ms.ToArray();
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            problems.Add(new(GameProblemCode.UnreadableFile, "The data archive cannot be read.", ExpectedFiles.ArchiveName));
            return;
        }

        IReadOnlyList<PackEntry> entries;
        try { entries = PackFile.Entries(new MemoryStream(bytes)); }
        catch (InvalidDataException)
        {
            problems.Add(new(GameProblemCode.ArchiveInvalid, "The data archive is not a valid Touhou Reiiden archive.", ExpectedFiles.ArchiveName));
            return;
        }

        foreach (string name in ExpectedFiles.PackEntries)
        {
            var entry = entries.FirstOrDefault(e => e.Name.Equals(name, StringComparison.OrdinalIgnoreCase));
            if (entry is null)
            {
                problems.Add(new(GameProblemCode.ArchiveEntryMissing, $"Missing from the data archive: {name}", name));
                continue;
            }
            try { PackFile.Read(new MemoryStream(bytes), entry); }
            catch (InvalidDataException) { problems.Add(new(GameProblemCode.ArchiveEntryUnreadable, $"Damaged in the data archive: {name}", name)); }
        }
    }

    /// <summary>
    /// The English patch adds a music file (ST7.MDT) that the original disk does not have: that is what tells the editions
    /// apart. The menu text cannot, because the original menu is already fullwidth Latin letters. Without that file, readable
    /// menu texts mean the original; if the texts cannot be read the edition is unknown.
    /// </summary>
    static GameEdition DetectEdition(IAssetSource source)
    {
        if (source.Exists(TranslationMarker)) return GameEdition.Translated;
        var title = TitleTextLoader.Load(source);
        if (!ReferenceEquals(title, TitleTexts.Default)) return title.FullwidthText ? GameEdition.Japanese : GameEdition.Translated;
        return UiTextLoader.Load(source).FullwidthNumerals ? GameEdition.Japanese : GameEdition.Unknown;
    }

    const string TranslationMarker = "ST7.MDT";

    static GameDataReport Fail(GameProblemCode code, string message, string? file, bool image = false) =>
        new(false, GameEdition.Unknown, [new GameProblem(code, message, file)], [], image);
}
