namespace Th01.Core.Game;

/// <summary>How the bomb's palette flash looks. Drawing only: the simulation never sees it.</summary>
public enum BombFlash
{
    /// <summary>No palette flash during the second part of the bomb (the default; easier on the eyes).</summary>
    Softened,
    /// <summary>The original's flash between a pale-blue peak palette and a brightening one (frames 51..139).</summary>
    Original,
}

/// <summary>Drawing options chosen by the user. None of them changes the simulation or its hash.</summary>
public sealed record VisualOptions
{
    public BombFlash BombFlash { get; init; } = BombFlash.Softened;
    /// <summary>false = the vertical shake of the bomb (frames 52..139) is not applied.</summary>
    public bool ScreenShake { get; init; } = true;
    /// <summary>
    /// true (the default) = no flashing lights: the boss hit flash is a light, steady tint instead of white blinking every
    /// other frame, and the boss defeat has no blinking white lines and only brightens the palette moderately (slower).
    /// false = the original white flashes. Durations never change.
    /// </summary>
    public bool SoftFlashes { get; init; } = true;
}
