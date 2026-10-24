namespace Th01.Core.Game;

/// <summary>
/// YuugenMagan, the stage 10 boss of the Makai route: five eyes, no body, a 14-phase fight that ends in a spinning
/// pentagram. Rules: docs/GAMEPLAY-NOTES.md section 29. ref: th01/main/boss/b10m.cpp, bullet/line.cpp
/// </summary>
public sealed class YuugenMagan : Boss
{
    public const int EyeW = 64, EyeH = 48;
    public const int HpTotal = 16;
    public const int HitScore = 5000;

    // eye cels
    public const int CHidden = 0, CClosed = 1, CHalfOpen = 2, CDown = 3, CLeft = 4, CRight = 5, CAhead = 6;

    // eye flags
    public const int EfNone = 0, EfWest = 1, EfEast = 2, EfSouthWest = 4, EfSouthEast = 8, EfNorth = 16, EfReset = 32;
    const int EfAll = EfWest | EfEast | EfSouthWest | EfSouthEast | EfNorth;

    // HP limits of the phases
    const int HpPhase1End = 15, HpPhase3End = 12, HpPhase5End = 10, HpPhase7End = 8, HpPhase9End = 2, HpPhase13End = 0;

    const int EyeOpeningFrames = 60, EyeToggleFrames = EyeOpeningFrames + 10;
    const int PreparedFrames = 100, TimeoutFrames = 200;

    // phase 0 keyframes
    const int LateralOpening = 100, SouthOpening = 120, NorthOpening = 140;
    const int LateralOpen = LateralOpening + EyeOpeningFrames, SouthOpen = SouthOpening + EyeOpeningFrames, NorthOpen = NorthOpening + EyeOpeningFrames;
    const int Closing = 240, Closed = 260, Hidden = 280, LateralLaserDone = 300, SouthLaserDone = 320, NorthLaserDone = 330;

    // pentagram
    const int PentagramInterval = 4, PentagramPoints = 5, PentagramRadiusFinal = 64, PentagramAngleInitial = 0xC0;
    const int ShrinkDistance = 224 - PentagramRadiusFinal;          // 160
    const int ShrinkTargetCenterY = Playfield.Top + Playfield.Height / 2;   // 232
    const int PentagramRegularCenterY = 189;

    // pentagram attack phases
    const int PapPrepare1 = 0, PapSpinCw1 = 1, PapSpinCcw1 = 2, PapSpinCw2 = 3, PapSpinCcw2 = 4, PapSpinCw3 = 5,
        PapSlam1 = 6, PapPrepare2 = 7, PapGrow = 8, PapSlam2 = 9;

    // missile pairs from the south
    const int MissileInterval = 8, MissileOffsetLeft = EyeW / 2 + 4 - Missiles.W / 2, MissileOffsetTop = EyeH / 2 + 12 - Missiles.H / 2;

    // lasers across the playfield
    const int LaserVelocityStep = 4 * 4, LaserInterval = 4;

    public sealed class Eye
    {
        public int Left, Top, Image = CHidden;
        public bool HitboxInactive = true;
        public int CenterX => Left + EyeW / 2;
        public int CenterY => Top + EyeH / 2;
        public int OffcenterX => CenterX - 4;
        public int IrisTop => CenterY + 4;
        public int IrisCenterY => IrisTop + 7 / 2 + 1;
        public int PupilBottom => CenterY + 12;
    }

    public readonly Eye West = new(), East = new(), SouthWest = new(), SouthEast = new(), North = new();
    readonly Eye[] _byFlag;

    // statics
    int _patternInterval;
    int _subphase;
    int _missilePairs;       // u1.missile_pairs_fired_in_subphase
    int _subphaseFrame;      // u1.subphase_frame
    int _xEdgeOffset;        // u1.x_edge_offset
    int _distance;           // u1.distance
    int _targetLeft;
    int _afterHitFrame;
    int _iterations;         // u3.iterations_done (int8)
    int _eyesOpen;           // u3.eyes_open
    int _angleSw, _angleSe;  // missile angles (unsigned char)
    int _pelletAngle;        // angle.pellet_east (unsigned char)

    // pentagram
    public int PentPhase;
    public int PentAngle;
    public int PentRadius, PentCenterX, PentCenterY;
    int _pentVelX, _pentVelY;
    public readonly int[] PentX = new int[PentagramPoints], PentY = new int[PentagramPoints];

    bool _defeated;

    public YuugenMagan(GameWorld w) : base(w)
    {
        Hp = HpTotal;
        _patternInterval = ByRank(350, 300, 200, 130);
        West.Left = 320 - 224 - EyeW / 2; West.Top = 128;
        East.Left = 320 + 224 - EyeW / 2; East.Top = 128;
        SouthWest.Left = 320 - 96 - EyeW / 2; SouthWest.Top = 176;
        SouthEast.Left = 320 + 96 - EyeW / 2; SouthEast.Top = 176;
        North.Left = 320 - EyeW / 2; North.Top = Playfield.Top;
        _byFlag = new[] { West, East, SouthWest, SouthEast, North };
    }

    public override int Width => EyeW;
    public override int Height => EyeH;
    public override bool Defeated => _defeated;
    public override bool HasEntrance => false;
    public override bool RunsAtLifeStart => false;
    public override IReadOnlyList<int> FlashColors { get; } = new[] { 1, 11 };
    public override int DefeatTicks()
    {
        int startY = World.Orb.Top + Playfield.OrbH / 2;
        return Math.Max(startY + 1, Playfield.Bottom - startY) / DefeatLineDistance + 15 * DefeatWhiteinInterval;
    }

    public IReadOnlyList<Eye> Eyes => _byFlag;
    public int EyesOpen => _eyesOpen;
    public int Subphase => _subphase;

    public override bool EntranceFrame() => true;

    int ByRank(int easy, int normal, int hard, int lunatic) => World.Rank switch
    {
        Rank.Easy => easy, Rank.Normal => normal, Rank.Hard => hard, Rank.Lunatic => lunatic, _ => normal,
    };

    int PlayerBottom => Player.Top + Playfield.PlayerH;
    int PlayerCenterY => Player.Top + Playfield.PlayerH / 2;

    // ---- eye helpers ----

    IEnumerable<Eye> Flagged(int flags)
    {
        for (int i = 0; i < 5; i++)
            if ((flags & (1 << i)) != 0) yield return _byFlag[i];
    }

    void SetImage(int flags, int cel) { foreach (var e in Flagged(flags)) e.Image = cel; }
    void Activate(int flags) { foreach (var e in Flagged(flags)) e.HitboxInactive = false; }
    void Deactivate(int flags) { foreach (var e in Flagged(flags)) e.HitboxInactive = true; }

    void Track(Eye e)
    {
        int p = World.Player.Left;
        if (e.CenterX - p > EyeW / 2) e.Image = CLeft;
        else if (p - e.CenterX > EyeW / 2) e.Image = CRight;
        else e.Image = CDown;
    }

    void TrackFlagged(int flags) { foreach (var e in Flagged(flags)) Track(e); }

    void FireFromIris(Eye e, PelletGroup g, int speedSp) =>
        World.Pellets.AddGroup(e.CenterX - Pellets.W / 2, e.IrisCenterY - Pellets.H / 2, g, speedSp);

    void FireFromBottom(Eye e, PelletGroup g, int speedSp) =>
        World.Pellets.AddGroup(e.CenterX - Pellets.W / 2, e.PupilBottom - Pellets.H / 2, g, speedSp);

    void FireSingleFromBottom(Eye e, bool mirror, int angle, int speedSp, PelletMotion motion = PelletMotion.Regular, int motionSpeed = 0) =>
        World.Pellets.AddSingle(e.CenterX - Pellets.W / 2, e.PupilBottom - Pellets.H / 2, mirror ? 0x80 - angle : angle, speedSp, motion, motionSpeed);

    /// <summary>The 70-frame eye toggle shared by the transition phases. It advances the given counter itself.</summary>
    void Toggle(int close, int open, ref int frame)
    {
        frame++;
        if (frame == EyeOpeningFrames / 3 * 1)
        {
            SetImage(close, CHalfOpen);
            SetImage(open, CClosed);
            Activate(open);
        }
        else if (frame == EyeOpeningFrames / 3 * 2)
        {
            SetImage(close, CClosed);
            SetImage(open, CHalfOpen);
            Deactivate(close);
        }
        else if (frame == EyeOpeningFrames / 3 * 3)
        {
            SetImage(close, CHidden);
            SetImage(open, CAhead);
        }
    }

    // ---- hit handling ----

    bool EyeHit(Eye e) =>
        !e.HitboxInactive && e.Image != CHidden &&
        e.Left - 12 <= World.Orb.PrevLeft && e.Left + 44 >= World.Orb.PrevLeft &&
        e.Top - 12 <= World.Orb.PrevTop && e.Top + 24 >= World.Orb.PrevTop;

    void HitUpdate() =>
        HitCommon(_byFlag.Any(EyeHit), HitScore, 0, 0, 0, 0);

    // ---- line bullets ----

    /// <summary>A line only hurts through its end point (right, bottom).</summary>
    void LineHit(int right, int bottom)
    {
        var w = World;
        if (right > w.Player.Left && right < w.Player.Left + Playfield.PlayerW && bottom > PlayerCenterY && !w.PlayerInvincible)
            w.PlayerIsHit = true;
    }

    // ---- pentagram ----

    void PentCorners(int angle)
    {
        for (int i = 0; i < PentagramPoints; i++)
        {
            PentX[i] = AngleMath.PolarX(PentCenterX, PentRadius, angle);
            PentY[i] = AngleMath.PolarY(PentCenterY, PentRadius, angle);
            angle = (angle + 0x100 / PentagramPoints) & 0xFF;
        }
    }

    void PentPutAndHitTest()
    {
        LineHit(PentX[2], PentY[2]);
        LineHit(PentX[4], PentY[4]);
        LineHit(PentX[1], PentY[1]);
        LineHit(PentX[3], PentY[3]);
        LineHit(PentX[0], PentY[0]);
    }

    void PentRegularUpdate(int angle)
    {
        PentCorners(angle & 0xFF);
        PentPutAndHitTest();
    }

    void PentSpin(bool clockwise)
    {
        int d = PhaseFrame / 64 + 2;
        PentAngle += clockwise ? d : -d;
    }

    void PentAim()
    {
        int a = AngleMath.Iatan2(PlayerBottom - PentCenterY, World.Player.Left - PentCenterX);
        _pentVelX = AngleMath.PolarX(0, 4, a);
        _pentVelY = AngleMath.PolarY(0, 4, a);
    }

    // shrink geometry: divisors derived from the compile-time constants of the original (see notes)
    const int DivNorthY = 2, DivLateralY = 3, DivSouthY = 2, DivSouthX = 3;

    (int x, int y) ShrinkPoint(int eyeFlag, int d)
    {
        return eyeFlag switch
        {
            EfWest => (West.OffcenterX + d, West.IrisTop + d / DivLateralY),
            EfEast => (East.OffcenterX - d, East.IrisTop + d / DivLateralY),
            EfSouthWest => (SouthWest.OffcenterX + d / DivSouthX, SouthWest.IrisTop + d / DivSouthY),
            EfSouthEast => (SouthEast.OffcenterX - d / DivSouthX, SouthEast.IrisTop + d / DivSouthY),
            _ => (North.OffcenterX, North.IrisTop + d / DivNorthY),
        };
    }

    (int x, int y)[] ShrinkPoints(int d) => new[]
    {
        ShrinkPoint(EfNorth, d), ShrinkPoint(EfSouthWest, d), ShrinkPoint(EfEast, d), ShrinkPoint(EfWest, d), ShrinkPoint(EfSouthEast, d),
    };

    /// <summary>Lines of the pentagram that joins the five eyes (end points: x, y of the second eye).</summary>
    void PentagramBetweenEyes()
    {
        void L(Eye b) => LineHit(b.OffcenterX, b.IrisTop);
        L(SouthWest); L(East); L(West); L(SouthEast); L(North);
    }

    // ---- phase 0 ----

    void Phase0Lasers()
    {
        // overlap_low_center_lt_gt(player_left, w, eye.offcenter_x, 48)
        bool Hit(Eye e, int playerW) =>
            World.Player.Left > e.OffcenterX - 24 && World.Player.Left < e.OffcenterX + 24 - playerW;
        if (PhaseFrame > LateralOpen)
        {
            if (PhaseFrame < LateralLaserDone)
            {
                // quirk: the east eye uses a much narrower player width
                if (Hit(West, Playfield.PlayerW) || Hit(East, Playfield.PlayerW / 4)) World.PlayerIsHit = true;
            }
            if (PhaseFrame > SouthOpen)
            {
                if (PhaseFrame < SouthLaserDone && (Hit(SouthWest, Playfield.PlayerW) || Hit(SouthEast, Playfield.PlayerW)))
                    World.PlayerIsHit = true;
                if (PhaseFrame > NorthOpen && Hit(North, Playfield.PlayerW)) World.PlayerIsHit = true;
            }
        }
        if (World.PlayerInvincible) World.PlayerIsHit = false;
    }

    void Phase0OpenEyes(int frame)
    {
        void Open(Eye e, int first) =>
            e.Image = frame - first < EyeOpeningFrames / 3 * 1 ? CClosed :
                      frame - first < EyeOpeningFrames / 3 * 2 ? CHalfOpen :
                      frame - first < EyeOpeningFrames / 3 * 3 ? CAhead : CDown;
        if (frame >= LateralOpening && frame <= LateralOpen) { Open(West, LateralOpening); Open(East, LateralOpening); }
        if (frame >= SouthOpening && frame <= SouthOpen) { Open(SouthWest, SouthOpening); Open(SouthEast, SouthOpening); }
        if (frame >= NorthOpening && frame <= NorthOpen) Open(North, NorthOpening);
    }

    void Phase0CloseEyes(int frame) =>
        SetImage(EfAll, frame == Closing ? CHalfOpen : frame == Closed ? CClosed : CHidden);

    void Phase0Fire(Eye e) => FireFromIris(e, PelletGroup.FiveSpreadWideAimed, 54);   // 3.375 px

    void UpdatePhase0()
    {
        PhaseFrame++;
        Phase0Lasers();
        int f = PhaseFrame;
        if (f == LateralOpening)
        {
            Phase0OpenEyes(f);
            Activate(EfWest | EfEast);
        }
        else if (f == SouthOpening)
        {
            Phase0OpenEyes(f);
            Activate(EfSouthWest | EfSouthEast);
        }
        else if (f == NorthOpening)
        {
            Phase0OpenEyes(f);
            Activate(EfNorth);
        }
        else if (f == LateralOpen || f == SouthOpen || f == NorthOpen)
        {
            Phase0OpenEyes(f);
        }
        else if (f == Closing)
        {
            Phase0CloseEyes(f);
            Deactivate(EfAll);
            // quirk: only Hard (the Lunatic branch inside is unreachable)
            if (World.Rank == Rank.Hard)
            {
                Phase0Fire(North); Phase0Fire(SouthEast); Phase0Fire(SouthWest);
            }
        }
        else if (f == Closed)
        {
            Phase0CloseEyes(f);
            if (World.Rank == Rank.Lunatic)
            {
                Phase0Fire(North); Phase0Fire(SouthEast); Phase0Fire(SouthWest); Phase0Fire(West); Phase0Fire(East);
            }
        }
        else if (f == Hidden)
        {
            Phase0CloseEyes(f);
        }
        else if (f == LateralLaserDone)
        {
            West.Image = CClosed; East.Image = CClosed;
        }
        else if (f == SouthLaserDone)
        {
            West.Image = CHalfOpen; East.Image = CHalfOpen;
            Activate(EfWest | EfEast);
        }
        else if (f > NorthLaserDone)
        {
            Phase = 1;
            Invincible = false;
            PhaseFrame = 0;
            West.Image = CLeft;
            East.Image = CDown;
            _patternInterval = ByRank(350, 300, 200, 130);
        }
    }

    // ---- phase helpers ----

    void FrameCommon()
    {
        PhaseFrame++;
        InvincibilityFrame++;
    }

    void Next(int phase)
    {
        Phase = phase;
        PhaseFrame = 0;
        InvincibilityFrame = 0;
    }

    void FromLateralPellets()
    {
        int m = PhaseFrame % _patternInterval;
        (PelletGroup g, int sp)? shot =
            m == 10 ? (PelletGroup.TwoSpreadWideAimed, 24) :
            m == 25 ? (PelletGroup.TwoSpreadWideAimed, 28) :
            m == 40 ? (PelletGroup.TwoSpreadWideAimed, 32) :
            m == 60 ? (PelletGroup.TwoSpreadWideAimed, 44) :
            m == 80 ? (PelletGroup.TwoSpreadWideAimed, 50) :
            m == 110 ? (PelletGroup.OneAimed, 68) : null;
        if (shot is { } s)
        {
            FireFromBottom(West, s.g, s.sp);
            FireFromBottom(East, s.g, s.sp);
        }
    }

    // ---- missile pairs from the south (phases 3 and 7) ----

    int MissileAngle(Eye e, int targetX) =>
        AngleMath.Iatan2(PlayerBottom - e.Top + MissileOffsetTop, targetX - e.Left + MissileOffsetLeft);

    void FireMissilePair()
    {
        void One(Eye e, int angle)
        {
            int vx = AngleMath.PolarX(0, 8, angle), vy = AngleMath.PolarY(0, 8, angle);
            World.Missiles.Add(e.Left + MissileOffsetLeft, e.Top + MissileOffsetTop, vx << 4, vy << 4);
        }
        One(SouthWest, _angleSw);
        One(SouthEast, _angleSe);
    }

    static bool SubIs(int sub, int step, int vmax)
    {
        for (int v = 0; v < 3; v++)
            if (vmax >= v + 1 && sub == v * 4 + step) return true;
        return false;
    }

    void MissilePairs(int vmax, bool clock)
    {
        const int Prepare = 0, AimAndFire = 1, Shift1 = 2, Shift2 = 3;
        TrackFlagged(EfSouthWest | EfSouthEast);

        if (PhaseFrame >= PreparedFrames - 10 && SubIs(_subphase, Prepare, 2)) SetImage(EfSouthWest | EfSouthEast, CClosed);

        if (PhaseFrame == PreparedFrames || SubIs(_subphase, AimAndFire, vmax))
        {
            if (vmax >= 1 && _subphase == 0 * 4 + Prepare)
            {
                _subphase = World.Rng.Next() % vmax * 4 + AimAndFire;
                _targetLeft = World.Player.Left - Missiles.W / 2;
                _angleSw = MissileAngle(SouthWest, _targetLeft);
                _angleSe = MissileAngle(SouthEast, _targetLeft);
                _angleSw = (_angleSw - 0x10) & 0xFF;
                _angleSe = (_angleSe + 0x10) & 0xFF;
            }
            if (PhaseFrame % MissileInterval == 0) FireMissilePair();
            if (PhaseFrame == TimeoutFrames)
            {
                _subphase++;
                _missilePairs = 0;
            }
        }
        if (SubIs(_subphase, Shift1, vmax))
        {
            if (PhaseFrame % MissileInterval == 1)
            {
                _missilePairs++;
                if (!clock)
                {
                    _angleSw = (_angleSw - 2) & 0xFF;
                    _angleSe = (_angleSe + 2) & 0xFF;
                }
                else
                {
                    int d = _subphase == 0 * 4 + Shift1 ? -2 : +2;
                    _angleSw = (_angleSw + d) & 0xFF;
                    _angleSe = (_angleSe + d) & 0xFF;
                }
                FireMissilePair();
                if (_missilePairs > 10)
                {
                    _subphase++;
                    _missilePairs = 0;
                }
            }
        }
        else if (SubIs(_subphase, Shift2, vmax))
        {
            if (PhaseFrame % MissileInterval == 1)
            {
                _missilePairs++;
                if (!clock)
                {
                    if (_subphase == 0 * 4 + Shift2) _angleSw = (_angleSw + 4) & 0xFF;
                    else if (_subphase == 1 * 4 + Shift2) _angleSe = (_angleSe - 4) & 0xFF;
                    else if (_subphase == 2 * 4 + Shift2) { _angleSw = (_angleSw + 2) & 0xFF; _angleSe = (_angleSe - 2) & 0xFF; }
                }
                else
                {
                    if (_subphase == 0 * 4 + Shift2) { _angleSw = (_angleSw + 3) & 0xFF; _angleSe = (_angleSe + 3) & 0xFF; }
                    else if (_subphase == 1 * 4 + Shift2) { _angleSw = (_angleSw - 3) & 0xFF; _angleSe = (_angleSe - 3) & 0xFF; }
                }
                FireMissilePair();
                if (_missilePairs > 10)
                {
                    _subphase = 0 * 4 + Prepare;
                    _missilePairs = 0;
                    PhaseFrame = 0;
                    _iterations++;
                }
            }
        }
    }

    // ---- phase 5 ----

    void UpdatePhase5()
    {
        const int Prepare1 = 0, Spray1 = 1, Prepare2 = 2, Spray2 = 3;
        FrameCommon();
        _subphaseFrame++;
        _pelletAngle = (_pelletAngle - 1) & 0xFF;
        TrackFlagged(EfWest | EfEast);

        if (PhaseFrame == PreparedFrames && _subphase == Prepare1)
        {
            _subphaseFrame = 0;
            _subphase = Spray1;
        }
        if (_subphase == Spray1)
        {
            if (_subphaseFrame % _patternInterval == 0)
            {
                FireSingleFromBottom(West, true, _pelletAngle, 64);
                FireSingleFromBottom(East, false, _pelletAngle, 64);
            }
            if (_subphaseFrame / 5 >= 36)
            {
                _subphase = Prepare2;
                PhaseFrame = 0;
            }
        }
        if (_subphase == Prepare2 && PhaseFrame > PreparedFrames / 2)
        {
            _subphaseFrame = -15;
            _subphase = Spray2;
            _pelletAngle = 0x80;
        }
        if (_subphase == Spray2)
        {
            if (_subphaseFrame % _patternInterval == 0)
            {
                FireSingleFromBottom(West, true, _pelletAngle, 64, PelletMotion.FallStraightFromTopThenRegular, 64);
                FireSingleFromBottom(East, false, _pelletAngle, 64, PelletMotion.FallStraightFromTopThenRegular, 64);
            }
            if (_subphaseFrame / 5 >= 20)
            {
                _subphase = Prepare1;
                PhaseFrame = 0;
                _iterations++;
                _pelletAngle = 0x00;
            }
        }
        HitUpdate();
        if (Hp <= HpPhase5End || _iterations > 4) Next(6);
    }

    // ---- phase 9: lasers across the floor ----

    int LaserSubphase(int iteration, int lsp) => iteration * 2 + lsp;

    int LaserRight(int offset, bool rightToLeft) => rightToLeft ? Playfield.Right - offset : Playfield.Left + offset;

    void LaserHitTest(int offset, bool rightToLeft) => LineHit(LaserRight(offset, rightToLeft), Playfield.Bottom);

    void SingleLaser(int iteration, bool rightToLeft)
    {
        if (_subphase == LaserSubphase(iteration, 0))
        {
            _subphase = LaserSubphase(iteration, 1);
            if (rightToLeft) _xEdgeOffset = 0;
        }
        if (PhaseFrame % LaserInterval == 1)
        {
            if (_xEdgeOffset <= Playfield.CenterX - EyeW) North.Image = rightToLeft ? CRight : CLeft;
            else if (_xEdgeOffset >= Playfield.CenterX + EyeW) North.Image = rightToLeft ? CLeft : CRight;
            else North.Image = CDown;

            LaserHitTest(_xEdgeOffset, rightToLeft);
            if (_xEdgeOffset >= Playfield.Width - (Playfield.PlayerW + Playfield.PlayerW / 2))
            {
                _subphase = LaserSubphase(iteration + 1, 0);
                PhaseFrame = 0;
                _xEdgeOffset = 0;
                if (World.Rank > Rank.Normal) FireFromIris(North, PelletGroup.ThreeSpreadNarrowAimed, 3);
            }
            _xEdgeOffset += LaserVelocityStep;
        }
    }

    void DualLasers(int iteration)
    {
        if (_subphase == LaserSubphase(iteration, 0))
        {
            _subphase = LaserSubphase(iteration, 1);
            _xEdgeOffset = 0;
        }
        if (PhaseFrame % LaserInterval == 1)
        {
            North.Image = CAhead;
            LaserHitTest(_xEdgeOffset, false);
            LaserHitTest(_xEdgeOffset, true);
            if (_xEdgeOffset >= Playfield.CenterX - Playfield.PlayerW)
            {
                _subphase = LaserSubphase(iteration, 2);
                PhaseFrame = 0;
                _xEdgeOffset = 0;
                if (World.Rank == Rank.Hard) FireFromIris(North, PelletGroup.ThreeSpreadNarrowAimed, 3);
                if (World.Rank == Rank.Lunatic) FireFromIris(North, PelletGroup.FiveSpreadNarrowAimed, 4);
            }
            _xEdgeOffset += LaserVelocityStep;
        }
    }

    bool LaserShouldRun(int iteration) =>
        (PhaseFrame == 40 && _subphase == LaserSubphase(iteration, 0)) || _subphase == LaserSubphase(iteration, 1);

    void UpdatePhase9()
    {
        FrameCommon();
        if (PhaseFrame >= 30 && (_subphase == LaserSubphase(0, 0) || _subphase == LaserSubphase(1, 0) || _subphase == LaserSubphase(2, 0)))
            North.Image = CClosed;
        if (LaserShouldRun(0)) SingleLaser(0, false);
        else if (LaserShouldRun(1)) SingleLaser(1, true);
        else if (LaserShouldRun(2)) DualLasers(2);
        HitUpdate();
        if (Hp <= HpPhase9End || _subphase == LaserSubphase(2, 2))
        {
            Phase = 10;
            PhaseFrame = 0;
            InvincibilityFrame = 0;
            PentPhase = PapPrepare1;
        }
    }

    // ---- phase 11 ----

    void UpdatePhase11()
    {
        const int PrepareSub = 0, CornersInEyes = 1, Move = 2, Done = 3;
        FrameCommon();
        TrackFlagged(EfAll);
        if (PhaseFrame == PreparedFrames || _subphase == CornersInEyes)
        {
            if (_subphase == PrepareSub)
            {
                _subphase = CornersInEyes;
                _targetLeft = World.Player.Left;
            }
            PentagramBetweenEyes();
            if (PhaseFrame == 140)
            {
                _subphase = Move;
                _distance = 0;
            }
        }
        if (_subphase == Move && PhaseFrame % PentagramInterval == 1)
        {
            _distance += ShrinkDistance / 20;
            var p = ShrinkPoints(_distance);
            for (int i = 0; i < 5; i++) { var q = p[(i + 1) % 5]; LineHit(q.x, q.y); }
            if (_distance > ShrinkDistance)
            {
                var n = ShrinkPoint(EfNorth, _distance);
                PentX[0] = n.x;
                PentY[0] = n.y;
                _subphase = Done;
            }
        }
        HitUpdate();
        if (Hp <= HpPhase13End) _defeated = true;
        if ((Hp <= HpPhase13End || _subphase == Done) && !Invincible)
        {
            Next(12);
            _subphase = 0;
            _eyesOpen = EfWest;
            PentCenterY = PentY[0] + PentagramRadiusFinal;
            PentCenterX = PentX[0];
            PentRadius = PentagramRadiusFinal;
            PentCorners(0x00);
            PentY[1] = PentY[4];
            _patternInterval = ByRank(24, 14, 10, 8);
        }
    }

    void UpdatePhase12()
    {
        const int DistanceY = ShrinkTargetCenterY - PentagramRegularCenterY + 8, Steps = EyeToggleFrames / PentagramInterval, YStep = DistanceY / Steps;
        InvincibilityFrame++;
        Toggle(EfEast | EfSouthWest | EfSouthEast | EfNorth, EfWest, ref PhaseFrame);
        if (InvincibilityFrame % PentagramInterval == 0)
        {
            PentCenterY -= YStep;
            PentRegularUpdate(PentagramAngleInitial);
        }
        if (PhaseFrame >= EyeToggleFrames)
        {
            Next(13);
            _subphase = 0;
            _eyesOpen = EfWest;
            PentAngle = 0x00;
            _afterHitFrame = 0;
        }
    }

    bool PentInSlam => PentPhase == PapSlam1 || PentPhase == PapSlam2;

    /// <summary>Returns true when the slam is over (the pentagram left the field at the bottom).</summary>
    bool PentSlam(int homingThresholdFromBottom)
    {
        if (PhaseFrame % PentagramInterval == 0)
        {
            if (PentCenterY < Playfield.Bottom - homingThresholdFromBottom) PentAim();
            PentCenterY += _pentVelY;
            PentCenterX += _pentVelX;
            PentSpin(true);
            PentRegularUpdate(PentAngle);
        }
        return PentCenterY >= Playfield.Bottom + Playfield.Height / 84 * 5;
    }

    void BackFrom13To10(int newPentPhase)
    {
        if (newPentPhase == PapPrepare1) PentPhase = newPentPhase;
        PhaseFrame = 0;
        Phase = 10;
        if (newPentPhase == PapPrepare2) PentPhase = newPentPhase;
        InvincibilityFrame = 0;
    }

    void UpdatePhase13()
    {
        const int GrowRadius = TimeoutFrames / PentagramInterval, GrownRadius = PentagramRadiusFinal + GrowRadius;
        FrameCommon();
        if (Invincible && _afterHitFrame == 0)
        {
            _eyesOpen <<= 1;
            _afterHitFrame = 1;
            if (_eyesOpen == EfReset) _eyesOpen = EfWest;
        }
        else if (_afterHitFrame > 0)
        {
            Toggle(_eyesOpen == EfWest ? EfNorth : _eyesOpen >> 1, _eyesOpen, ref _afterHitFrame);
            if (_afterHitFrame >= EyeToggleFrames) _afterHitFrame = 0;
        }
        else
        {
            TrackFlagged(_eyesOpen);
        }

        if (PhaseFrame % _patternInterval == 0 && !PentInSlam)
        {
            for (int i = 0; i < PentagramPoints; i++)
            {
                int a = AngleMath.Iatan2(PentY[i] - PentCenterY, PentX[i] - PentCenterX);
                World.Pellets.AddSingle(PentX[i], PentY[i], a, 48);
            }
        }

        if (PhaseFrame == PreparedFrames && PentPhase == PapPrepare1) { PentPhase = PapPrepare1 + 1; PhaseFrame = 0; }
        if (PhaseFrame == PreparedFrames && PentPhase == PapPrepare2) { PentPhase = PapPrepare2 + 1; PhaseFrame = 0; }

        if (PentPhase is PapSpinCw1 or PapSpinCw2 or PapSpinCw3)
        {
            if (PhaseFrame % PentagramInterval == 0)
            {
                PentSpin(true);
                PentRegularUpdate(PentAngle);
            }
            if (PhaseFrame >= TimeoutFrames)
            {
                PentPhase++;
                PhaseFrame = 0;
            }
        }
        if (PentPhase is PapSpinCcw1 or PapSpinCcw2)
        {
            if (PhaseFrame % PentagramInterval == 0)
            {
                PentSpin(false);
                PentRegularUpdate(PentAngle);
            }
            if (PhaseFrame >= TimeoutFrames)
            {
                PentPhase++;
                PhaseFrame = 0;
                PentAim();
            }
        }
        else if (PentPhase == PapSlam1)
        {
            if (PentSlam(Playfield.PlayerH + 4 + PentagramRadiusFinal)) BackFrom13To10(PapPrepare2);
        }
        else if (PentPhase == PapGrow)
        {
            if (PhaseFrame % PentagramInterval == 0)
            {
                PentRadius++;
                PentSpin(true);
                PentRegularUpdate(PentAngle);
            }
            if (PhaseFrame >= TimeoutFrames)
            {
                PentPhase = PapSlam2;
                PhaseFrame = 0;
                PentAim();
            }
        }
        if (PentPhase == PapSlam2 && Phase == 13)
        {
            if (PentSlam(GrownRadius + (Playfield.PlayerH - 6))) BackFrom13To10(PapPrepare1);
        }
        HitUpdate();
        if (Hp <= HpPhase13End)
        {
            World.Missiles.Reset();
            _defeated = true;
        }
    }

    // ---- main ----

    public override void Update()
    {
        World.Missiles.Update();
        switch (Phase)
        {
            case 0: UpdatePhase0(); break;
            case 1:
                FrameCommon();
                TrackFlagged(EfWest | EfEast);
                FromLateralPellets();
                HitUpdate();
                if (Hp <= HpPhase1End || PhaseFrame > 1100) Next(2);
                break;
            case 2:
                Toggle(EfWest | EfEast, EfSouthWest | EfSouthEast, ref PhaseFrame);
                if (PhaseFrame >= EyeToggleFrames)
                {
                    Next(3);
                    _subphase = 0;
                    _iterations = 0;
                    _missilePairs = 0;
                }
                break;
            case 3:
                FrameCommon();
                MissilePairs(3, clock: false);
                HitUpdate();
                if (Hp <= HpPhase3End || _iterations >= 5) Next(4);
                break;
            case 4:
                Toggle(EfSouthWest | EfSouthEast, EfWest | EfEast, ref PhaseFrame);
                if (PhaseFrame >= EyeToggleFrames)
                {
                    Next(5);
                    _subphase = 0;
                    _iterations = 0;
                    _subphaseFrame = 0;
                    _patternInterval = ByRank(12, 8, 4, 2);
                    _pelletAngle = 0x00;
                }
                break;
            case 5: UpdatePhase5(); break;
            case 6:
                Toggle(EfWest | EfEast, EfSouthWest | EfSouthEast, ref PhaseFrame);
                if (PhaseFrame >= EyeToggleFrames)
                {
                    Next(7);
                    _subphase = 0;
                    _iterations = 0;
                    _missilePairs = 0;
                    PentAngle = 0x00;
                }
                break;
            case 7:
                FrameCommon();
                MissilePairs(2, clock: true);
                HitUpdate();
                if (Hp <= HpPhase7End || _iterations > 4) Next(8);
                break;
            case 8:
                Toggle(EfSouthWest | EfSouthEast, EfNorth, ref PhaseFrame);
                if (PhaseFrame >= EyeToggleFrames)
                {
                    Next(9);
                    _subphase = 0;
                    _xEdgeOffset = 0;
                    PentAngle = 0x00;
                }
                break;
            case 9: UpdatePhase9(); break;
            case 10:
                Toggle(EfNone, EfAll, ref PhaseFrame);
                if (PhaseFrame >= EyeToggleFrames)
                {
                    Next(11);
                    _subphase = 0;
                    _eyesOpen = 31;
                    _distance = 0;
                    PentAngle = 0x00;
                }
                break;
            case 11: UpdatePhase11(); break;
            case 12: UpdatePhase12(); break;
            case 13: UpdatePhase13(); break;
        }
    }

    /// <summary>Lines the renderer should draw this frame (x1, y1, x2, y2).</summary>
    public IEnumerable<(int x1, int y1, int x2, int y2)> Lines()
    {
        if (Phase == 0)
        {
            foreach (var (e, from, to) in new[] { (West, LateralOpen, LateralLaserDone), (East, LateralOpen, LateralLaserDone),
                (SouthWest, SouthOpen, SouthLaserDone), (SouthEast, SouthOpen, SouthLaserDone), (North, NorthOpen, int.MaxValue) })
                if (PhaseFrame > from && PhaseFrame < to) yield return (e.OffcenterX, e.IrisTop, e.OffcenterX, Playfield.Bottom - 2);
        }
        else if (Phase == 9 && _subphase % 2 == 1)
        {
            int it = _subphase / 2;
            if (it < 2) yield return (North.OffcenterX, North.IrisTop, LaserRight(_xEdgeOffset, it == 1), Playfield.Bottom);
            else
            {
                yield return (North.OffcenterX, North.IrisTop, LaserRight(_xEdgeOffset, false), Playfield.Bottom);
                yield return (North.OffcenterX, North.IrisTop, LaserRight(_xEdgeOffset, true), Playfield.Bottom);
            }
        }
        else if (Phase == 11 && _subphase == 1)
        {
            Eye[] ring = { North, SouthWest, East, West, SouthEast };
            for (int i = 0; i < 5; i++) yield return (ring[i].OffcenterX, ring[i].IrisTop, ring[(i + 1) % 5].OffcenterX, ring[(i + 1) % 5].IrisTop);
        }
        else if (Phase == 11 && _subphase >= 2)
        {
            var p = ShrinkPoints(_distance);
            for (int i = 0; i < 5; i++) yield return (p[i].x, p[i].y, p[(i + 1) % 5].x, p[(i + 1) % 5].y);
        }
        else if (Phase >= 12 && !(Phase == 13 && PentPhase == PapPrepare1 && PhaseFrame < PreparedFrames) && PentRadius > 0)
        {
            int[] order = { 0, 2, 4, 1, 3 };
            for (int i = 0; i < 5; i++) yield return (PentX[order[i]], PentY[order[i]], PentX[order[(i + 1) % 5]], PentY[order[(i + 1) % 5]]);
        }
    }

    public override void Hash(ref StateHasher h)
    {
        h.Add(Phase); h.Add(PhaseFrame); h.Add(InvincibilityFrame); h.Add(Hp); h.Add(Invincible); h.Add(_defeated);
        foreach (var e in _byFlag) { h.Add(e.Image); h.Add(e.HitboxInactive); }
        h.Add(_patternInterval); h.Add(_subphase); h.Add(_missilePairs); h.Add(_subphaseFrame); h.Add(_xEdgeOffset); h.Add(_distance);
        h.Add(_targetLeft); h.Add(_afterHitFrame); h.Add(_iterations); h.Add(_eyesOpen); h.Add(_angleSw); h.Add(_angleSe); h.Add(_pelletAngle);
        h.Add(PentPhase); h.Add(PentAngle); h.Add(PentRadius); h.Add(PentCenterX); h.Add(PentCenterY); h.Add(_pentVelX); h.Add(_pentVelY);
        for (int i = 0; i < 5; i++) { h.Add(PentX[i]); h.Add(PentY[i]); }
    }
}
