namespace Th01.Core.Diagnostics;

/// <summary>
/// Optional capability of an <c>IGameSession</c>: expose the values written to an oracle trace
/// (docs/ORACLE.md). Keys are trace column names: input_bits is filled by the recorder; the session
/// supplies player_x, player_y, orb_x, orb_y, orb_vx, orb_vy, score, rng_seed and any extra columns.
/// Missing keys are written empty and skipped by the comparer. Must not mutate simulation state.
/// </summary>
public interface ITraceSource
{
    IReadOnlyDictionary<string, long> TraceValues();
}
