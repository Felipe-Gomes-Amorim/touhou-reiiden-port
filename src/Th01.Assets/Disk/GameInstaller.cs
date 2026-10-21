namespace Th01.Assets.Disk;

/// <summary>Raised with a message that is safe to show to the user.</summary>
public sealed class GameFileException(string message) : Exception(message);

/// <summary>
/// Turns what the user picked (a .hdi disk image, a folder, or any file inside a game folder) into a folder
/// of game files the port can read. Everything stays on the user's machine.
/// </summary>
public static class GameInstaller
{
    // DOS boot files in the image root; not game data.
    static readonly string[] BootFiles = ["IO.SYS", "MSDOS.SYS", "COMMAND.COM", "AUTOEXEC.BAT", "GAME.BAT"];
    const long MaxImageBytes = 512L * 1024 * 1024;

    /// <summary>Names of required game files missing from <paramref name="folder"/> (empty = looks like a valid copy).</summary>
    public static IReadOnlyList<string> MissingRequired(string folder)
    {
        var src = new FolderAssetSource(folder);
        return ExpectedFiles.All.Where(f => f.Required && !src.Exists(f.Name)).Select(f => f.Name).ToList();
    }

    /// <summary>Extracts the game files of the image root into <paramref name="outDir"/>. Returns the number of files written.</summary>
    public static int ExtractRoot(HdiImage image, string outDir, bool all = false)
    {
        Directory.CreateDirectory(outDir);
        int count = 0;
        foreach (var e in image.Enumerate())
        {
            if (e.IsDirectory) continue;
            if (!all && (e.Path.Contains('/') || BootFiles.Contains(e.Path, StringComparer.OrdinalIgnoreCase))) continue;
            string dest = Path.Combine(outDir, e.Path.Replace('/', Path.DirectorySeparatorChar));
            Directory.CreateDirectory(Path.GetDirectoryName(dest)!);
            File.WriteAllBytes(dest, image.ReadFile(e));
            count++;
        }
        return count;
    }

    /// <summary>
    /// Returns a folder with the game files for <paramref name="selected"/>. A disk image is extracted into
    /// <paramref name="cacheDir"/>/data (replacing any previous extraction); a folder is used where it is.
    /// </summary>
    public static string Prepare(string selected, string cacheDir)
    {
        if (Directory.Exists(selected)) return CheckFolder(selected);
        if (!File.Exists(selected)) throw new GameFileException($"The file or folder was not found:\n{selected}");

        if (!selected.EndsWith(".hdi", StringComparison.OrdinalIgnoreCase))
        {
            // Any other file: treat its folder as the game folder (e.g. the user picked REIIDEN.EXE).
            return CheckFolder(Path.GetDirectoryName(Path.GetFullPath(selected))!);
        }

        HdiImage image;
        try
        {
            var info = new FileInfo(selected);
            if (info.Length > MaxImageBytes) throw new GameFileException("That file is too large to be a Touhou Reiiden disk image.");
            image = new HdiImage(File.ReadAllBytes(selected));
        }
        catch (InvalidDataException e)
        {
            throw new GameFileException(
                "This disk image could not be read. Only Anex86 .hdi images with a FAT12 partition are supported.\n(" + e.Message + ")");
        }

        string tmp = Path.Combine(cacheDir, "data.tmp");
        string final = Path.Combine(cacheDir, "data");
        try
        {
            if (Directory.Exists(tmp)) Directory.Delete(tmp, true);
            ExtractRoot(image, tmp);
            var missing = MissingRequired(tmp);
            if (missing.Count > 0)
                throw new GameFileException("This disk image does not look like Touhou Reiiden. Missing: " + string.Join(", ", missing));
            if (Directory.Exists(final)) Directory.Delete(final, true);
            Directory.Move(tmp, final);
        }
        catch (InvalidDataException e)
        {
            throw new GameFileException("The disk image is damaged: " + e.Message);
        }
        finally
        {
            if (Directory.Exists(tmp)) Directory.Delete(tmp, true);
        }
        return final;
    }

    static string CheckFolder(string folder)
    {
        var missing = MissingRequired(folder);
        if (missing.Count > 0)
            throw new GameFileException("That folder does not contain the game files. Missing: " + string.Join(", ", missing));
        return folder;
    }
}
