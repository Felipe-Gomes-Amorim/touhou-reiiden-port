namespace Th01.Assets;

public sealed record ExpectedFile(string Name, bool Required, string Note);

/// <summary>
/// Names only (no data). The archive's Shift-JIS name is the 8.3 split of 東方靈異伝:
/// name "東方靈異", extension "伝", on disk "東方靈異.伝". docs/RECON.md section 1.
/// <see cref="All"/> is the file list of the translated (T-En xJeePx v1.00) disk image.
/// </summary>
public static class ExpectedFiles
{
    public const string ArchiveName = "東方靈異.伝";

    public static readonly IReadOnlyList<ExpectedFile> All =
    [
        new("ALICE.MDT", false, "music"),
        new("BOSS1.GRP", false, "image (PI variant)"),
        new("BOSS2.GRP", false, "image (PI variant)"),
        new("BOSS3.GRP", false, "image (PI variant)"),
        new("BOSS4.GRP", false, "image (PI variant)"),
        new("BOSS5.GRP", false, "image (PI variant)"),
        new("BOSS6_A.GRP", false, "image (PI variant)"),
        new("BOSS6_A1.GRP", false, "image (PI variant)"),
        new("BOSS6_A2.GRP", false, "image (PI variant)"),
        new("BOSS6_A3.GRP", false, "image (PI variant)"),
        new("BOSS6_A4.GRP", false, "image (PI variant)"),
        new("BOSS6_A5.GRP", false, "image (PI variant)"),
        new("BOSS6_A6.GRP", false, "image (PI variant)"),
        new("BOSS6_H.GRP", false, "image (PI variant)"),
        new("BOSS6_L.GRP", false, "image (PI variant)"),
        new("BOSS7_D1.GRP", false, "image (PI variant)"),
        new("BOSS8.GRZ", false, "image (RLE + alpha)"),
        new("BOSS8_A1.GRP", false, "image (PI variant)"),
        new("BOSS8_D1.GRP", false, "image (PI variant)"),
        new("BOSS8_D2.GRP", false, "image (PI variant)"),
        new("BOSS8_D3.GRP", false, "image (PI variant)"),
        new("BOSS8_D4.GRP", false, "image (PI variant)"),
        new("CLEAR3.GRP", false, "image (PI variant)"),
        new("ED1A.GRP", false, "image (PI variant)"),
        new("ED1B.GRP", false, "image (PI variant)"),
        new("ED1C.GRP", false, "image (PI variant)"),
        new("ED1D.GRP", false, "image (PI variant)"),
        new("ED1E.GRP", false, "image (PI variant)"),
        new("ED2A.GRP", false, "image (PI variant)"),
        new("ED3A.GRP", false, "image (PI variant)"),
        new("ED3B.GRP", false, "image (PI variant)"),
        new("ED4A.GRP", false, "image (PI variant)"),
        new("ED5A.GRP", false, "image (PI variant)"),
        new("ED5B.GRP", false, "image (PI variant)"),
        new("ED5C.GRP", false, "image (PI variant)"),
        new("ENDB_A.GRP", false, "image (PI variant)"),
        new("ENDB_B.GRP", false, "image (PI variant)"),
        new("ENDM_A.GRP", false, "image (PI variant)"),
        new("FUUIN.EXE", false, "executable"),
        new("GAME_O.GRP", false, "image (PI variant)"),
        new("INIT.MDT", false, "music"),
        new("IRIS.MDT", false, "music"),
        new("KAMI.MDT", false, "music"),
        new("KAMI2.MDT", false, "music"),
        new("LEGEND.MDT", false, "music"),
        new("MASK.GRF", false, "mask image"),
        new("MDRV98.COM", false, "sound driver (not used by the port)"),
        new("OP.EXE", true, "executable"),
        new("OP_WIN.GRP", false, "image (PI variant)"),
        new("POSITIVE.MDT", false, "music"),
        new("REIIDEN.CFG", false, "options (not on the disk: the game creates it on first run)"),
        new("REIIDEN.EXE", true, "executable"),
        new("REIIDEN2.GRP", false, "image (PI variant)"),
        new("REIIDEN3.GRP", false, "image (PI variant)"),
        new("REIMU.MDT", false, "music"),
        new("REYHINO.DAT", false, "high scores (not on the disk: the game creates it on first run)"),
        new("REYHILU.DAT", false, "high scores (Lunatic rank; the disk ships it, the game keeps one REYHI<rank>.DAT per rank)"),
        new("SHUGEN.MDT", false, "music"),
        new("ST0.GRP", true, "image (PI variant)"),
        new("ST0.MDT", false, "music"),
        new("ST1.GRP", false, "image (PI variant)"),
        new("ST1.MDT", false, "music"),
        new("ST2.GRP", false, "image (PI variant)"),
        new("ST2.MDT", false, "music"),
        new("ST3.GRP", false, "image (PI variant)"),
        new("ST3.MDT", false, "music"),
        new("ST4.GRP", false, "image (PI variant)"),
        new("ST4.MDT", false, "music"),
        new("ST5.GRP", false, "image (PI variant)"),
        new("ST5.MDT", false, "music"),
        new("ST6.GRP", false, "image (PI variant)"),
        new("ST6.MDT", false, "music"),
        new("ST7.MDT", false, "music (only on the translated copy)"),
        new("SYUGEN.MDT", false, "music"),
        new("TENSI.MDT", false, "music"),
        new("TOUHOU.DAT", false, "data"),
        new("ZIGOKU.MDE", false, "music/SE"),
        new("ZIPANGU.MDT", false, "music"),
        new("ZUNSOFT.COM", false, "executable"),
        new("東方靈異.伝", true, "data archive"),
    ];

    /// <summary>
    /// Story-screen images (ending, game over, stage clear, title window) and the executables whose data segment
    /// holds the text shown with them. There are no separate script files.
    /// </summary>
    public static readonly IReadOnlyList<string> CutsceneImages =
    [
        "CLEAR3.GRP", "ED1A.GRP", "ED1B.GRP", "ED1C.GRP", "ED1D.GRP", "ED1E.GRP", "ED2A.GRP", "ED3A.GRP", "ED3B.GRP",
        "ED4A.GRP", "ED5A.GRP", "ED5B.GRP", "ED5C.GRP", "ENDB_A.GRP", "ENDB_B.GRP", "ENDM_A.GRP", "GAME_O.GRP",
        "OP_WIN.GRP", "REIIDEN2.GRP", "REIIDEN3.GRP",
    ];

    public static readonly IReadOnlyList<string> TextExecutables = ["REIIDEN.EXE", "OP.EXE", "FUUIN.EXE"];

    /// <summary>Files inside the archive (names as stored; read them with PackFile).</summary>
    public static readonly IReadOnlyList<string> PackEntries =
    [
        "STAGE0.DAT",
        "STAGE1.DAT",
        "STAGE2.DAT",
        "STAGE3.DAT",
        "STAGE4.DAT",
        "STAGE5.DAT",
        "STAGE6.DAT",
        "STAGE7.DAT",
        "BOSS1.BOS",
        "BOSS1_2.BOS",
        "MIKO_AC.BOS",
        "MIKO_AC2.BOS",
        "BOSS1_3.BOS",
        "BOSS2.BOS",
        "BOSS3.BOS",
        "BOSS3_1.BOS",
        "BOSS3_2.BOS",
        "BOSS5.BOS",
        "BOSS5_2.BOS",
        "BOSS5_3.BOS",
        "BOSS6_1.BOS",
        "BOSS6_2.BOS",
        "BOSS6_3.BOS",
        "BOSS8_1.BOS",
        "BOSS8_E1.BOS",
        "BOSS8_E2.BOS",
        "TAMASII.BOS",
        "TAMASII2.BOS",
        "BOSS5_GR.GRC",
        "BOSS6GR1.GRC",
        "BOSS6GR2.GRC",
        "BOSS6GR3.GRC",
        "BOSS6GR4.GRC",
        "KUZI1.GRC",
        "KUZI2.GRC",
        "BOSS3_M.PTN",
        "MIKO.PTN",
        "NUMB.PTN",
        "STG.PTN",
        "STG_B.PTN",
        "TAMAYEN.PTN",
    ];
}
