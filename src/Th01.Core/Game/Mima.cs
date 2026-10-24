namespace Th01.Core.Game;

/// <summary>
/// Mima, the stage 10 boss of the Jigoku route: a 128x160 body that casts from a rotating square, drops pillars,
/// hops around the field and fires missiles, pellets and lasers. Rules: docs/GAMEPLAY-NOTES.md section 28.
/// ref: th01/main/boss/b10j.cpp
/// </summary>
public sealed class Mima : Boss
{
    public const int W = 128, H = 160;
    public const int BaseCenterX = Playfield.CenterX;
    public const int BaseCenterY = Playfield.Top + Playfield.Height / 42 * 17;
    public const int BaseLeft = BaseCenterX - W / 2;
    public const int BaseTop = BaseCenterY - H / 2;
    public const int HpTotal = 12, HpPhase1End = 6, HpPhase3End = 0;
    public const int HitScore = 5000;

    const int SealRadius = 80;
    const int SealCircumsquareRadius = 112;       // (int)(80 * 1.41f)
    const int SquareInterval = 8;
    const int SquareRadiusStep = 8;
    const int SquareInitialRadius = 32;           // (int)(80 * 0.4f)
    const int PixelNone = -999;

    // Pillar pattern
    const int PillarCount = 8, DelayPerCircle = 20, PillarW = 32;
    const int KeyframeCircles = 100, KeyframeCircleLast = KeyframeCircles + (PillarCount - 1) * DelayPerCircle;
    const int TimeCircles = 128, TimePillars = 32, TimePillarsDone = 16;

    /// <summary>Entrance and defeat are blocking in the original; durations are assumptions (see notes).</summary>
    public const int DefeatFadeTicks = 15 * 5;

    // ---- statics of b10j.cpp ----
    int _spreadInInterval = 4;
    int _spreadInSpeed = 8;
    int _hop = 255;                               // uint8, 255 = "not started"
    int _direction;                               // 0 right, 1 left
    int _speed;                                   // pattern_state.speed (sub-pixels, or decimal for the half-circle missiles)
    int _targetLeft;
    int _missileAngle;
    int _pelletAngle;

    // rotating square
    public int SquareAngle, SquareFlag, SquareRadius;
    int _sqCenterX, _sqCenterY;
    readonly (int x, int y)[,] _corners = new (int x, int y)[2, 4];

    // pillars
    readonly int[] _pTime = new int[PillarCount], _pCenterX = new int[PillarCount], _pBottom = new int[PillarCount];

    // drawing-only state kept for the renderer
    public bool MeteorActive = true;
    public bool CastVisible;

    // entrance
    bool _entranceStarted;
    int _entranceWait;

    bool _defeated;

    public Mima(GameWorld w) : base(w)
    {
        Hp = HpTotal;
        Left = Playfield.CenterX - W / 2;
        Top = Playfield.Top;
        World.Particles.Init();
    }

    public override int Width => W;
    public override int Height => H;
    public override bool Defeated => _defeated;

    public (int x, int y) SquareCorner(int which, int i) => _corners[which, i];

    int ByRank(int easy, int normal, int hard, int lunatic) => World.Rank switch
    {
        Rank.Easy => easy, Rank.Normal => normal, Rank.Hard => hard, Rank.Lunatic => lunatic, _ => normal,
    };

    int PlayerCenterX => World.Player.Left + Playfield.PlayerW / 2;
    static int PlayerCenterY => Player.Top + Playfield.PlayerH / 2;

    /// <summary>Ticks of the white-out and fade after the last hit: lines spreading 2 px per frame from the Orb, then the fade.</summary>
    public override IReadOnlyList<int> FlashColors { get; } = new[] { 3, 9 };

    public override int DefeatTicks()
    {
        int startY = World.Orb.Top + Playfield.OrbH / 2;
        return Math.Max(startY + 1, Playfield.Bottom - startY) / 2 + DefeatFadeTicks;
    }

    // ---- sprite spread-in (the vertical opening used for the entrance, hops and phase change) ----

    /// <summary>Sets the frame counter to 0 when the animation is complete.</summary>
    void SpreadIn(int finalLeft, int finalTop)
    {
        if (PhaseFrame < 10) return;
        if (PhaseFrame == 10)
        {
            Left = finalLeft;
            Top = finalTop;
            return;
        }
        if (PhaseFrame % _spreadInInterval != 0) return;
        int lineOnTop = H / 2 - _spreadInSpeed / _spreadInInterval * (PhaseFrame - 10);
        if (lineOnTop < 0) PhaseFrame = 0;
    }

    // ---- entrance (phase 0) ----

    public override bool EntranceFrame()
    {
        if (!_entranceStarted)
        {
            _entranceStarted = true;
            World.Missiles.Update();
            World.Particles.Update(ParticleOrigin.TopRight);
            PhaseFrame = 0;
            HitboxOrbInactive = false;
            _spreadInInterval = 1;
            _spreadInSpeed = 2;
            MeteorActive = true;
            _entranceWait = 0;
        }
        if (_entranceWait > 0)
        {
            _entranceWait--;
            return false;
        }
        PhaseFrame++;
        SpreadIn(BaseLeft, BaseTop);
        if (PhaseFrame != 0)
        {
            _entranceWait = 1;                         // frame_delay(2)
            return false;
        }
        Phase = 1;
        PatternCur = 0;
        HopInit();
        return true;
    }

    // ---- rotating square ----

    void SquareInit(int flag)
    {
        SquareFlag = flag;
        SquareRadius = SquareInitialRadius;
        SquareAngle = 0;
    }

    void SetCorners(int which, int angle)
    {
        for (int i = 0; i < 4; i++)
        {
            _corners[which, i] = (AngleMath.PolarX(_sqCenterX, SquareRadius, angle), AngleMath.PolarY(_sqCenterY, SquareRadius, angle));
            angle = (angle + 0x40) & 0xFF;
        }
    }

    /// <summary>Recomputes the corners from the CURRENT angle and radius (the original's unblit/blit calls).</summary>
    void SquareSet()
    {
        _sqCenterX = CenterX;
        _sqCenterY = CenterY;
        if ((SquareFlag & 1) != 0) SetCorners(0, SquareAngle);
        if ((SquareFlag & 2) != 0) SetCorners(1, (0 - SquareAngle) & 0xFF);
    }

    void SquareDeactivate()
    {
        SquareSet();
        SquareFlag = 0;
    }

    void TurnSquare(int delta) => SquareAngle = (SquareAngle + delta) & 0xFF;

    void FireStaticFromCorner(int x, int y, int speed)
    {
        int angle = AngleMath.Iatan2(y - _sqCenterY, x - _sqCenterX);
        World.Pellets.AddSingle(x, y, angle, speed);
    }

    // ---- patterns ----

    void PatternAimedThenStatic()
    {
        if (PhaseFrame < 100) return;
        if (PhaseFrame == 100)
        {
            SquareInit(1);
            _speed = ByRank(64, 72, 80, 88);
            World.Sound.PlaySe(8);
        }
        if (PhaseFrame % SquareInterval == 0)
        {
            SquareSet();
            TurnSquare(PhaseFrame < 260 ? +0x0C : -0x0C);
            if (SquareRadius < SealCircumsquareRadius)
            {
                SquareRadius += SquareRadiusStep;
            }
            else if (PhaseFrame > 280)
            {
                // The bullets leave from the PREVIOUS corners (the ones computed before the turn).
                for (int i = 0; i < 4; i++)
                {
                    FireStaticFromCorner(_corners[0, i].x, _corners[0, i].y, _speed);
                    World.Sound.PlaySe(7);
                }
            }
            else
            {
                for (int i = 0; i < 4; i++)
                {
                    int tx = PlayerCenterX - Pellets.W / 2, ty = PlayerCenterY;
                    int angle = AngleMath.Iatan2(ty - _corners[0, i].y, tx - _corners[0, i].x);
                    World.Pellets.AddSingle(_corners[0, i].x, _corners[0, i].y, angle, _speed / 2);
                    World.Sound.PlaySe(7);
                }
            }
            SquareSet();
        }
        if (PhaseFrame > 360)
        {
            SquareDeactivate();
            PhaseFrame = 0;
        }
    }

    void PatternAimedMissiles()
    {
        if (PhaseFrame < 100) return;
        if (PhaseFrame == 100)
        {
            SquareInit(1);
            _speed = ByRank(96, 104, 112, 118);
            World.Sound.PlaySe(8);
        }
        if (PhaseFrame % SquareInterval == 0)
        {
            SquareSet();
            TurnSquare(-0x0C);
            if (SquareRadius < SealRadius)
            {
                SquareRadius += SquareRadiusStep;
            }
            else if (PhaseFrame == 224)
            {
                _targetLeft = World.Player.Left;
            }
            else if (PhaseFrame > 240)
            {
                int a = AngleMath.Iatan2(PlayerCenterY - _sqCenterY, _targetLeft + Playfield.PlayerW / 2 - Missiles.W / 2 - _sqCenterX);
                int vx = AngleMath.PolarX(0, _speed, a), vy = AngleMath.PolarY(0, _speed, a);
                // The call site passes whole pixels: the fraction is dropped.
                for (int i = 0; i < 4; i++)
                    World.Missiles.Add(_corners[0, i].x, _corners[0, i].y, (vx >> 4) << 4, (vy >> 4) << 4);
                World.Sound.PlaySe(6);
            }
            SquareSet();
        }
        if (PhaseFrame > 320)
        {
            SquareDeactivate();
            PhaseFrame = 0;
        }
    }

    void PatternTwoSquares()
    {
        if (PhaseFrame == 50) CastVisible = true;
        if (PhaseFrame < 100) return;
        if (PhaseFrame == 100)
        {
            SquareInit(3);
            _speed = ByRank(64, 72, 80, 88);
            World.Sound.PlaySe(8);
        }
        if (PhaseFrame % SquareInterval == 0)
        {
            SquareSet();
            TurnSquare(-0x06);
            World.Pellets.SpawnWithCloud = true;
            if (SquareRadius < SealCircumsquareRadius)
            {
                SquareRadius += SquareRadiusStep;
            }
            else
            {
                for (int i = 0; i < 4; i++)
                {
                    FireStaticFromCorner(_corners[0, i].x, _corners[0, i].y, _speed);
                    FireStaticFromCorner(_corners[1, i].x, _corners[1, i].y, _speed);
                    World.Sound.PlaySe(7);
                }
            }
            SquareSet();
            World.Pellets.SpawnWithCloud = false;
        }
        if (PhaseFrame > 320)
        {
            SquareDeactivate();
            PhaseFrame = 0;
            MeteorActivate();
        }
    }

    void MeteorActivate()
    {
        if (!MeteorActive)
        {
            MeteorActive = true;
            CastVisible = false;
        }
    }

    void HopInit()
    {
        _hop = 255;
        _spreadInInterval = 4;
        _direction = World.Rng.Next() % 2;
        _spreadInSpeed = H / 2 / (20 / 4);
    }

    void PatternHop()
    {
        const int hopDistance = Playfield.Width / 5, keyframeHop = 1, keyframeTeleport = 4, keyframeHitboxActive = 12;
        if (_hop == 255)
        {
            MeteorActive = false;
            CastVisible = false;
            _hop = 0;
            PhaseFrame = keyframeHop;
        }
        if (_hop == 0)
        {
            HitboxOrbInactive = true;
            // The "vertical sprite transition": only counts frames (it never draws anything useful).
            if (PhaseFrame >= 10 && PhaseFrame % 4 == 0)
            {
                int halfH = (PhaseFrame - 10) * 2;
                if (halfH >= H / 2) PhaseFrame = 0;
            }
        }
        else
        {
            if (PhaseFrame == keyframeTeleport)
            {
                Left = _direction == 0
                    ? Playfield.Left + (_hop - 1) * hopDistance
                    : Playfield.Right - _hop * hopDistance;
                Top = BaseTop - Playfield.Height / 14;   // not centred, on purpose
            }
            HitboxOrbInactive = PhaseFrame < keyframeHitboxActive;
            SpreadIn(Left, Top);
        }

        if (PhaseFrame != 0) return;

        if (_hop != 0)
        {
            _speed = ByRank(30, 35, 40, 45);
            int count = ByRank(5, 8, 10, 12);
            int angle = 0;
            for (int i = 0; i < count; i++, angle = (angle + 0x100 / count) & 0xFF)
                World.Pellets.AddSingle(CenterX, CenterY, angle, _speed, PelletMotion.Chase, _speed);
        }
        if (_hop >= 4)
        {
            _hop = 255;
            _direction = 1 - _direction;
            MeteorActive = true;
            PhaseFrame = 0;
        }
        else
        {
            _hop++;
            PhaseFrame = keyframeHop;
        }
    }

    // ---- phase 3 patterns ----

    int RandX(int min, int width) => Playfield.Left + min + World.Rng.Next() % width;

    void PatternPillars()
    {
        if (PhaseFrame == 50) CastVisible = true;
        if (PhaseFrame < KeyframeCircles) return;
        if (PhaseFrame == KeyframeCircles)
        {
            for (int i = 0; i < PillarCount; i++)
            {
                _pTime[i] = TimeCircles;
                _pBottom[i] = Playfield.Bottom;
            }
            // playfield_rand_x(a, b) = fraction(a) + irand() % fraction(b - a), each fraction in whole pixels
            _pCenterX[0] = RandX(16, 48);
            _pCenterX[1] = Playfield.Right - PillarW - RandX(0, 48) + PillarW / 2;
            _pCenterX[2] = RandX(64, 224);
            _pCenterX[3] = RandX(336, 224);
            _pCenterX[4] = RandX(128, 160);
            _pCenterX[5] = RandX(336, 160);
            _pCenterX[6] = RandX(64, 224);
            _pCenterX[7] = RandX(336, 224);
            World.Sound.PlaySe(8);
        }

        bool IsCircleFrame(int i) => PhaseFrame % PillarCount == i && PhaseFrame > KeyframeCircles + i * DelayPerCircle;

        for (int i = 0; i < PillarCount; i++)
            if (IsCircleFrame(i) && _pTime[i] > TimePillars) _pTime[i] -= PillarCount;

        for (int k = 0; k < PillarCount; k++)
            if (PhaseFrame == KeyframeCircles + k * DelayPerCircle) World.Sound.PlaySe(12);
        if (PhaseFrame == KeyframeCircleLast) MeteorActivate();

        for (int i = 0; i < PillarCount; i++)
        {
            if (!IsCircleFrame(i)) continue;
            if (_pTime[i] > TimePillars)
            {
                // drawing: shrinking circle and the ray from the body
            }
            else if (_pTime[i] > TimePillarsDone)
            {
                if (_pTime[i] == TimePillars) World.Sound.PlaySe(7);
                // 8 px wide quarter-box around the pillar centre
                if (!World.PlayerInvincible &&
                    World.Player.Left > _pCenterX[i] - Playfield.PlayerW - PillarW / 4 &&
                    World.Player.Left < _pCenterX[i] + PillarW / 4)
                    World.PlayerIsHit = true;
                _pTime[i] -= PillarCount;
            }
            else if (_pTime[i] != PixelNone)
            {
                World.Pellets.AddGroup(CenterX, CenterY, PelletGroup.ThreeSpreadWideAimed, 36 /* to_sp(2.25) */);
                _pTime[i] = PixelNone;
            }
        }
        bool allDone = true;
        for (int i = 0; i < PillarCount; i++) allDone &= _pTime[i] == PixelNone;
        if (allDone) PhaseFrame = 0;
    }

    /// <summary>Pillar state for the renderer: (time, centre x); time -999 means finished.</summary>
    public (int time, int centerX) Pillar(int i) => (_pTime[i], _pCenterX[i]);

    void PatternHalfcircleMissiles()
    {
        if (PhaseFrame < 100) return;
        if (PhaseFrame == 100)
        {
            SquareInit(1);
            _missileAngle = 0;
            _speed = ByRank(40, 45, 50, 55);          // decimal sub-pixels: 4.0 / 4.5 / 5.0 / 5.5 px
            World.Sound.PlaySe(8);
        }
        if (PhaseFrame % SquareInterval == 0)
        {
            SquareSet();
            TurnSquare(-0x0C);
            if (SquareRadius < SealRadius)
            {
                SquareRadius += SquareRadiusStep;
            }
            else if (PhaseFrame > 180 && PhaseFrame % 16 == 8)
            {
                int whole = _speed / 10;               // the fraction is chopped off (4, 4, 5, 5)
                int vx = AngleMath.PolarX(0, whole, _missileAngle), vy = AngleMath.PolarY(0, whole, _missileAngle);
                for (int i = 0; i < 4; i++)
                    World.Missiles.Add(_corners[0, i].x, _corners[0, i].y, vx << 4, vy << 4);
                World.Sound.PlaySe(6);
                _missileAngle = (_missileAngle + 0x0D) & 0xFF;
            }
            SquareSet();
        }
        if (PhaseFrame > 340)
        {
            SquareDeactivate();
            PhaseFrame = 0;
        }
    }

    void PatternSlowSpray()
    {
        const int keyframeSquare = 100, fireLeftToRight = 180, fireRightToLeft = 270, done = 370;
        if (PhaseFrame < keyframeSquare) return;
        if (PhaseFrame == keyframeSquare)
        {
            SquareInit(1);
            _pelletAngle = 0x80;
            _speed = ByRank(32, 40, 48, 56);
            World.Sound.PlaySe(8);
        }
        if (PhaseFrame % SquareInterval == 0)
        {
            SquareSet();
            SquareAngle = PhaseFrame > fireRightToLeft ? (SquareAngle + 0x0C) & 0xFF : (SquareAngle - 0x0C) & 0xFF;
            if (SquareRadius < SealRadius)
            {
                SquareRadius += SquareRadiusStep;
            }
            else if (PhaseFrame > fireLeftToRight)
            {
                _pelletAngle = (PhaseFrame > fireRightToLeft ? _pelletAngle + 0x0C : _pelletAngle - 0x0C) & 0xFF;
                for (int i = 0; i < 4; i++)
                    World.Pellets.AddSingle(_corners[0, i].x, _corners[0, i].y, _pelletAngle, _speed);
                World.Sound.PlaySe(6);
            }
            SquareSet();
        }
        if (PhaseFrame > done)
        {
            SquareDeactivate();
            PhaseFrame = 0;
        }
    }

    void PatternAimedLasers()
    {
        const int laserW = 4;
        if (PhaseFrame == 50) CastVisible = true;
        if (PhaseFrame < 100) return;
        if (PhaseFrame == 100)
        {
            SquareInit(1);
            _speed = ByRank(50, 54, 58, 62);          // speed * 8 as the laser wants it
            World.Sound.PlaySe(8);
        }
        if (PhaseFrame % SquareInterval == 0)
        {
            SquareSet();
            TurnSquare(+0x03);
            if (SquareRadius < SealRadius) SquareRadius += SquareRadiusStep;
            SquareSet();                              // remembered for the coming frames: no "previous corner" quirk here
        }
        if (PhaseFrame > 180 && PhaseFrame < 300)
        {
            int i = PhaseFrame % 4;
            World.Lasers[i].Spawn(_corners[0, i].x, _corners[0, i].y, PlayerCenterX - laserW / 2, Player.Top + Playfield.PlayerH,
                _speed, 7 /* V_WHITE */, 20, laserW);
        }
        if (PhaseFrame > 300)
        {
            SquareDeactivate();
            PhaseFrame = 0;
        }
    }

    // ---- main ----

    void FrameCommon()
    {
        PhaseFrame++;
        InvincibilityFrame++;
    }

    void NextPhase(int phase)
    {
        Phase = phase;
        PhaseFrame = 0;
        InvincibilityFrame = 0;
        PatternCur = 0;
    }

    void PatternNext(int total) => PatternCur = PatternCur == total - 1 ? 0 : PatternCur + 1;

    void HitUpdate() =>
        HitCommon(OrbPrevInside(8, 88, 8, 120), HitScore, Left + 16, Top + 48, 96, 48);

    public override void Update()
    {
        World.Missiles.Update();
        World.Particles.Update(ParticleOrigin.TopRight);

        if (Phase == 1)
        {
            FrameCommon();
            switch (PatternCur)
            {
                case 0: PatternAimedThenStatic(); break;
                case 1: PatternAimedMissiles(); break;
                case 2: PatternTwoSquares(); break;
                case 3: PatternHop(); break;
            }
            // Wait for the running pattern to end so nothing stays on screen.
            if (PhaseFrame == 0)
            {
                PatternNext(4);
                if (Hp <= HpPhase1End) PatternCur = 99;
            }
            HitUpdate();
            if (PatternCur == 99 && !Invincible)
            {
                _spreadInInterval = 4;
                _spreadInSpeed = 8;
                Left = BaseLeft;
                Top = BaseTop;
                NextPhase(2);
            }
        }
        else if (Phase == 2)
        {
            FrameCommon();
            SpreadIn(BaseLeft, BaseTop);
            if (PhaseFrame == 0) NextPhase(3);
        }
        else if (Phase == 3)
        {
            FrameCommon();
            switch (PatternCur)
            {
                case 0: PatternPillars(); break;
                case 1: PatternHalfcircleMissiles(); break;
                case 2: PatternSlowSpray(); break;
                case 3: PatternAimedLasers(); break;
            }
            if (PhaseFrame == 0) PatternNext(4);
            HitUpdate();
            if (Hp <= HpPhase3End)
            {
                World.Pellets.ResetNonClouds();
                World.Missiles.Reset();
                for (int i = 0; i < 5; i++) World.Lasers.Slots[i].Reset();
                _defeated = true;
            }
        }
    }

    public override void Hash(ref StateHasher h)
    {
        h.Add(Phase); h.Add(PhaseFrame); h.Add(InvincibilityFrame); h.Add(Hp); h.Add(Invincible); h.Add(PatternCur);
        h.Add(Left); h.Add(Top); h.Add(HitboxOrbInactive); h.Add(_defeated);
        h.Add(_spreadInInterval); h.Add(_spreadInSpeed); h.Add(_hop); h.Add(_direction); h.Add(_speed);
        h.Add(_targetLeft); h.Add(_missileAngle); h.Add(_pelletAngle);
        h.Add(SquareAngle); h.Add(SquareFlag); h.Add(SquareRadius); h.Add(_sqCenterX); h.Add(_sqCenterY);
        for (int k = 0; k < 2; k++)
            for (int i = 0; i < 4; i++) { h.Add(_corners[k, i].x); h.Add(_corners[k, i].y); }
        for (int i = 0; i < PillarCount; i++) { h.Add(_pTime[i]); h.Add(_pCenterX[i]); h.Add(_pBottom[i]); }
        h.Add(MeteorActive); h.Add(CastVisible); h.Add(_entranceStarted); h.Add(_entranceWait);
    }
}
