namespace Th01.Core.Game;

/// <summary>What the player sprite on VRAM currently is, as left behind by the original's unput/put calls.</summary>
public enum PlayerSpriteKind : byte
{
    None,       // nothing drawn (the original has several one-frame "invisible Reimu" bugs)
    Ptn32,      // miko.ptn cel, 32x32 at (player_left, player_top)
    Bos48x48,   // miko_ac.bos cel at (player_left-8, 352)
    Bos48x32,   // miko_ac2.bos cel at (player_left-8, player_top)
}

/// <summary>PTN ids used by the player (slot*64+image). ref: th01/sprites/main_ptn.h</summary>
public static class PtnId
{
    public const int Orb = 3;
    public const int Shot = 1;
    public const int MikoL = 64;
    public const int MikoLDash = 65;      // 2 cels
    public const int MikoLCast = 67;
    public const int MikoLDashShoot = 68; // 2 cels
    public const int MikoR = 74;
    public const int MikoRDash = 75;      // 2 cels
    public const int MikoRCast = 77;
    public const int MikoRDashShoot = 78; // 2 cels
    public const int MissEffect = 84;     // 2 cels
    public const int MikoMiss = 86;
    public const int MikoMissAlternate = 87;
}

/// <summary>
/// Player: movement, strikes, slides, special combos, shots and bomb start. A direct behavioural port of
/// player_unput_update_render() with all drawing replaced by <see cref="Sprite"/> bookkeeping.
/// ref: th01/main/player/player.cpp
/// </summary>
public sealed class Player
{
    // Durations
    const int DashFramesPerCel = 4;
    const int DashCels = 2;
    const int DashFrames = DashFramesPerCel * DashCels;
    const int SlideFrames = 12;
    const int SwingFramesPerCel = 3;
    const int SwingFrames = 23;
    const int ShotComboFramesPerCel = 6;
    const int ShotComboFrames = 19;
    const int SwingCels = 8;
    const int FlipkickCels = 6;
    const int ShotComboCels = 4;
    const int SlideCels = 2;

    // miko_ac.bos (48x48) cel bases
    public const int CSwing = 0;
    const int CSwingLast = CSwing + SwingCels - 1;
    public const int CLFlipkick = 8;
    public const int CRFlipkick = CLFlipkick + FlipkickCels;
    public const int CLShotCombo = CRFlipkick + FlipkickCels;
    public const int CRShotCombo = CLShotCombo + ShotComboCels;
    // miko_ac2.bos (48x32) cel bases
    public const int CLSlide = 0;
    const int CLSlideLast = CLSlide + SlideCels - 1;
    public const int CRSlide = CLSlide + SlideCels;
    const int CRSlideLast = CRSlide + SlideCels - 1;
    public const int CLSlidekick = CRSlide + SlideCels;
    public const int CRSlidekick = CLSlidekick + 1;

    // mode_t
    const int MRegular = 0, MShoot = 1, MSwingOrSlide = 2, MSpecialFirst = 3, MSpecialSecond = 4, MSpecialThird = 5;
    // submode_direction_t == input_lr values
    const int SdStationary = 0, SdRight = 1, SdLeft = 2, SdNoOrbRepelThenStationaryBug = 3;
    // submode_special_t, OR'd with x_direction_t
    const int SsFlipkickMoving = 0, SsFlipkickStationary = 2, SsSlidekick = 4, SsShotCombo = 6;
    const int XRight = 0, XLeft = 1;
    // bomb_flag_t
    const int BfNone = 0, BfStart = 2, BfActive = 3;

    readonly GameWorld _w;

    // Shared state (globals in the original)
    public int Left = Playfield.PlayerLeftStart;
    public static int Top => Playfield.PlayerTop;
    bool _invincibleAgainstOrb;

    // Function-static state of player_unput_update_render()
    int _dashCycle;
    int _modeFrame;
    int _mode = MRegular;
    int _dashDirection = XLeft;
    int _bombFlag = BfNone;
    bool _bombing;
    bool _comboEnabled;
    int _swingDeflectionFrames;
    int _submode;

    // What is drawn
    public PlayerSpriteKind SpriteKind { get; private set; } = PlayerSpriteKind.None;
    public int SpriteCel { get; private set; }
    public int SpriteLeft { get; private set; }
    /// <summary>A bomb is running (drawing uses it for the casting pose and the effects).</summary>
    public bool Bombing => _bombing;

    public Player(GameWorld w) => _w = w;

    public int CenterX => Left + Playfield.PlayerW / 2;

    // Drawing bookkeeping
    void Unput() => SpriteKind = PlayerSpriteKind.None;
    void PutPtn(int cel) { SpriteKind = PlayerSpriteKind.Ptn32; SpriteCel = cel; SpriteLeft = Left; }
    void Put48(int cel) { SpriteKind = PlayerSpriteKind.Bos48x48; SpriteCel = cel; SpriteLeft = Left - 8; }
    void Put32(int cel) { SpriteKind = PlayerSpriteKind.Bos48x32; SpriteCel = cel; SpriteLeft = Left - 8; }

    public void MoveAndClamp(int delta)
    {
        Left += delta;
        if (Left < Playfield.PlayerLeftMin) Left = Playfield.PlayerLeftMin;
        else if (Left >= Playfield.PlayerLeftMax) Left = Playfield.PlayerLeftMax;
    }

    void ShotsAddCentered(int dx = 0) => _w.Shots.Add(Left + Playfield.PlayerW / 4 + dx, Top);

    int DashCel(int baseCel) => _dashCycle % DashFrames < DashFramesPerCel ? baseCel : baseCel + 1;
    static int ModeDashCel(int v, int baseCel) => baseCel + (v & 3);

    // Cel selection (ModeFrame in the original)
    static int SwingCel(int v, int off) => CSwing + ((v + off >= SwingFrames ? CSwingLast * SwingFramesPerCel : v + off) / SwingFramesPerCel);
    static int FlipkickCel(int v, int off, int baseCel) => v + off < 4 ? baseCel : (v + off - 7) / 4 + baseCel;
    static int ShotComboCel(int v, int off, int baseCel) => (v + off) / ShotComboFramesPerCel + baseCel;
    static int SlideCelCur(int v, int baseCel) => baseCel + v % SlideCels;
    static int SlideCelPrev(int v, int baseCel) => baseCel + (SlideCels - 1 - v % SlideCels);

    /// <summary>player_reset(): the original calls the update function with its "do not reset" parameter false.</summary>
    public void Reset()
    {
        _dashCycle = 0;
        _modeFrame = 0;
        _mode = MRegular;
        _submode = -1;
        _dashDirection = XLeft;
        _bombFlag = BfNone;
        _w.Bomb.Damaging = false;
        _w.PlayerSliding = false;
        _w.PlayerDeflecting = false;
        _swingDeflectionFrames = _w.Rank switch
        {
            Rank.Easy => 15,
            Rank.Normal => 12,
            Rank.Hard => 10,
            Rank.Lunatic => 8,
            _ => 12,
        };
        _bombing = false;
        _comboEnabled = false;
        _w.Input.Bomb = false;
        _invincibleAgainstOrb = false;
        _prevPtn = PtnId.MikoL;
    }

    int _prevPtn = PtnId.MikoL;

    /// <summary>The default sprite blit the stage loop does at the start of a life.</summary>
    public void PutDefault() => PutPtn(PtnId.MikoL);

    /// <summary>The miss pose: the original draws the alternate cel with a 1-in-8 irand() roll.</summary>
    public void PutMiss() =>
        PutPtn(PtnId.MikoMiss + (_w.Rng.Next() % 8 == 0 ? PtnId.MikoMissAlternate - PtnId.MikoMiss : 0));

    // special_start / special_start_strike / special_start_shot
    void SpecialStart(bool deflect)
    {
        if (deflect) _w.PlayerDeflecting = true;
        _w.Sound.PlaySe(11);
        _comboEnabled = false;
    }

    void SpecialCombo(int dir, int same, int other)
    {
        int lr = _w.Input.Lr;
        _submode = lr == other ? SsFlipkickStationary | dir
                 : lr == same ? SsSlidekick | dir
                 : SsFlipkickMoving | dir;
    }

    void SpecialStartStrike(int dir)
    {
        SpecialCombo(dir, dir == XRight ? InputLatch.LrRight : InputLatch.LrLeft,
                          dir == XRight ? InputLatch.LrLeft : InputLatch.LrRight);
        _mode = MSpecialFirst;
        SpecialStart(_submode < SsSlidekick);
    }

    void SpecialStartShot(int dir)
    {
        _submode = SsShotCombo | dir;
        _mode = MSpecialFirst;
        SpecialStart(true);
    }

    bool IsFlipkick(int dir) => _submode == (SsFlipkickStationary | dir) || _submode == (SsFlipkickMoving | dir);

    void SlideStop()
    {
        _mode = MRegular;
        Unput();
        _submode = SdNoOrbRepelThenStationaryBug;
        _modeFrame = 0;
        _w.PlayerSliding = false;
    }

    /// <summary>One tick of player logic. Does NOT run the plain player/Orb hit test (that happens in the orb step).</summary>
    public void Update()
    {
        var inp = _w.Input;
        _dashCycle = (_dashCycle + 1) & (DashFrames - 1);

        if ((_w.RemBombs != 0 || _bombing) && _bombFlag >= BfStart)
        {
            if (_bombFlag == BfStart)
            {
                _w.BombFrame = 0;
                _bombFlag = BfActive;
                _w.PlayerDeflecting = true;
                _bombing = true;
                _w.RemBombs--;
            }
            OrbPlayerHitTest(1);
            bool bombDone = _w.Bomb.UpdateFrame(_w, _w.BombFrame);
            _bombFlag = bombDone ? BfNone : BfActive;
            if (bombDone)
            {
                _bombing = false;
                inp.Bomb = false;
                PutPtn(PtnId.MikoL);
                _prevPtn = PtnId.MikoL;
            }
            else if (_w.BombFrame > 60)
            {
                PutPtn(PtnId.MikoLCast);
            }
            inp.Shot = false;
            inp.Strike = false;
            _mode = MRegular;
            if (_bombFlag == BfNone) _w.PlayerDeflecting = false;
        }
        // "Yes, not `< M_SPECIAL_FIRST`."
        else if (inp.Bomb && _mode != MSpecialFirst && _w.RemBombs != 0)
        {
            _bombFlag = BfStart;
            inp.Bomb = false;
            _w.Pellets.SpeedRaise(1);   // pellet_speed_raise(0.025f)
        }
        else if (_mode == MRegular)
        {
            UpdateRegular();
        }
        else if (_mode == MShoot)
        {
            UpdateShoot();
        }
        else if (_mode == MSwingOrSlide)
        {
            UpdateSwingOrSlide();
        }
        else
        {
            UpdateSpecial();
        }
    }

    void UpdateRegular()
    {
        var inp = _w.Input;
        switch (inp.Lr)
        {
            case InputLatch.LrRight:
                _dashDirection = XRight;
                Unput();
                MoveAndClamp(+4);
                PutPtn(DashCel(PtnId.MikoRDash));
                _prevPtn = DashCel(PtnId.MikoRDash);
                _submode = SdRight;
                break;
            case InputLatch.LrLeft:
                _dashDirection = XLeft;
                Unput();
                MoveAndClamp(-4);
                PutPtn(DashCel(PtnId.MikoLDash));
                _prevPtn = DashCel(PtnId.MikoLDash);
                _submode = SdLeft;
                break;
            default:
                // Quirk: after the bug value the sprite is not redrawn this frame.
                if (_submode == SdStationary)
                {
                    int cel = _dashDirection != XRight ? PtnId.MikoL : PtnId.MikoR;
                    PutPtn(cel);
                    _prevPtn = cel;
                }
                _submode = SdStationary;
                break;
        }

        if (inp.Shot)
        {
            Unput();
            _mode = MShoot;
            _modeFrame = 0;
            _submode = inp.Lr;
            if (_submode == InputLatch.LrBoth) _submode = SdStationary;
            if (_submode == SdStationary)
            {
                int cel = _dashDirection != XRight ? PtnId.MikoLCast : PtnId.MikoRCast;
                PutPtn(cel);
                _prevPtn = cel;
            }
            else if (_submode == SdRight)
            {
                PutPtn(PtnId.MikoRDashShoot);
                _prevPtn = PtnId.MikoRDashShoot;
            }
            else if (_submode == SdLeft)
            {
                PutPtn(PtnId.MikoLDashShoot);
                _prevPtn = PtnId.MikoLDashShoot;
            }
        }
        if (inp.Strike)
        {
            Unput();
            if (inp.Lr == InputLatch.LrNone || inp.Lr == InputLatch.LrBoth)
            {
                _modeFrame = 0;
                _mode = MSwingOrSlide;
                _submode = SdStationary;
                _w.PlayerDeflecting = true;
                // Quirk: no blit for the rest of the function, Reimu is invisible for one frame.
            }
            else
            {
                _w.Sound.PlaySe(11);
                _modeFrame = 0;
                _mode = MSwingOrSlide;
                _submode = inp.Lr;
                _w.PlayerSliding = true;
                _comboEnabled = false;
                Put32(_submode == SdRight ? CRSlideLast : CLSlideLast);
            }
            _invincibleAgainstOrb = true;
        }
    }

    void UpdateShoot()
    {
        var inp = _w.Input;
        if (_submode == SdStationary)
        {
            if (_modeFrame == 0)
            {
                ShotsAddCentered();
            }
            else if (_modeFrame >= 2)
            {
                _w.BombFrame = 0;
                _mode = MRegular;
                inp.Shot = false;
                _submode = SdNoOrbRepelThenStationaryBug;
            }
        }
        else if (_submode == SdRight)
        {
            _dashDirection = XRight;
            Unput();
            MoveAndClamp(+4);
            PutPtn(ModeDashCel(_modeFrame, PtnId.MikoRDashShoot));
            _prevPtn = ModeDashCel(_modeFrame, PtnId.MikoRDashShoot);
            if (_modeFrame >= 1)
            {
                ShotsAddCentered();
                _w.BombFrame = 0;
                inp.Shot = false;
                _mode = MRegular;
            }
        }
        else if (_submode == SdLeft)
        {
            _dashDirection = XLeft;
            Unput();
            MoveAndClamp(-4);
            PutPtn(ModeDashCel(_modeFrame, PtnId.MikoLDashShoot));
            _prevPtn = ModeDashCel(_modeFrame, PtnId.MikoLDashShoot);
            if (_modeFrame >= 1)
            {
                ShotsAddCentered();
                _w.BombFrame = 0;
                inp.Shot = false;
                _mode = MRegular;
            }
        }
        _modeFrame++;
    }

    void Slide(int delta, int celBase)
    {
        MoveAndClamp(delta);
        Put32(SlideCelCur(_modeFrame, celBase));
        if (_modeFrame >= 5 && !_w.Input.Strike && !_w.Input.Shot) _comboEnabled = true;
    }

    void UpdateSwingOrSlide()
    {
        var inp = _w.Input;
        if (_submode == SdStationary)
        {
            if (_modeFrame < 1)
            {
                // Quirk: no blit, Reimu stays invisible during this first frame.
                _submode = inp.Lr % InputLatch.LrBoth;
                if (_submode != SdStationary)
                {
                    _modeFrame = 0;
                    _w.PlayerDeflecting = false;
                    _w.Sound.PlaySe(11);
                    _w.PlayerSliding = true;
                }
            }
            else
            {
                // Quirk: the second and third frame are not blitted either.
                if (_modeFrame % SwingFramesPerCel == 0) Put48(SwingCel(_modeFrame, +1));
                if (_modeFrame == _swingDeflectionFrames) _w.PlayerDeflecting = false;
                if (_modeFrame >= SwingFrames)
                {
                    _mode = MRegular;
                    Unput(); // Quirk: invisible for this frame...
                    _submode = SdNoOrbRepelThenStationaryBug; // ...and the next one.
                    _modeFrame = 0;
                    inp.Strike = false;
                    _w.BombFrame = 0;
                    _invincibleAgainstOrb = false;
                }
            }
        }
        else if (_submode == SdRight || _submode == SdLeft)
        {
            bool right = _submode == SdRight;
            int dir = right ? XRight : XLeft;
            int celBase = right ? CRSlide : CLSlide;
            Slide(right ? +6 : -6, celBase);
            if (_modeFrame >= SlideFrames)
            {
                SlideStop();
                if (_comboEnabled && inp.Strike)
                {
                    SpecialStartStrike(dir);
                    return;
                }
                if (_comboEnabled && inp.Shot)
                {
                    SpecialStartShot(dir);
                    return;
                }
                inp.Strike = false;
                _w.BombFrame = 0;
                // The right-hand branch also clears combo_enabled; the left one does not. Equivalent (see notes).
                if (right) _comboEnabled = false;
                _invincibleAgainstOrb = false;
            }
        }
        _modeFrame++;

        if (_submode == SdStationary)
        {
            if (_modeFrame < 21) OrbPlayerHitTest(_modeFrame);
        }
        else
        {
            if (_submode == SdRight) OrbPlayerHitTest(OrbRepel.ThreeX4Right);
            if (_submode == SdLeft) OrbPlayerHitTest(OrbRepel.ThreeX4Left);
        }
    }

    void UpdateSpecial()
    {
        var inp = _w.Input;
        int specialFrames;

        // Drawing + deflection/combo bookkeeping
        switch (_submode)
        {
            case SsFlipkickStationary | XRight:
                Put48(FlipkickCel(_modeFrame, 0, CRFlipkick));
                break;
            case SsFlipkickStationary | XLeft:
                Put48(FlipkickCel(_modeFrame, 0, CLFlipkick));
                break;
            case SsFlipkickMoving | XRight:
                MoveAndClamp(2);
                Put48(FlipkickCel(_modeFrame, 0, CRFlipkick));
                break;
            case SsFlipkickMoving | XLeft:
                MoveAndClamp(-2);
                Put48(FlipkickCel(_modeFrame, 0, CLFlipkick));
                break;
            case SsSlidekick | XRight:
                MoveAndClamp(6);
                Put32(CRSlidekick);
                break;
            case SsSlidekick | XLeft:
                MoveAndClamp(-6);
                Put32(CLSlidekick);
                break;
            case SsShotCombo | XRight:
                Put48(ShotComboCel(_modeFrame, 0, CRShotCombo));
                break;
            case SsShotCombo | XLeft:
                Put48(ShotComboCel(_modeFrame, 0, CLShotCombo));
                break;
        }

        switch (_submode)
        {
            case SsFlipkickMoving | XRight:
            case SsFlipkickMoving | XLeft:
            case SsFlipkickStationary | XRight:
            case SsFlipkickStationary | XLeft:
                if (_modeFrame == 20) _w.PlayerDeflecting = false;
                if (_modeFrame >= 20 && !inp.Strike && !inp.Shot) _comboEnabled = true;
                specialFrames = 28;
                break;
            case SsSlidekick | XRight:
            case SsSlidekick | XLeft:
                specialFrames = 13;
                break;
            default: // shot combos
                if (_modeFrame == 1) ShotsAddCentered(-8);
                if (_modeFrame == 4) ShotsAddCentered(+8);
                if (_modeFrame == 7) ShotsAddCentered(0);
                if (_modeFrame == 10) ShotsAddCentered(-16);
                if (_modeFrame == 13) ShotsAddCentered(0);
                if (_modeFrame == 16) ShotsAddCentered(+16);
                if (_modeFrame == 19) ShotsAddCentered(0);
                if (_modeFrame == 4) _w.PlayerDeflecting = false;
                specialFrames = ShotComboFrames;
                break;
        }

        _modeFrame++;
        if (_modeFrame > specialFrames)
        {
            // Quirk: the sprite is unblitted here and the regular one never redrawn: Reimu is invisible this frame.
            Unput();
            _w.BombFrame = 0;
            _modeFrame = 0;
            _w.PlayerSliding = false;
            _w.PlayerDeflecting = false;
            if (_mode < MSpecialThird)
            {
                if (_comboEnabled && inp.Strike)
                {
                    if (IsFlipkick(XLeft)) SpecialCombo(XLeft, InputLatch.LrLeft, InputLatch.LrRight);
                    else if (IsFlipkick(XRight)) SpecialCombo(XRight, InputLatch.LrRight, InputLatch.LrLeft);
                    _mode++;
                    SpecialStart(_submode < SsSlidekick);
                    return;
                }
                if (_comboEnabled && inp.Shot)
                {
                    if (IsFlipkick(XLeft)) _submode = SsShotCombo | XLeft;
                    else if (IsFlipkick(XRight)) _submode = SsShotCombo | XRight;
                    _mode++;
                    _w.PlayerDeflecting = true;
                    _w.Sound.PlaySe(11);
                    _comboEnabled = false;
                    return;
                }
            }
            _invincibleAgainstOrb = false;
            _mode = MRegular;
            // Quirk: submode is left as the special value; the next regular frame reads it as a direction
            // and skips the redraw (one more invisible frame).
        }
        else
        {
            switch (_submode)
            {
                case SsFlipkickMoving | XRight:
                case SsFlipkickMoving | XLeft:
                case SsFlipkickStationary | XRight:
                case SsFlipkickStationary | XLeft:
                    OrbPlayerHitTest(_modeFrame / 2);
                    break;
                case SsSlidekick | XRight:
                    OrbPlayerHitTest(OrbRepel.ThreeX8Right);
                    break;
                case SsSlidekick | XLeft:
                    OrbPlayerHitTest(OrbRepel.ThreeX8Left);
                    break;
                case SsShotCombo | XRight:
                case SsShotCombo | XLeft:
                    if (_modeFrame < 16) OrbPlayerHitTest(_modeFrame);
                    break;
            }
        }
    }

    static int DeltaAbs(int a, int b) => a - b < 0 ? (a - b) * -1 : a - b;

    bool OrbHitsPlayer(int w, int h) =>
        DeltaAbs(_w.Orb.Left, Left) < w && DeltaAbs(_w.Orb.Top, Top) < h;

    bool InRepelRange() => OrbHitsPlayer(Playfield.PlayerW + Playfield.OrbW / 4, Playfield.PlayerH);

    /// <summary>
    /// orb_player_hittest(): 0 = lethal test; 1..99 = repel with that friction; 100..104 = fixed-direction repel.
    /// Friction 0 reached through an attack's first frame is therefore a lethal test (original quirk).
    /// </summary>
    public void OrbPlayerHitTest(int repelFriction)
    {
        var o = _w.Orb;
        if (repelFriction == OrbRepel.None)
        {
            if (!_w.PlayerInvincible && !_invincibleAgainstOrb &&
                OrbHitsPlayer(Playfield.PlayerW - Playfield.OrbW / 4, Playfield.PlayerH - Playfield.OrbH / 2))
            {
                _w.PlayerIsHit = true;
            }
        }
        else if (repelFriction < OrbRepel.ThreeXUnchanged)
        {
            if (!InRepelRange()) return;
            int d = Left - o.Left;
            if (d > 0)
            {
                o.VelocityX = OrbVelocityX.Left4;
            }
            else if (d == 0)
            {
                o.VelocityX = OrbVelocityX.Zero;
                if (_w.Rng.Next() % 8 == 0) o.VelocityX = OrbVelocityX.Left4;
                // Quirk: this probably meant Right4.
                if (_w.Rng.Next() % 8 == 4) o.VelocityX = OrbVelocityX.Left4;
            }
            else
            {
                o.VelocityX = OrbVelocityX.Right4;
            }
            o.ForceNew(repelFriction / 2 + Orb.ForceRepel, OrbForce.Immediate);
        }
        else if (InRepelRange())
        {
            if (repelFriction == OrbRepel.ThreeX4Left) o.VelocityX = OrbVelocityX.Left4;
            else if (repelFriction == OrbRepel.ThreeX4Right) o.VelocityX = OrbVelocityX.Right4;
            else if (repelFriction == OrbRepel.ThreeX8Right) o.VelocityX = OrbVelocityX.Right8;
            else if (repelFriction == OrbRepel.ThreeX8Left) o.VelocityX = OrbVelocityX.Left8;
            o.ForceNew(-10, OrbForce.Immediate);
        }
    }

    public void Hash(ref StateHasher h)
    {
        h.Add(Left); h.Add(_invincibleAgainstOrb);
        h.Add(_dashCycle); h.Add(_modeFrame); h.Add(_mode); h.Add(_dashDirection);
        h.Add(_bombFlag); h.Add(_bombing); h.Add(_comboEnabled);
        h.Add(_swingDeflectionFrames); h.Add(_submode); h.Add(_prevPtn);
        h.Add((int)SpriteKind); h.Add(SpriteCel); h.Add(SpriteLeft);
    }
}
