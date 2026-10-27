using System.Diagnostics;

namespace Th01.Platform.MonoGame.Launcher;

/// <summary>The real <see cref="ILauncherServices"/>: file explorer, report files in the user's data folder, file dialogs.</summary>
public sealed class LauncherServices(string userDir, MissingArtLog missingArt, IUserPrompt prompt, Func<bool>? fontRomFound = null) : ILauncherServices
{
    /// <summary>Set by the window every frame.</summary>
    public bool GamepadConnected { get; set; }

    public bool FontRomFound => fontRomFound?.Invoke() ?? false;

    public void OpenDataFolder()
    {
        try
        {
            Directory.CreateDirectory(userDir);
            Process.Start(new ProcessStartInfo(userDir) { UseShellExecute = true });
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or System.ComponentModel.Win32Exception or InvalidOperationException)
        {
            Console.Error.WriteLine($"Could not open {userDir}: {e.Message}");
        }
    }

    /// <summary>The crash log of the last run, the missing-art list saved by the last run, and what this run has noted so far.</summary>
    public IReadOnlyList<string> ReportLines()
    {
        var lines = new List<string>();
        string crash = Path.Combine(userDir, "crash.local.log");
        try
        {
            if (File.Exists(crash))
                lines.AddRange(File.ReadLines(crash).Take(8).Select(l => "crash: " + l));
            string art = Path.Combine(userDir, MissingArtLog.FileName);
            if (File.Exists(art))
                lines.AddRange(File.ReadLines(art).Select(l => "missing art: " + l));
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException) { /* the report just stays shorter */ }
        foreach (string l in missingArt.Lines)
            if (!lines.Contains("missing art: " + l)) lines.Add("missing art: " + l);
        return lines;
    }

    public string? PickFontRom() => prompt.PickFontRom();
}
