namespace Th01.Core.Game;

/// <summary>
/// Texts of the title screen, option menu and music test. The defaults are plain English strings; the platform
/// replaces them with the strings read from the user's OP.EXE (the asset layer does the reading, the Core never
/// references it). Strings from the original keep their padding spaces on purpose: the original centres each
/// entry by padding it to a common width, so callers must not trim them. The arrays are treated as read-only.
/// ref: th01/shiftjis/op.hpp, th01/rank.h, th01/resident.hpp, th01/shiftjis/fns.hpp
/// </summary>
public sealed record TitleTexts
{
    public const int MainChoiceCount = 4, OptionChoiceCount = 5, RankCount = 4, BgmModeCount = 2, MusicChoiceCount = 2, MusicTrackCount = 15;

    /// <summary>The "hit key" prompt of the title screen.</summary>
    public string HitKey { get; init; } = "HIT KEY";

    /// <summary>START, CONTINUE, OPTION, QUIT.</summary>
    public string[] MainChoices { get; init; } = ["START", "CONTINUE", "OPTION", "QUIT"];

    /// <summary>RANK, MUSIC, PLAYER, M.TEST (music test), QUIT.</summary>
    public string[] OptionChoices { get; init; } = ["RANK", "MUSIC", "PLAYER", "M.TEST", "QUIT"];

    /// <summary>Easy, Normal, Hard, Lunatic.</summary>
    public string[] Ranks { get; init; } = ["EASY", "NORMAL", "HARD", "LUNATIC"];

    /// <summary>Music off, music on (the original shows the driver name for the second).</summary>
    public string[] BgmModes { get; init; } = ["OFF", "ON"];

    /// <summary>The two lines of the music test header (track number, quit).</summary>
    public string[] MusicChoices { get; init; } = ["MUSIC No.", "QUIT"];

    /// <summary>Track titles of the music test, in track order.</summary>
    public string[] MusicTitles { get; init; } =
        ["TRACK 01", "TRACK 02", "TRACK 03", "TRACK 04", "TRACK 05", "TRACK 06", "TRACK 07", "TRACK 08",
         "TRACK 09", "TRACK 10", "TRACK 11", "TRACK 12", "TRACK 13", "TRACK 14", "TRACK 15"];

    /// <summary>File names (.MDT) of the music test tracks, in track order.</summary>
    public string[] MusicFiles { get; init; } =
        ["reimu.mdt", "ZIPANGU.mdt", "st0.mdt", "st1.mdt", "st2.mdt", "st3.mdt", "st4.mdt", "st5.mdt",
         "positive.mdt", "legend.mdt", "kami.mdt", "tensi.mdt", "syugen.mdt", "alice.mdt", "iris.mdt"];

    /// <summary>Message printed when the game exits.</summary>
    public string Goodbye { get; init; } = "THANK YOU FOR PLAYING";

    /// <summary>True when the texts come from the Japanese executable and use full-width characters.</summary>
    public bool FullwidthText { get; init; }

    public static TitleTexts Default { get; } = new();
}
