namespace Th01.Core.Game;

/// <summary>
/// The Jigoku stage 10 boss, Mima: a 128x160 sprite with 12 HP, a spread-in entrance, a first phase of four
/// square-seal patterns (6 HP), a re-appearance, and a second phase of four more patterns. State and timing only;
/// the drawing-only parts of the original (unblits, palette tricks, the circles and rays of the pillar pattern) are
/// left to the renderer. Phase 0 (the entrance) is run by the session as its own phase.
/// ref: th01/main/boss/b10j.cpp, boss.cpp, entity_a.cpp
/// </summary>
public sealed class Mima : IBoss
{
    // ---- constants ----
    public const int W = 128, H = 160;
    public const int BaseCenterX = Playfield.CenterX;
    public const int BaseCenterY = Playfield.Top + Playfield.Height / 42 * 17;
    public const int BaseLeft = BaseCenterX - W / 2;
    public const int BaseTop = BaseCenterY - H / 2;
    public const int HpTotal = 12, HpPhase1End = 6, HpPhase3End = 0;
    public const int HitInvincibilityFrames = 40;   // BOSS_HIT_INVINCIBILITY_FRAMES
    public const int HitScore = 5000;
    public const int HitboxShotW = W - W / 8, HitboxShotH = H * 2 / 5;
    public const int AnimTop = 48, AnimH = 64;      // the animated part of the sprite (BOSS3_2.BOS), relative to the top
    public const int MeteorCels = 4;

    const int SealRadius = 80;
    const int SealCircumsquareRadius = 112;         // (int)(SEAL_RADIUS * 1.41f)
    const int SquarePoints = 4, SquareInterval = 8, SquareRadiusStep = 8;
    const int SquareRadiusInitial = 32;             // (int)(SEAL_RADIUS * 0.4f)
    const int KeyframeSpreadinStart = 10;
    const int PixelNone = -999;
    const int PatternPhaseEnd = 99;                 // phase 1 waits for the current pattern to finish, then leaves

    // pillar pattern
    const int PillarCount = 8, PillarDelayPerCircle = 20, PillarW = 32;
    const int KeyframeCircles = 100, TimeCircles = 128, TimePillars = 32, TimePillarsDone = 16;

    // hop pattern
    const int HopDistance = Playfield.Width / 5;
    const int HopSpreadinInterval = 4, HopSpreadinFrames = 20;
    const int KeyframeHop = 1, KeyframeTeleport = 4, KeyframeHitboxActive = 12;

    // Hitbox for the Orb, hitbox_orb_set(16, 16, 112, 144) shrunk by the Orb's own hitbox (see SinGyoku).
    const int OrbHitLeft = W / 8 * 1 + 16 - 24, OrbHitRight = W / 8 * 7 - 16 - 8;
    const int OrbHitTop = H / 10 * 1 + 16 - 24, OrbHitBottom = H / 10 * 9 - 16 - 8;

    readonly GameWorld _w;

    // ---- state (statics of b10j.cpp) ----
    public int Phase { get; set; }                  // boss_phase: 0 entrance, 1, 2 (re-appearing), 3, 8 = defeated
    public int PhaseFrame { get; set; }             // boss_phase_frame
    public int InvincibilityFrame { get; set; }
    public int Hp { get; set; } = HpTotal;
    public bool Invincible { get; set; }
    public int PatternCur { get; set; }
    public int Left { get; set; } = Playfield.CenterX - W / 2;
    public int Top { get; set; } = Playfield.Top;
    public bool HitboxOrbInactive;
    /// <summary>meteor_active: whether the animated part of the sprite shows the meteor (true) or the cast pose.</summary>
    public bool MeteorActive = true;

    int _spreadinInterval = 4;
    int _spreadinSpeed = 8;

    // pattern statics
    int _speed;                     // pattern_state.speed (subpixels, or decimal subpixels for the missile halfcircle)
    int _sqAngle, _sqRadius;        // SquareState (unsigned char angle)
    int _missileAngle;
    int _pelletAngle;
    int _targetLeft;
    int _hop = -1;                  // uint8_t, -1 = not started
    int _hopDirection;              // x_direction_t: X_RIGHT = 0, X_LEFT = 1
    readonly int[] _pillarTime = new int[PillarCount];
    readonly int[] _pillarCenterX = new int[PillarCount];
    readonly int[] _sqCornersX = new int[SquarePoints], _sqCornersY = new int[SquarePoints];   // laser pattern only

    // scratch for one pattern call (locals of the original)
    readonly int[] _cornersX = new int[SquarePoints], _cornersY = new int[SquarePoints];
    readonly int[] _cornersCwX = new int[SquarePoints], _cornersCwY = new int[SquarePoints];

    public Mima(GameWorld w) => _w = w;

    public int CenterX => Left + W / 2;
    public int CenterY => Top + H / 2;
    public bool Defeated => Phase == 8;

    /// <summary>The sprite is gone while she hops between spots (drawing only).</summary>
    public bool Vanished => Phase == 1 && PatternCur == 3 && _hop >= 0 && (_hop == 0 || PhaseFrame < KeyframeSpreadinStart + 1);

    /// <summary>Cel of BOSS3_2.BOS laid over the lower part of the sprite (drawing only).</summary>
    public int AnimCel => MeteorActive ? 1 + PhaseFrame / 8 % MeteorCels : 0;

    public int PillarTimeAt(int i) => _pillarTime[i];
    public int PillarCenterXAt(int i) => _pillarCenterX[i];
    public int Hop => _hop;

    int ByRank(int easy, int normal, int hard, int lunatic) => _w.Rank switch
    {
        Rank.Easy => easy, Rank.Normal => normal, Rank.Hard => hard, Rank.Lunatic => lunatic, _ => normal,
    };

    int PlayerCenterX => _w.Player.Left + Playfield.PlayerW / 2;
    static int PlayerCenterY => Player.Top + Playfield.PlayerH / 2;

    // ---- spread-in (shared by the entrance, the hops and the re-appearance) ----

    int SpreadinBottomCur() => _spreadinSpeed / _spreadinInterval * (PhaseFrame - KeyframeSpreadinStart);

    /// <summary>phase_spreadin(): places her at frame 10 and sets <see cref="PhaseFrame"/> to 0 when the animation is done.</summary>
    void PhaseSpreadin(int finalLeft, int finalTop)
    {
        if (PhaseFrame < KeyframeSpreadinStart) return;
        if (PhaseFrame == KeyframeSpreadinStart)
        {
            Left = finalLeft;
            Top = finalTop;
            return;
        }
        if (PhaseFrame % _spreadinInterval != 0) return;
        if (H / 2 - SpreadinBottomCur() < 0) PhaseFrame = 0;
    }

    // ---- entrance (phase 0) ----

    /// <summary>One frame of the blocking entrance loop; returns true on the last one.</summary>
    public bool EntranceFrame()
    {
        if (PhaseFrame == 0)
        {
            HitboxOrbInactive = false;
            _spreadinInterval = 1;
            _spreadinSpeed = 2;
            MeteorActive = true;
        }
        PhaseFrame++;
        PhaseSpreadin(BaseLeft, BaseTop);
        if (PhaseFrame != 0) return false;
        Phase = 1;
        PatternCur = 0;
        // pattern_hop_and_fire_chase_pellets(false): static initialization
        _hop = -1;
        _spreadinInterval = HopSpreadinInterval;
        _hopDirection = _w.Rng.Next() % 2;
        _spreadinSpeed = H / 2 / (HopSpreadinFrames / HopSpreadinInterval);
        InvincibilityFrame = 0;
        Invincible = false;
        return true;
    }

    // ---- main ----

    /// <summary>mima_main() for phases 1, 2 and 3 (one tick).</summary>
    public void Update()
    {
        var w = _w;
        if (Phase is 0 or 8) return;
        w.Missiles.Update();

        if (Phase == 1)
        {
            FrameCommon();
            switch (PatternCur)
            {
                case 0: PatternAimedThenStaticPellets(); break;
                case 1: PatternAimedMissiles(); break;
                case 2: PatternTwoSquares(); break;
                case 3: PatternHop(); break;
            }
            // Wait for the current pattern to be over before leaving: it may have manipulated the screen.
            if (PhaseFrame == 0)
            {
                PatternNext(4);
                if (Hp <= HpPhase1End) PatternCur = PatternPhaseEnd;
            }
            HitUpdate();
            if (PatternCur == PatternPhaseEnd && !Invincible)
            {
                _spreadinInterval = 4;
                _spreadinSpeed = 8;
                Left = BaseLeft;
                Top = BaseTop;
                NextPhase(2);
            }
        }
        else if (Phase == 2)
        {
            FrameCommon();
            PhaseSpreadin(BaseLeft, BaseTop);
            if (PhaseFrame == 0) NextPhase(3);
        }
        else if (Phase == 3)
        {
            FrameCommon();
            switch (PatternCur)
            {
                case 0: PatternPillarsAndAimedSpreads(); break;
                case 1: PatternHalfcircleMissiles(); break;
                case 2: PatternSlowPelletSpray(); break;
                case 3: PatternAimedLasers(); break;
            }
            if (PhaseFrame == 0) PatternNext(4);
            HitUpdate();
            if (Hp <= HpPhase3End)
            {
                w.Missiles.Reset();
                w.Lasers.ResetFirst(5);
                w.Sound.PlaySe(5);
                Phase = 8;
                PhaseFrame = 0;
            }
        }
    }

    void FrameCommon()
    {
        PhaseFrame++;
        InvincibilityFrame++;
    }

    void PatternNext(int total) => PatternCur = PatternCur == total - 1 ? 0 : PatternCur + 1;

    void NextPhase(int phase)
    {
        Phase = phase;
        PhaseFrame = 0;
        InvincibilityFrame = 0;
        PatternCur = 0;
    }

    // ---- hit handling (boss_hit_update_and_render + hittest_orb) ----

    bool HitTestOrb()
    {
        if (HitboxOrbInactive) return false;
        var o = _w.Orb;
        return Left + OrbHitLeft <= o.PrevLeft && Left + OrbHitRight >= o.PrevLeft
            && Top + OrbHitTop <= o.PrevTop && Top + OrbHitBottom >= o.PrevTop;
    }

    void HitUpdate()
    {
        var w = _w;
        // Player shots are absorbed regardless of invincibility.
        w.Shots.HitTestBoss(Left + W - HitboxShotW, Top + H / 2 - HitboxShotH / 2, HitboxShotW - Shots.W, HitboxShotH - Shots.H);

        bool bombHit = w.RemLives == 0 && w.Bomb.Damaging && w.FrameRand % 50 == 0;
        if ((HitTestOrb() && !Invincible) || bombHit)
        {
            InvincibilityFrame = 0;
            Hp--;
            Invincible = true;
            var o = w.Orb;
            if (o.IsMovingLeft) o.VelocityX = OrbVelocityX.Right4;
            else if (o.IsMovingRight) o.VelocityX = OrbVelocityX.Left4;
            else if (o.VelocityX == OrbVelocityX.Zero)
                o.VelocityX = o.Left < Playfield.CenterX - Playfield.OrbW / 2 ? OrbVelocityX.Right4 : OrbVelocityX.Left4;
            w.Score += HitScore;
            w.Sound.PlaySe(3);
        }
        if (Invincible && InvincibilityFrame < HitInvincibilityFrames)
        {
            // flashing: drawing only
        }
        else if (InvincibilityFrame > HitInvincibilityFrames)
        {
            // Runs even when not invincible (original bug: a periodic palette reset); the state change is harmless.
            InvincibilityFrame = 0;
            Invincible = false;
        }
    }

    // ---- the rotating square seal ----

    void SquareInit()
    {
        _sqRadius = SquareRadiusInitial;
        _sqAngle = 0x00;
    }

    static void Polygon(int[] xs, int[] ys, int cx, int cy, int radius, int angle)
    {
        for (int i = 0; i < SquarePoints; i++)
        {
            xs[i] = AngleMath.PolarX(cx, radius, angle);
            ys[i] = AngleMath.PolarY(cy, radius, angle);
            angle = (angle + 0x100 / SquarePoints) & 0xFF;
        }
    }

    void Corners(int radius, int angle) => Polygon(_cornersX, _cornersY, CenterX, CenterY, radius, angle);

    /// <summary>fire_static_from_corner(): a pellet leaving the square's centre through that corner.</summary>
    void FireStaticFromCorner(int cornerX, int cornerY, int speed)
    {
        int angle = AngleMath.Iatan2(cornerY - CenterY, cornerX - CenterX);
        _w.Pellets.AddSingle(cornerX, cornerY, angle, speed);
    }

    // ---- phase 1 patterns ----

    void PatternAimedThenStaticPellets()
    {
        if (PhaseFrame < 100) return;
        if (PhaseFrame == 100)
        {
            SquareInit();
            _speed = ByRank(64, 72, 80, 88);   // to_sp(4.0, 4.5, 5.0, 5.5)
            _w.Sound.PlaySe(8);
        }
        if (PhaseFrame % SquareInterval == 0)
        {
            // Quirk: the bullets spawn at the previous corner positions, computed for the unblit.
            Corners(_sqRadius, _sqAngle);
            _sqAngle = (_sqAngle + (PhaseFrame < 260 ? +0x0C : -0x0C)) & 0xFF;
            if (_sqRadius < SealCircumsquareRadius)
            {
                _sqRadius += SquareRadiusStep;
            }
            else if (PhaseFrame > 280)
            {
                for (int i = 0; i < SquarePoints; i++)
                {
                    FireStaticFromCorner(_cornersX[i], _cornersY[i], _speed);
                    _w.Sound.PlaySe(7);
                }
            }
            else
            {
                for (int i = 0; i < SquarePoints; i++)
                {
                    int angle = AngleMath.Iatan2(PlayerCenterY - _cornersY[i], PlayerCenterX - Pellets.W / 2 - _cornersX[i]);
                    _w.Pellets.AddSingle(_cornersX[i], _cornersY[i], angle, _speed / 2);
                    _w.Sound.PlaySe(7);
                }
            }
        }
        if (PhaseFrame > 360) PhaseFrame = 0;
    }

    void PatternAimedMissiles()
    {
        if (PhaseFrame < 100) return;
        if (PhaseFrame == 100)
        {
            SquareInit();
            _speed = ByRank(96, 104, 112, 118);   // to_sp(6.0, 6.5, 7.0, 7.375)
            _w.Sound.PlaySe(8);
        }
        if (PhaseFrame % SquareInterval == 0)
        {
            Corners(_sqRadius, _sqAngle);
            _sqAngle = (_sqAngle - 0x0C) & 0xFF;
            if (_sqRadius < SealRadius)
            {
                _sqRadius += SquareRadiusStep;
            }
            else if (PhaseFrame == 224)
            {
                _targetLeft = _w.Player.Left;
            }
            else if (PhaseFrame > 240)
            {
                int toX = _targetLeft + Playfield.PlayerW / 2 - Missiles.W / 2;
                int a = AngleMath.Iatan2(PlayerCenterY - CenterY, toX - CenterX);
                // The velocity is cut down to whole pixels when it is handed to the missile.
                int vx = AngleMath.PolarX(0, _speed, a) >> 4, vy = AngleMath.PolarY(0, _speed, a) >> 4;
                for (int i = 0; i < SquarePoints; i++) _w.Missiles.Add(_cornersX[i], _cornersY[i], vx, vy);
                _w.Sound.PlaySe(6);
            }
        }
        if (PhaseFrame > 320) PhaseFrame = 0;
    }

    void PatternTwoSquares()
    {
        if (PhaseFrame == 50) MeteorActive = false;
        if (PhaseFrame < 100) return;
        if (PhaseFrame == 100)
        {
            SquareInit();
            _speed = ByRank(64, 72, 80, 88);
            _w.Sound.PlaySe(8);
        }
        if (PhaseFrame % SquareInterval == 0)
        {
            Polygon(_cornersX, _cornersY, CenterX, CenterY, _sqRadius, _sqAngle);
            Polygon(_cornersCwX, _cornersCwY, CenterX, CenterY, _sqRadius, (0x00 - _sqAngle) & 0xFF);
            _sqAngle = (_sqAngle - 0x06) & 0xFF;
            _w.Pellets.SpawnWithCloud = true;
            if (_sqRadius < SealCircumsquareRadius)
            {
                _sqRadius += SquareRadiusStep;
            }
            else
            {
                for (int i = 0; i < SquarePoints; i++)
                {
                    FireStaticFromCorner(_cornersX[i], _cornersY[i], _speed);
                    FireStaticFromCorner(_cornersCwX[i], _cornersCwY[i], _speed);
                    _w.Sound.PlaySe(7);
                }
            }
            _w.Pellets.SpawnWithCloud = false;
        }
        if (PhaseFrame > 320)
        {
            PhaseFrame = 0;
            MeteorActive = true;
        }
    }

    void PatternHop()
    {
        if (_hop == -1)
        {
            MeteorActive = false;
            _hop = 0;
            PhaseFrame = KeyframeHop;
        }
        if (_hop == 0)
        {
            HitboxOrbInactive = true;
            // mima_vertical_sprite_transition_broken()
            if (PhaseFrame >= 10 && PhaseFrame % 4 == 0 && (PhaseFrame - 10) * 2 >= H / 2) PhaseFrame = 0;
        }
        else
        {
            if (PhaseFrame == KeyframeTeleport)
            {
                Left = _hopDirection == 0
                    ? Playfield.Left + (_hop - 1) * HopDistance
                    : Playfield.Right - _hop * HopDistance;
                Top = BaseTop - Playfield.Height / 14;   // not centered
            }
            HitboxOrbInactive = PhaseFrame < KeyframeHitboxActive;
            PhaseSpreadin(Left, Top);
        }

        // Spread-in animation done?
        if (PhaseFrame != 0) return;

        if (_hop != 0)
        {
            int speed = ByRank(30, 35, 40, 45);   // to_sp(1.875, 2.1875, 2.5, 2.8125)
            int count = ByRank(5, 8, 10, 12);
            for (int i = 0, angle = 0x00; i < count; i++, angle = (angle + 0x100 / count) & 0xFF)
                _w.Pellets.AddSingle(CenterX, CenterY, angle, speed, PelletMotion.Chase, speed);
        }
        if (_hop >= 4)
        {
            // Prepare a potential next run of this pattern.
            _hop = -1;
            _hopDirection = 1 - _hopDirection;
            MeteorActive = true;
            PhaseFrame = 0;
        }
        else
        {
            _hop++;
            PhaseFrame = KeyframeHop;
        }
    }

    // ---- phase 3 patterns ----

    /// <summary>playfield_rand_x(): the fractions of the playfield width are pre-computed pixel amounts.</summary>
    int RandX(int min, int range) => Playfield.Left + min + _w.Rng.Next() % range;

    void PatternPillarsAndAimedSpreads()
    {
        var w = _w;
        if (PhaseFrame == 50) MeteorActive = false;
        if (PhaseFrame < KeyframeCircles) return;
        if (PhaseFrame == KeyframeCircles)
        {
            for (int i = 0; i < PillarCount; i++) _pillarTime[i] = TimeCircles;
            _pillarCenterX[0] = RandX(16, 48);                              // (0.025, 0.100)
            _pillarCenterX[1] = Playfield.Right - PillarW - RandX(0, 48) + PillarW / 2;   // (0.000, 0.075)
            _pillarCenterX[2] = RandX(64, 224);                             // (0.100, 0.450)
            _pillarCenterX[3] = RandX(336, 224);                            // (0.525, 0.875)
            _pillarCenterX[4] = RandX(128, 160);                            // (0.200, 0.450)
            _pillarCenterX[5] = RandX(336, 160);                            // (0.525, 0.775)
            _pillarCenterX[6] = RandX(64, 224);
            _pillarCenterX[7] = RandX(336, 224);
            w.Sound.PlaySe(8);
        }

        bool IsCircleFrame(int i) => PhaseFrame % PillarCount == i && PhaseFrame > KeyframeCircles + i * PillarDelayPerCircle;

        for (int i = 0; i < PillarCount; i++)
            if (IsCircleFrame(i) && _pillarTime[i] > TimePillars) _pillarTime[i] -= PillarCount;

        for (int k = 0; k < PillarCount; k++)
            if (PhaseFrame == KeyframeCircles + k * PillarDelayPerCircle) w.Sound.PlaySe(12);
        if (PhaseFrame == KeyframeCircles + (PillarCount - 1) * PillarDelayPerCircle) MeteorActive = true;

        for (int i = 0; i < PillarCount; i++)
        {
            if (!IsCircleFrame(i)) continue;
            if (_pillarTime[i] > TimePillars) continue;       // circle and spawn ray: drawing only
            if (_pillarTime[i] > TimePillarsDone)
            {
                if (_pillarTime[i] == TimePillars) w.Sound.PlaySe(7);
                // Reimu's centre has to be at least 24 pixels from the pillar's centre.
                if (!w.PlayerInvincible && w.Player.Left > _pillarCenterX[i] - Playfield.PlayerW - PillarW / 4 &&
                    w.Player.Left < _pillarCenterX[i] + PillarW / 4)
                    w.PlayerIsHit = true;
                _pillarTime[i] -= PillarCount;
            }
            else if (_pillarTime[i] != PixelNone)
            {
                w.Pellets.AddGroup(CenterX, CenterY, PelletGroup.ThreeSpreadWideAimed, 36 /* to_sp(2.25) */);
                _pillarTime[i] = PixelNone;
            }
        }
        bool done = true;
        for (int i = 0; i < PillarCount; i++) done &= _pillarTime[i] == PixelNone;
        if (done) PhaseFrame = 0;
    }

    void PatternHalfcircleMissiles()
    {
        if (PhaseFrame < 100) return;
        if (PhaseFrame == 100)
        {
            SquareInit();
            _missileAngle = 0x00;
            _speed = ByRank(40, 45, 50, 55);   // to_dsp(4.0, 4.5, 5.0, 5.5): the fraction is chopped off, so 4, 4, 5, 5
            _w.Sound.PlaySe(8);
        }
        if (PhaseFrame % SquareInterval == 0)
        {
            Corners(_sqRadius, _sqAngle);
            _sqAngle = (_sqAngle - 0x0C) & 0xFF;
            if (_sqRadius < SealRadius)
            {
                _sqRadius += SquareRadiusStep;
            }
            else if (PhaseFrame > 180 && PhaseFrame % 16 == 8)
            {
                int len = _speed / 10;
                int vx = AngleMath.PolarX(0, len, _missileAngle), vy = AngleMath.PolarY(0, len, _missileAngle);
                for (int i = 0; i < SquarePoints; i++) _w.Missiles.Add(_cornersX[i], _cornersY[i], vx, vy);
                _w.Sound.PlaySe(6);
                _missileAngle = (_missileAngle + 0x0D) & 0xFF;
            }
        }
        if (PhaseFrame > 340) PhaseFrame = 0;
    }

    void PatternSlowPelletSpray()
    {
        const int keyframeSquare = 100, keyframeFireLeftToRight = 180, keyframeFireRightToLeft = 270, keyframeDone = 370;
        if (PhaseFrame < keyframeSquare) return;
        if (PhaseFrame == keyframeSquare)
        {
            SquareInit();
            _pelletAngle = 0x80;
            _speed = ByRank(32, 40, 48, 56);   // to_sp(2.0, 2.5, 3.0, 3.5)
            _w.Sound.PlaySe(8);
        }
        if (PhaseFrame % SquareInterval == 0)
        {
            Corners(_sqRadius, _sqAngle);
            _sqAngle = (_sqAngle + (PhaseFrame > keyframeFireRightToLeft ? +0x0C : -0x0C)) & 0xFF;
            if (_sqRadius < SealRadius)
            {
                _sqRadius += SquareRadiusStep;
            }
            else if (PhaseFrame > keyframeFireLeftToRight)
            {
                // slightly overshooting the half circle on the way back
                _pelletAngle = (_pelletAngle + (PhaseFrame > keyframeFireRightToLeft ? +0x0C : -0x0C)) & 0xFF;
                for (int i = 0; i < SquarePoints; i++)
                    _w.Pellets.AddSingle(_cornersX[i], _cornersY[i], _pelletAngle, _speed);
                _w.Sound.PlaySe(6);
            }
        }
        if (PhaseFrame > keyframeDone) PhaseFrame = 0;
    }

    void PatternAimedLasers()
    {
        if (PhaseFrame == 50) MeteorActive = false;
        if (PhaseFrame < 100) return;
        if (PhaseFrame == 100)
        {
            SquareInit();
            _speed = ByRank(50, 54, 58, 62);   // select_laser_speed_for_rank: to_sp(6.25 / 2, ...)
            _w.Sound.PlaySe(8);
        }
        if (PhaseFrame % SquareInterval == 0)
        {
            _sqAngle = (_sqAngle + 0x03) & 0xFF;
            if (_sqRadius < SealRadius) _sqRadius += SquareRadiusStep;
            // The corners are remembered, so the lasers use the current ones (unlike the other patterns).
            Polygon(_sqCornersX, _sqCornersY, CenterX, CenterY, _sqRadius, _sqAngle);
        }
        if (PhaseFrame > 180 && PhaseFrame < 300)
        {
            int i = PhaseFrame % SquarePoints;
            _w.Lasers.Safe(i).Spawn(_sqCornersX[i], _sqCornersY[i], PlayerCenterX - ShootoutLaser.LaserW / 2,
                Player.Top + Playfield.PlayerH, _speed, 15, 20, ShootoutLaser.LaserW);
        }
        if (PhaseFrame > 300)
        {
            PhaseFrame = 0;
            MeteorActive = true;
        }
    }

    public void Hash(ref StateHasher h)
    {
        h.Add(Phase); h.Add(PhaseFrame); h.Add(InvincibilityFrame); h.Add(Hp); h.Add(Invincible);
        h.Add(PatternCur); h.Add(Left); h.Add(Top); h.Add(HitboxOrbInactive); h.Add(MeteorActive);
        h.Add(_spreadinInterval); h.Add(_spreadinSpeed); h.Add(_speed); h.Add(_sqAngle); h.Add(_sqRadius);
        h.Add(_missileAngle); h.Add(_pelletAngle); h.Add(_targetLeft); h.Add(_hop); h.Add(_hopDirection);
        for (int i = 0; i < PillarCount; i++) { h.Add(_pillarTime[i]); h.Add(_pillarCenterX[i]); }
        for (int i = 0; i < SquarePoints; i++) { h.Add(_sqCornersX[i]); h.Add(_sqCornersY[i]); }
    }
}
