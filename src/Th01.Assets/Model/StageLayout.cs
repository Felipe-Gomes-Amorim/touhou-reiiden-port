namespace Th01.Assets.Model;

/// <summary>Card/obstacle grid of one stage: 10 rows x 20 columns, indexed [row, column].</summary>
public sealed class StageLayout
{
    public const int Rows = 10;
    public const int Columns = 20;

    public bool[,] Cards { get; }
    public byte[,] Obstacles { get; }

    public StageLayout() : this(new bool[Rows, Columns], new byte[Rows, Columns]) { }

    public StageLayout(bool[,] cards, byte[,] obstacles)
    {
        ArgumentNullException.ThrowIfNull(cards);
        ArgumentNullException.ThrowIfNull(obstacles);
        if (cards.GetLength(0) != Rows || cards.GetLength(1) != Columns)
            throw new ArgumentException($"Cards must be [{Rows},{Columns}].", nameof(cards));
        if (obstacles.GetLength(0) != Rows || obstacles.GetLength(1) != Columns)
            throw new ArgumentException($"Obstacles must be [{Rows},{Columns}].", nameof(obstacles));
        Cards = cards; Obstacles = obstacles;
    }
}
