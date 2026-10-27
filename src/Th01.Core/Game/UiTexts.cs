namespace Th01.Core.Game;

/// <summary>
/// The labels of the route menu and of the stage bonus box. The platform reads the real ones from the user's own
/// REIIDEN.EXE (so a Japanese copy shows Japanese, a translated copy its translation); these defaults are used when
/// they cannot be found, and are our own plain English wording.
/// </summary>
public sealed record UiTexts
{
    public static readonly UiTexts Default = new();

    // Route menu after the stage 5 boss.
    public string RouteRound { get; init; } = "ROUND 1 CLEAR";
    public string RouteCongrats { get; init; } = "Congratulation!!";
    public string RouteSelect { get; init; } = "Select next Round";
    public string RouteMakai { get; init; } = "Makai";
    public string RouteJigoku { get; init; } = "Hell";

    // Stage bonus box. The stage number goes into the two characters at <see cref="BonusTitleDigitsAt"/> of the title.
    public string BonusTitle { get; init; } = "STAGE      COMPLETE";
    public string BonusSubtitle { get; init; } = "  BONUS";
    public string BonusTime { get; init; } = "Time";
    public string BonusCardCombo { get; init; } = "Continuous";
    public string BonusResources { get; init; } = "Bomb&Player";
    public string BonusStage { get; init; } = "  STAGE";
    public string BonusTotal { get; init; } = "BONUS Point";
    public string BonusHitKey { get; init; } = "Hit Z Key";

    /// <summary>True when the numbers are drawn with fullwidth numerals (as the original does) instead of plain digits.</summary>
    public bool FullwidthNumerals { get; init; }

    /// <summary>Index in <see cref="BonusTitle"/> of the first of the two characters that the stage number replaces.</summary>
    public const int BonusTitleDigitsAt = 6;
}
