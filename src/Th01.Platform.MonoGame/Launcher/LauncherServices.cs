using System.Diagnostics;
using Th01.Core.Audio;

namespace Th01.Platform.MonoGame.Launcher;

/// <summary>Counts behind the "last report" line.</summary>
public sealed record ReportSummary(int MissingArt, bool CrashLog)
{
    public static readonly ReportSummary Empty = new(0, false);
    public bool IsEmpty => MissingArt == 0 && !CrashLog;

    /// <summary>Summary of report lines as <see cref="LauncherServices.ReportLines"/> makes them.</summary>
    public static ReportSummary FromLines(IEnumerable<string> lines)
    {
        int art = 0; bool crash = false;
        foreach (string l in lines)
        {
            if (l.StartsWith(LauncherServices.ArtPrefix, StringComparison.Ordinal)) art++;
            else if (l.StartsWith(LauncherServices.CrashPrefix, StringComparison.Ordinal)) crash = true;
        }
        return new ReportSummary(art, crash);
    }
}

/// <summary>The real <see cref="ILauncherServices"/>: file explorer, report files in the user's data folder, file dialogs, a preview sound.</summary>
public sealed class LauncherServices(string userDir, MissingArtLog missingArt, IUserPrompt prompt, Func<FontRomState>? fontRomState = null, ISoundSink? sound = null) : ILauncherServices
{
    public const string CrashPrefix = "crash: ", ArtPrefix = "missing art: ";
    /// <summary>The "item picked up" effect: short and pleasant, the one played to hear a volume change.</summary>
    public const int PreviewEffectId = 15;

    IReadOnlyList<string>? _report;

    /// <summary>Set by the window every frame.</summary>
    public bool GamepadConnected { get; set; }
    public string? GamepadName { get; set; }

    public FontRomState FontRomStatus => fontRomState?.Invoke() ?? FontRomState.NotFound;

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

    /// <summary>Reads the report files again the next time they are needed (the rows ask often; the files are read once per refresh).</summary>
    public void RefreshReport() => _report = null;

    /// <summary>The crash log of the last run, the missing-art list saved by the last run, and what this run has noted so far.</summary>
    public IReadOnlyList<string> ReportLines() => _report ??= BuildReport();

    public ReportSummary Summary() => Launcher.ReportSummary.FromLines(ReportLines());

    IReadOnlyList<string> BuildReport()
    {
        var lines = new List<string>();
        string crash = Path.Combine(userDir, "crash.local.log");
        try
        {
            if (File.Exists(crash))
                lines.AddRange(File.ReadLines(crash).Take(8).Select(l => CrashPrefix + l));
            string art = Path.Combine(userDir, MissingArtLog.FileName);
            if (File.Exists(art))
                lines.AddRange(File.ReadLines(art).Select(l => ArtPrefix + l));
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException) { /* the report just stays shorter */ }
        foreach (string l in missingArt.Lines)
            if (!lines.Contains(ArtPrefix + l)) lines.Add(ArtPrefix + l);
        return lines;
    }

    public void PreviewEffect() => sound?.PlaySe(PreviewEffectId);

    public string? PickFontRom() => prompt.PickFontRom();
}
