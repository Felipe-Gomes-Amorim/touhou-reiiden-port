using Th01.Core.Game;
using Th01.Core.Input;
using Th01.Platform.MonoGame;

namespace Th01.Platform.Tests;

/// <summary>A stage provider that needs no game files (one card, no art), and a way to make the player die.</summary>
public sealed class FlowFakeStages : IStageProvider
{
    public int Lives;
    public readonly List<(int Stage, int Route)> Requests = new();

    public GameStart Create(int stageId, int route, Func<StageSetup, GameOptions> makeOptions)
    {
        var cards = new bool[StageSetup.Rows, StageSetup.Cols];
        cards[9, 19] = true;
        var setup = new StageSetup { StageId = stageId, Route = route, Cards = cards, Obstacles = new byte[StageSetup.Rows, StageSetup.Cols] };
        var o = makeOptions(setup) with { Lives = Lives, Score = 12345 };
        Requests.Add((stageId, route));
        return new GameStart(new GameSession(o), Th01.Core.Graphics.NullGameArt.Instance);
    }

    public static void Die(GameFlow f)
    {
        for (int i = 0; i < EntranceOverlay.TotalTicks(true) + 5; i++) f.Tick(GameButtons.None);
        f.Tick(GameButtons.Shot); f.Tick(GameButtons.None);
        var w = f.Game!.World;
        w.Orb.Left = w.Player.Left; w.Orb.Top = Playfield.PlayerTop - 8;
        w.Orb.Y.Force = 0; w.Orb.ForceFrame = 0;
        f.Tick(GameButtons.None);
    }
}
