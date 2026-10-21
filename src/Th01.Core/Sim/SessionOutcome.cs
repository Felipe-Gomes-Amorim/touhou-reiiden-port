namespace Th01.Core.Sim;

/// <summary>
/// How a game session ended. The platform reads this to switch screens (e.g. game over -> ranking);
/// the session itself never changes screens. STABLE: change only via T1 (docs/TASKS.md).
/// </summary>
public enum SessionOutcome
{
    /// <summary>Still playing. Every session starts here.</summary>
    Running = 0,
    GameOver = 1,
    Cleared = 2,
}
