using Th01.Core.Game;

namespace Th01.Audio.Playback;

/// <summary>
/// Which .MDT plays where. ref: th01/main_01.cpp (boss switch, life loop), op_01.cpp (title, music room), end/end.cpp,
/// main/boss/b20j.cpp, b20m.cpp, shiftjis/fns.hpp (MUSIC_FILES).
/// In the original the stage music is (re)started only when a scene or a boss stage begins and keeps playing across the
/// stages of a scene and across misses; <see cref="MdrvSoundSink"/> ignores a request for the track already playing, so a
/// caller can ask on every stage.
/// </summary>
public static class BgmTracks
{
    /// <summary>Title screen (title_init). Faded out when the game starts.</summary>
    public const string Title = "REIMU.MDT";
    /// <summary>Ending pictures (end_and_verdict_and_regist_animate).</summary>
    public const string Ending = "IRIS.MDT";
    /// <summary>After the ending: verdict and name entry (end_done).</summary>
    public const string AfterEnding = "ST1.MDT";
    /// <summary>Sariel's second form (b20m.cpp, once the first form is beaten and the screen changes).</summary>
    public const string SarielSecondForm = "SYUGEN.MDT";
    /// <summary>Loaded at start-up to set up the driver's voices; it holds no notes. Nothing to play.</summary>
    public const string DriverInit = "INIT.MDT";

    /// <summary>
    /// The 15 tracks of the original music room (op_01.cpp), in its order. ST6, KAMI2 are not in it; INIT is not music.
    /// </summary>
    public static readonly IReadOnlyList<string> MusicRoom =
    [
        "REIMU.MDT", "ZIPANGU.MDT", "ST0.MDT", "ST1.MDT", "ST2.MDT", "ST3.MDT", "ST4.MDT", "ST5.MDT",
        "POSITIVE.MDT", "LEGEND.MDT", "KAMI.MDT", "TENSI.MDT", "SYUGEN.MDT", "ALICE.MDT", "IRIS.MDT",
    ];

    public static bool IsBossStage(int stageId) => stageId % StageSetup.StagesPerScene == StageSetup.BossStage;

    /// <summary>
    /// Track of a stage. <paramref name="scene"/> is the scene number that also selects the stage picture and STAGEn.DAT
    /// (0 for stages 1-5, then 1/2 for stages 6-10 on the Makai/Jigoku route, and so on); route 0 = Makai, 1 = Jigoku.
    /// </summary>
    public static string ForStage(int stageId, int route, int scene)
    {
        if (!IsBossStage(stageId)) return $"ST{scene}.MDT";
        int bossScene = stageId / StageSetup.StagesPerScene;
        return bossScene switch
        {
            0 => "POSITIVE.MDT",                                  // SinGyoku
            1 => "LEGEND.MDT",                                    // YuugenMagan (Makai) and Mima (Jigoku)
            2 => route == 1 ? "KAMI.MDT" : "KAMI2.MDT",           // Kikuri (Jigoku), Elis (Makai)
            _ => route == 0 ? "TENSI.MDT" : "ALICE.MDT",          // Sariel (Makai), Konngara (Jigoku)
        };
    }
}
