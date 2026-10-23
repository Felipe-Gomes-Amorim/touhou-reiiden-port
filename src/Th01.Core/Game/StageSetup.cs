namespace Th01.Core.Game;

/// <summary>
/// Stage layout as Th01.Core wants it (Core must not reference Th01.Assets). The platform converts from
/// <c>Assets.Model.StageLayout</c>. ref: th01/formats/stagedat.hpp
/// </summary>
public sealed class StageSetup
{
    public const int Cols = 20;
    public const int Rows = 10;
    public const int StagesPerScene = 5;
    public const int BossStage = 4;
    /// <summary>0-based stage id of Mima, the Jigoku boss of stage 10.</summary>
    public const int MimaStage = 9;
    public const int RouteMakai = 0, RouteJigoku = 1;

    /// <summary>0-based stage id across the whole game (stage_num - 1).</summary>
    public int StageId { get; init; }

    /// <summary>0 = Makai, 1 = Jigoku (selects the timer table and later the bosses).</summary>
    public int Route { get; init; }

    /// <summary>[row, col] true = a card tile.</summary>
    public required bool[,] Cards { get; init; }

    /// <summary>[row, col] raw obstacle bytes. 14..16 store the HP of the card in the same tile; do not normalize.</summary>
    public required byte[,] Obstacles { get; init; }

    public bool IsBoss => StageId % StagesPerScene == BossStage;

    public void Validate()
    {
        if (Cards.GetLength(0) != Rows || Cards.GetLength(1) != Cols)
            throw new ArgumentException("Cards must be [10,20]");
        if (Obstacles.GetLength(0) != Rows || Obstacles.GetLength(1) != Cols)
            throw new ArgumentException("Obstacles must be [10,20]");
    }
}

/// <summary>obstacle_type_t. ref: th01/formats/stagedat.hpp</summary>
public static class ObstacleType
{
    public const int None = 0;
    public const int Bumper = 1;
    public const int TurretFirst = 2;
    public const int TurretLast = 13;
    public const int TurretQuickFirst = 8;
    public const int Card2Flip = 14;
    public const int Card3Flip = 15;
    public const int Card4Flip = 16;
    public const int Portal = 17;
    public const int BarTop = 18;
    public const int BarBottom = 19;
    public const int BarLeft = 20;
    public const int BarRight = 21;

    /// <summary>Types that become obstacle slots (bumpers, turrets, portals, bars).</summary>
    public static bool IsActual(int t) => t is >= Bumper and <= TurretLast or >= Portal and <= BarRight;
}
