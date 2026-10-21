using Th01.Core.Graphics;
using Th01.Core.Input;

namespace Th01.Core.Sim;

/// <summary>
/// Contract between gameplay (Th01.Core) and the platform. One <see cref="Step"/> = one VSync tick.
/// STABLE: change only via T1 (docs/TASKS.md).
/// </summary>
public interface IGameSession
{
    /// <summary>Advance exactly one tick. Must be a pure function of prior state + input.</summary>
    void Step(GameButtons input);

    /// <summary>Draw the current state into the 640x400 indexed framebuffer (must not mutate simulation state).</summary>
    void Render(Framebuffer target);

    /// <summary>Deterministic hash of all simulation state (see DummyState.Hash for the convention).</summary>
    ulong StateHash();

    /// <summary>
    /// Becomes non-<see cref="SessionOutcome.Running"/> once the session is over, and stays so.
    /// Default so existing implementations keep compiling.
    /// </summary>
    SessionOutcome Outcome => SessionOutcome.Running;
}
