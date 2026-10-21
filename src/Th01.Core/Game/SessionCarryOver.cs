namespace Th01.Core.Game;

/// <summary>
/// State that survives from one card stage to the next (the original keeps it in globals and the resident
/// structure). Produced by <see cref="GameSession.CarryOver"/> once the stage is cleared and fed to the next
/// session through <see cref="GameOptions.CarryOver"/>. ref: th01/main_01.cpp (stage loop), resident.hpp
/// </summary>
public sealed record SessionCarryOver
{
    public uint Score { get; init; }
    public int RemLives { get; init; }
    public int RemBombs { get; init; }
    public ushort PointValue { get; init; }
    /// <summary>resident->pellet_speed after the stage-clear rule "negative becomes 0".</summary>
    public int PelletSpeed { get; init; }
    public int ExtendNext { get; init; } = 1;
    /// <summary>card_flip_cycle; drawn once at game start and never again.</summary>
    public byte CardFlipCycle { get; init; }
    /// <summary>orb_velocity_y as raw IEEE-754 bits (keeps floating point inside OrbYAxis; not reset between stages).</summary>
    public long OrbVelocityYBits { get; init; }
    /// <summary>frame_rand: seed of the next stage's irand_init.</summary>
    public uint FrameRand { get; init; }
    /// <summary>player_left; only reset to the start column on the first stage of a scene and on boss stages.</summary>
    public int PlayerLeft { get; init; } = Playfield.PlayerLeftStart;
    /// <summary>resident->bonus_per_stage (5 entries, one per stage of the scene); null = all zero.</summary>
    public uint[]? BonusPerStage { get; init; }
    /// <summary>Route chosen after the stage 5 boss (0 Makai, 1 Jigoku); the platform loads the next stages with it.</summary>
    public int RouteChosen { get; init; }
}
