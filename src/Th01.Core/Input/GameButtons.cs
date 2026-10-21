namespace Th01.Core.Input;

/// <summary>Logical inputs, independent of keyboard/gamepad. One bit per button.</summary>
[Flags]
public enum GameButtons : ushort
{
    None = 0,
    Up = 1 << 0,
    Down = 1 << 1,
    Left = 1 << 2,
    Right = 1 << 3,
    Shot = 1 << 4,
    Bomb = 1 << 5,   // "strike" in the original; exact semantics TBD in RECON
    Pause = 1 << 6,
    Ok = 1 << 7,
    Cancel = 1 << 8,
}
