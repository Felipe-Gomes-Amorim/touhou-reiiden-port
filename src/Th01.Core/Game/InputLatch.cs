using Th01.Core.Input;

namespace Th01.Core.Game;

/// <summary>
/// The original's input variables. Keys are not read as "held": shot/strike are latched flags that the game clears
/// itself, left/right are edge-updated flags, and the bomb comes from a double-tap or left+right+shot.
/// Port mapping: Left/Right = arrows, Shot = Z, Bomb = X ("strike"), Pause = Esc.
/// ref: th01/hardware/input.cpp (input_sense), th01/hardware/input.hpp
/// </summary>
public sealed class InputLatch
{
    public const byte LrNone = 0;
    public const byte LrRight = 1;
    public const byte LrLeft = 2;
    public const byte LrBoth = 3;

    public const int BombDoubletapWindow = 20;

    public byte Lr;
    public bool Shot;
    public bool Strike;
    public bool Bomb;
    public bool Paused;

    bool _prevLeft, _prevRight, _prevShot, _prevStrike, _prevPause;
    // input_prev[12] / [13]: tap counters, reset together with the other "prev" slots.
    byte _tapShot, _tapStrike;

    /// <summary>input_reset_sense(): clears everything including the edge memory.</summary>
    public void Reset()
    {
        Lr = LrNone;
        Shot = false;
        Strike = false;
        Paused = false;
        _prevLeft = _prevRight = _prevShot = _prevStrike = _prevPause = false;
        _tapShot = 0;
        _tapStrike = 0;
        Bomb = false;
    }

    /// <param name="doubletapFrame">bomb_doubletap_frame, shared with the main loop (reset by a late tap).</param>
    public void Sense(GameButtons held, ref int doubletapFrame)
    {
        bool left = (held & GameButtons.Left) != 0;
        bool right = (held & GameButtons.Right) != 0;
        bool shot = (held & GameButtons.Shot) != 0;
        bool strike = (held & GameButtons.Bomb) != 0;
        bool pause = (held & GameButtons.Pause) != 0;

        if (_prevLeft != left) { if (left) Lr |= LrLeft; else Lr &= unchecked((byte)~LrLeft); }
        _prevLeft = left;
        if (_prevRight != right) { if (right) Lr |= LrRight; else Lr &= unchecked((byte)~LrRight); }
        _prevRight = right;

        if (_prevShot != shot)
        {
            if (shot) { Shot = true; Tap(ref _tapShot, ref _tapStrike, ref doubletapFrame); }
            else Shot = false;
        }
        _prevShot = shot;

        if (_prevStrike != strike)
        {
            if (strike) { Strike = true; Tap(ref _tapStrike, ref _tapShot, ref doubletapFrame); }
            else Strike = false;
        }
        _prevStrike = strike;

        if (_prevPause != pause && pause) Paused = !Paused;
        _prevPause = pause;

        if ((_tapStrike >= 2 && _tapShot >= 2) || (Lr == LrBoth && Shot))
        {
            Bomb = true;
            _tapStrike = 0;
            _tapShot = 0;
        }
    }

    /// <summary>While the pause menu is up only the pause key is read.</summary>
    public void SensePauseOnly(GameButtons held)
    {
        bool pause = (held & GameButtons.Pause) != 0;
        if (_prevPause != pause && pause) Paused = !Paused;
        _prevPause = pause;
    }

    static void Tap(ref byte pressed, ref byte other, ref int doubletapFrame)
    {
        if (doubletapFrame < BombDoubletapWindow)
        {
            pressed++;
        }
        else
        {
            doubletapFrame = 0;
            pressed = 1;
            other = 0;
        }
    }

    public void Hash(ref StateHasher h)
    {
        h.Add(Lr); h.Add(Shot); h.Add(Strike); h.Add(Bomb); h.Add(Paused);
        h.Add(_prevLeft); h.Add(_prevRight); h.Add(_prevShot); h.Add(_prevStrike); h.Add(_prevPause);
        h.Add(_tapShot); h.Add(_tapStrike);
    }
}
