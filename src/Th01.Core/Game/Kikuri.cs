namespace Th01.Core.Game;

/// <summary>
/// The Jigoku stage 15 boss, Kikuri: a disc with two souls and 14 HP, hit only by the Orb and only in a strip at the top
/// of the field. Phase 2 is a spiral of pellets, phase 3 a colour fade, phase 4 spinning rings and the souls rising out of
/// the light ball, phase 5 tears that turn into ripples on the floor plus crossed eye lasers, phase 6 four patterns in a
/// loop. State and timing only. Phase 0 (barn door transition, then the entrance rings) is run by the session.
/// ref: th01/main/boss/b15j.cpp, boss.cpp, entity_a.cpp
/// </summary>
public sealed class Kikuri : Boss
{
    // ---- constants ----
    public const int DiscCenterX = 320, DiscCenterY = 180, DiscRadius = 90;
    public const int EyeLeftCenterX = 307, EyeRightCenterX = 336, EyeBottom = 147;
    public const int LightballCenterX = 320, LightballCenterY = 224;
    public const int HpTotal = 14, HpPhase2End = 10, HpPhase5End = 6, HpPhase6End = 0;
    public const int HitScore = 7000;
    public const int SoulW = 32, SoulH = 32, SoulCels = 3, SoulCount = 2;
    public const int TearW = 16, TearH = 16, TearCount = 10, RippleW = 16;
    public const int RingsFrames = 100;             // ASSUMPTION (the helper that draws the entrance rings was not found). TODO(T5)

    const int BarnDoorYEnd = 400 / 2 + 24;
    const int SoulAreaLeft = Playfield.Left + Playfield.Width / 20;
    const int SoulAreaTop = Playfield.Top;
    const int SoulAreaRight = Playfield.Right - Playfield.Width / 20 - SoulW;
    const int SoulAreaBottom = Playfield.Bottom;
    const int TearTopMax = Playfield.Bottom - TearH;
    const int ColLaser = 10;
    public const int Id = 3 * StageSetup.StagesPerScene - 1;   // stage 15, Jigoku route only
    public const int HpFirstRedWhiteValue = HpPhase5End, HpFirstWhiteValue = HpPhase2End;

    // kikuri_hittest_orb(): the Orb's TOP is compared with "<" against both edges, so the strip runs from the top of the
    // field down to y = 132 (original bug: it should have been ">" for the top edge).
    const int HitboxLeft = DiscCenterX - 128 / 2;
    const int HitboxRight = DiscCenterX + 128 / 2 - Playfield.OrbW;
    const int HitboxCenterY = DiscCenterY - 8;
    const int HitboxTop = HitboxCenterY - 80 / 2;
    const int HitboxBottom = HitboxCenterY + 80 / 2 - Playfield.OrbH;

    public const int P4SoulActivation = 0, P4Pattern = 1, P4Done = 2;

    /// <summary>An entity that can only move every other call (lock_frame) and stays inside a clamp box.</summary>
    public sealed class Mover
    {
        public int Left, Top, LockFrame, Image;

        public void PosSet(int left, int top)
        {
            Left = left;
            Top = top;
        }

        /// <summary>locked_move_unput_and_put_8(0, dx, dy, 1): moves on every other call, then clamps.</summary>
        public void LockedMove(int dx, int dy)
        {
            if (LockFrame == 0)
            {
                Left = Math.Min(Math.Max(Left + dx, SoulAreaLeft), SoulAreaRight);
                Top = Math.Min(Math.Max(Top + dy, SoulAreaTop), SoulAreaBottom);
                LockFrame = 1;
            }
            else if (LockFrame >= 1) LockFrame = 0;
            else LockFrame++;
        }

        public void Hash(ref StateHasher h)
        {
            h.Add(Left); h.Add(Top); h.Add(LockFrame); h.Add(Image);
        }
    }

    // ---- state (InvincibilityFrame doubles as the barn door's line counter during the entrance) ----
    public override bool Defeated => Phase == 8;

    public readonly Mover[] Souls = { new(), new() };
    public readonly Mover[] Tears = new Mover[TearCount];
    /// <summary>tear_anim_frame: nonzero = alive (falling while 1, then the ripple's frame counter).</summary>
    public readonly int[] TearAnimFrame = new int[TearCount];

    int _entranceStage;             // 0 barn door, 1 entrance rings
    int _patternsDone;
    int _subphase4;                 // union with phase_6_pattern in the original
    int _pattern6;
    int _state;                     // pattern_state: an interval or a laser speed, shared by all patterns
    int _spiralAngle, _spiralDrift, _spiralDistance;
    int _discAngle;                 // pattern_4_spiral_along_disc
    int _randomRangeXHalf;

    public Kikuri(GameWorld w) : base(w)
    {
        Hp = HpTotal;
        Left = DiscCenterX - DiscRadius;
        Top = DiscCenterY - DiscRadius;
        for (int i = 0; i < TearCount; i++) Tears[i] = new Mover();
    }

    public override int Width => 2 * DiscRadius;
    public override int Height => 2 * DiscRadius;

    public override IReadOnlyList<int> FlashColors { get; } = new[] { 6, 11, 8, 2 };
    public override int HpFirstRedWhite => HpFirstRedWhiteValue;
    public override int HpFirstWhite => HpFirstWhiteValue;

    /// <summary>Ticks of the white-out and fade: the lines start at the Orb, 2 rows per tick (assumption until measured).</summary>
    public override int DefeatTicks()
    {
        int startY = World.Orb.Top + Playfield.OrbH / 2;
        return Math.Max(startY + 1, Playfield.Bottom - startY) / DefeatLineDistance + 15 * DefeatWhiteinInterval;
    }

    /// <summary>Entrance drawing state: which of the two transitions is running.</summary>
    public int EntranceStage => _entranceStage;
    public bool SoulsActive => Phase >= 4 || Phase == 8;

    int ByRank(int easy, int normal, int hard, int lunatic) => World.Rank switch
    {
        Rank.Easy => easy, Rank.Normal => normal, Rank.Hard => hard, Rank.Lunatic => lunatic, _ => normal,
    };

    int PlayerCenterX => World.Player.Left + Playfield.PlayerW / 2;
    static int PlayerCenterY => Player.Top + Playfield.PlayerH / 2;

    // ---- entrance (phase 0) ----

    /// <summary>
    /// One tick of the two blocking entrance loops: the barn door (one iteration per frame, a new line every 3rd) and the
    /// entrance rings (a frame_delay only after the odd iterations). Returns true on the last one.
    /// </summary>
    public override bool EntranceFrame()
    {
        if (_entranceStage == 0)
        {
            if (PhaseFrame == 0 && InvincibilityFrame == 0) Invincible = false;
            PhaseFrame++;
            if (PhaseFrame % 3 == 0)
            {
                InvincibilityFrame++;
                if (InvincibilityFrame > BarnDoorYEnd)
                {
                    InvincibilityFrame = 0;
                    PhaseFrame = 0;
                    _entranceStage = 1;
                }
            }
            return false;
        }
        do
        {
            PhaseFrame++;
            if (PhaseFrame >= RingsFrames)
            {
                Phase = 2;
                _patternsDone = 0;
                PhaseFrame = 0;
                return true;
            }
        } while (PhaseFrame % 2 == 0);
        return false;
    }

    // ---- main ----

    /// <summary>kikuri_main() for phases 2..6 (one tick).</summary>
    public override void Update()
    {
        var w = World;
        switch (Phase)
        {
            case 2:
                FrameCommon();
                PatternSymmetricSpiralFromDisc();
                HitUpdate();
                if (PhaseFrame == 0) _patternsDone++;
                if (!Invincible && (Hp <= HpPhase2End || _patternsDone >= 6))
                {
                    Phase = 3;
                    PhaseFrame = 0;
                    InvincibilityFrame = 0;
                    _patternsDone = 0;
                }
                break;
            case 3:
                PhaseFrame++;
                // The light ball's colour fades from (B, B, F) to (F, B, A) in 5 steps, one every 20 frames.
                if (PhaseFrame >= 20 * (0xF - 0xA))
                {
                    Phase = 4;
                    PhaseFrame = 0;
                    _subphase4 = P4SoulActivation;
                }
                break;
            case 4:
                FrameCommon();
                PatternSpinningAimedRings();
                _subphase4 = _subphase4 == P4SoulActivation ? Phase4SoulsActivate() : PatternSoulsSpreads();
                HitUpdate();
                if (!Invincible && _subphase4 == P4Done)
                {
                    Phase = 5;
                    _pattern6 = 0;
                    PhaseFrame = 0;
                }
                break;
            case 5:
                FrameCommon();
                PatternSoulsDropTearsAndMoveDiagonally();
                PatternTwoCrossedEyeLasers();
                HitUpdate();
                if (!Invincible && (Hp <= HpPhase5End || PhaseFrame > 1600))
                {
                    Phase = 6;
                    PhaseFrame = 0;
                    InvincibilityFrame = 0;
                    _patternsDone = 0;
                }
                break;
            case 6:
                FrameCommon();
                PatternSoulsSingleAimedPelletAndMoveDiagonally();
                _pattern6 = _pattern6 switch
                {
                    0 => Pattern4SpiralAlongDisc(),
                    1 => PatternSingleLasersFromLeftEye(),
                    2 => PatternSoulsSymmetricRainLines(),
                    3 => PatternVerticalLasersFromTop(),
                    _ => _pattern6,
                };
                if (PhaseFrame == 0) _patternsDone++;
                // A well-hidden timeout: after 30 patterns the next hit ends the fight.
                if (_patternsDone > 29) Hp = 1;
                HitUpdate();
                if (Hp <= HpPhase6End)
                {
                    for (int i = 0; i < 4; i++) w.Lasers.Slots[i].Reset();   // "4? Doubly broken": only the first four
                    Phase = 8;
                    PhaseFrame = 0;
                }
                break;
        }
        PatternCur = Phase == 4 ? _subphase4 : Phase == 6 ? _pattern6 : 0;
    }

    void FrameCommon()
    {
        PhaseFrame++;
        InvincibilityFrame++;
    }

    // ---- hit handling ----

    bool HitTestOrb()
    {
        var o = World.Orb;
        return o.Left > HitboxLeft && o.Left < HitboxRight && o.Top < HitboxTop && o.Top < HitboxBottom;
    }

    // The original passes no shot hitbox here (assumption: shots go through the disc). TODO(T5)
    void HitUpdate() => HitCommon(HitTestOrb(), HitScore, 0, 0, -1, -1);

    // ---- souls and tears ----

    void SoulMove(int i, int dx, int dy)
    {
        var s = Souls[i];
        s.LockedMove(dx, dy);
        if (PhaseFrame % 12 == 0) s.Image = s.Image >= SoulCels - 1 ? 0 : s.Image + 1;
    }

    int SoulCenterX(int i) => Souls[i].Left + SoulW / 2;
    int SoulCenterY(int i) => Souls[i].Top + SoulH / 2;

    void TearsAdd(int left, int top)
    {
        for (int i = 0; i < TearCount; i++)
        {
            if (TearAnimFrame[i] != 0) continue;
            Tears[i].PosSet(left, top);
            Tears[i].Image = 0;
            TearAnimFrame[i] = 1;
            return;
        }
    }

    /// <summary>tear_ripple_hittest(): the box is shifted up to 7 pixels right of what is drawn (byte-aligned sprites).</summary>
    void TearRippleHittest(int left, int extraW)
    {
        var w = World;
        if (w.PlayerInvincible) return;
        if (w.Player.Left >= left - (Playfield.PlayerW / 4 + RippleW / 2) && w.Player.Left <= left + extraW)
            w.PlayerIsHit = true;
    }

    void TearsUpdate()
    {
        for (int i = 0; i < TearCount; i++)
        {
            if (TearAnimFrame[i] == 0) continue;
            if (Tears[i].Top <= TearTopMax) Tears[i].LockedMove(0, +8);
            else TearAnimFrame[i] = RippleUpdate(Tears[i].Left, TearAnimFrame[i]);
        }
    }

    /// <summary>ripple_update_and_render(): only the hit tests and the sound; returns the new frame counter.</summary>
    int RippleUpdate(int tearLeft, int frame)
    {
        frame++;
        if (frame < 5)
        {
            TearRippleHittest(tearLeft, 0);
            World.Sound.PlaySe(7);
        }
        else if (frame < 10) TearRippleHittest(tearLeft, 0);
        else if (frame < 20) TearRippleHittest(tearLeft - RippleW, RippleW * 2);
        else if (frame < 60) TearRippleHittest(tearLeft - RippleW * 2, RippleW * 4);
        else if (frame >= 75) frame = 0;
        return frame;
    }

    void SoulsMoveDiagonallyAndTearsUpdate(int velocityBaseX, int velocityBaseY)
    {
        int vx = AngleMath.PolarX(0, velocityBaseX, PhaseFrame);
        // polar_x() rather than polar_y() is what makes the movement diagonal instead of elliptic.
        int vy = AngleMath.PolarX(0, velocityBaseY, PhaseFrame);
        TearsUpdate();
        SoulMove(0, +vx, vy);
        SoulMove(1, -vx, vy);
    }

    // ---- phase 2 ----

    void PatternSymmetricSpiralFromDisc()
    {
        if (PhaseFrame < 100) return;
        if (PhaseFrame == 100)
        {
            _spiralAngle = 0x00;
            _spiralDrift = 0x00;
            _state = ByRank(4, 3, 2, 1);
            _spiralDistance = ByRank(DiscRadius, DiscRadius, DiscRadius, DiscRadius / 2 + 5);
        }
        if (PhaseFrame % _state != 0) return;
        for (int k = 0; k < 2; k++)
        {
            int a = (_spiralAngle + k * 0x80) & 0xFF;
            int left = AngleMath.PolarX(DiscCenterX, _spiralDistance, a);
            int top = AngleMath.PolarY(DiscCenterY, _spiralDistance, a);
            World.Pellets.AddSingle(left, top, (a + _spiralDrift) & 0xFF, 50 /* to_sp(3.125) */);
        }
        if (PhaseFrame >= 900) PhaseFrame = 0;
        _spiralAngle = (_spiralAngle - 0x08) & 0xFF;
        _spiralDrift = (_spiralDrift + 1) & 0xFF;
    }

    // ---- phase 4 ----

    void PatternSpinningAimedRings()
    {
        const int ring = 8;
        int interval = ByRank(80, 60, 55, 50);
        if (PhaseFrame % interval != 0) return;
        for (int i = 0; i < ring; i++)
        {
            int a = i * (0x100 / ring);
            int left = AngleMath.PolarX(LightballCenterX - Pellets.W / 2, 16, a);
            int top = AngleMath.PolarY(LightballCenterY, 16, a);
            int aim = AngleMath.Iatan2(PlayerCenterY + Pellets.H / 2 - LightballCenterY, PlayerCenterX + Pellets.W / 2 - LightballCenterX);
            World.Pellets.AddSingle(left, top, aim, 48 /* to_sp(3.0) */, PelletMotion.Spin, 32 /* to_sp(2.0) */,
                LightballCenterX - Pellets.W, LightballCenterY);
        }
    }

    int Phase4SoulsActivate()
    {
        if (PhaseFrame == 200)
        {
            for (int i = 0; i < SoulCount; i++) Souls[i].PosSet(LightballCenterX - SoulW / 2, LightballCenterY - SoulH / 2);
        }
        else if (PhaseFrame > 200 && PhaseFrame % 2 == 0)
        {
            SoulMove(0, -4, -3);
            SoulMove(1, +4, -3);
            if (Souls[0].Top < SoulAreaTop + 32 /* playfield_fraction_y(2 / 21.0f) */) return P4Pattern;
        }
        return P4SoulActivation;
    }

    int PatternSoulsSpreads()
    {
        SoulMove(0, 0, 0);
        SoulMove(1, 0, 0);
        if (PhaseFrame % 80 == 0)
        {
            for (int i = 0; i < SoulCount; i++)
            {
                int left = SoulCenterX(i) - Pellets.W / 2, top = SoulCenterY(i) - Pellets.H;
                // Not aimed on Lunatic.
                var group = World.Rank switch
                {
                    Rank.Easy => PelletGroup.TwoSpreadWideAimed,
                    Rank.Normal => PelletGroup.ThreeSpreadWideAimed,
                    Rank.Hard => PelletGroup.FiveSpreadWideAimed,
                    Rank.Lunatic => PelletGroup.FiveSpreadWide,
                    _ => PelletGroup.ThreeSpreadWideAimed,
                };
                World.Pellets.AddGroup(left, top, group, 55 /* to_sp(3.4375) */);
            }
        }
        // PhaseFrame carries over from the activation.
        return PhaseFrame > 600 ? P4Done : P4Pattern;
    }

    // ---- phase 5 ----

    void PatternSoulsDropTearsAndMoveDiagonally()
    {
        if (PhaseFrame < 10) _state = ByRank(200, 160, 140, 120);
        if (_state > 0 && PhaseFrame % _state == 0)
        {
            TearsAdd(SoulCenterX(0) - TearW / 4, SoulCenterY(0) - TearH / 2);   // not half, in the original too
            TearsAdd(SoulCenterX(1) - TearW / 4, SoulCenterY(1) - TearH / 2);
        }
        SoulsMoveDiagonallyAndTearsUpdate(6, 2);
    }

    void PatternTwoCrossedEyeLasers()
    {
        // The first firing is on frame 400.
        if (PhaseFrame > 200 && PhaseFrame % 200 == 0)
        {
            const int laserW = 8, offsetX = Playfield.Width / 10 - laserW / 2;
            const int speed = 100 / 2;   // to_sp(6.25) / 2
            World.Lasers[0].Spawn(EyeLeftCenterX, EyeBottom, World.Player.Left + offsetX, Playfield.Bottom, speed, ColLaser, 20, laserW);
            World.Lasers[1].Spawn(EyeRightCenterX, EyeBottom, World.Player.Left - offsetX, Playfield.Bottom, speed, ColLaser, 20, laserW);
            World.Sound.PlaySe(6);
        }
    }

    // ---- phase 6 ----

    void PatternSoulsSingleAimedPelletAndMoveDiagonally()
    {
        if (PhaseFrame % 200 == 0)
        {
            for (int i = 0; i < SoulCount; i++)
                World.Pellets.AddGroup(SoulCenterX(i) - Pellets.W / 2, SoulCenterY(i) - Pellets.H / 2, PelletGroup.OneAimed, 40 /* to_sp(2.5) */);
        }
        SoulsMoveDiagonallyAndTearsUpdate(4, 2);
    }

    int Pattern4SpiralAlongDisc()
    {
        if (PhaseFrame < 100) return 0;
        if (PhaseFrame == 100)
        {
            _discAngle = 0x20;
            _state = ByRank(14, 8, 6, 4);
        }
        if (_state > 0 && PhaseFrame % _state == 0)
        {
            const int ring = 2;
            for (int i = 0; i < ring * 2; i += 2)
            {
                int a = (PhaseFrame * 4 + 0x100 / ring * i) & 0xFF;
                int left = AngleMath.PolarX(DiscCenterX, DiscRadius, a);
                int top = AngleMath.PolarY(DiscCenterY, DiscRadius, a);
                for (int k = 0; k < 4; k++)
                    World.Pellets.AddSingle(left, top, (_discAngle + k * 0x40) & 0xFF, 36 /* to_sp(2.25) */);
            }
            if (PhaseFrame <= 600)
            {
                _discAngle = (_discAngle - 0x05) & 0xFF;
            }
            else
            {
                PhaseFrame = 0;
                return 1;
            }
        }
        return 0;
    }

    void FireRandomAimedEyeLaser(int i, int originLeft, int aimRangeX, int speed, int w)
    {
        int offset = World.Rng.Next() % (aimRangeX * 2) - aimRangeX;
        World.Lasers[i].Spawn(originLeft, EyeBottom, World.Player.Left + offset, Playfield.Bottom, speed, ColLaser, 20, w);
    }

    int PatternSingleLasersFromLeftEye()
    {
        const int originX = EyeLeftCenterX, laserW = 4, rangeX = Playfield.Width / 10 - laserW;
        if (PhaseFrame <= 200) return 1;
        if (PhaseFrame == 250)
        {
            _state = ByRank(52, 56, 60, 64);   // select_laser_speed_for_rank: to_sp(6.5 / 2, ...)
            World.Sound.PlaySe(6);
            FireRandomAimedEyeLaser(0, originX, rangeX, _state, laserW);
        }
        else if (PhaseFrame is 280 or 310 or 340 or 370 or 400)
        {
            World.Sound.PlaySe(6);
            FireRandomAimedEyeLaser((PhaseFrame - 250) / 30, originX, rangeX, _state, laserW);
        }
        if (PhaseFrame > 500)
        {
            PhaseFrame = 0;
            return 2;
        }
        return 1;
    }

    int PatternSoulsSymmetricRainLines()
    {
        // The state is still the laser speed of the previous pattern, reinterpreted as an interval (52, 56, 60, 64):
        // the "== 100" initialisation below never runs, as in the original.
        if (PhaseFrame > 100)
        {
            if (PhaseFrame == 100) _state = ByRank(35, 25, 23, 22);
            if (_state > 0 && PhaseFrame % _state == 0)
            {
                for (int i = 0; i < SoulCount; i++)
                {
                    int left = SoulCenterX(i) - Pellets.W / 2, top = SoulCenterY(i) - Pellets.H / 2;
                    int[] angles = { 0x00, 0x00, 0x00, 0x80, 0x80, 0x80 };
                    int[] speeds = { 48, 32, 16, 16, 32, 48 };   // to_sp(3, 2, 1, 1, 2, 3)
                    for (int k = 0; k < 6; k++)
                        World.Pellets.AddSingle(left, top, angles[k], speeds[k], PelletMotion.Gravity, 1 /* to_sp(0.0625) */);
                    World.Sound.PlaySe(7);
                }
            }
        }
        if (PhaseFrame > 250)
        {
            PhaseFrame = 0;
            return 3;
        }
        return 2;
    }

    int PatternVerticalLasersFromTop()
    {
        const int laserCount = 10, interval = 10, distanceX = Playfield.Width / laserCount;
        const int keyframeStart = 100, keyframeEnd = keyframeStart + (laserCount - 1) * interval;
        if (PhaseFrame < keyframeStart) return 3;
        if (PhaseFrame == keyframeStart)
        {
            _state = ByRank(60, 64, 68, 72);   // to_sp(7.5 / 2, ...)
            _randomRangeXHalf = ByRank(0, Playfield.Width / 64, Playfield.Width / 40, Playfield.Width / 32);
        }
        if (PhaseFrame % interval == 0)
        {
            int i = (PhaseFrame - keyframeStart) / interval;
            int offset = World.Rng.Next() % (_randomRangeXHalf * 2 + 1) - _randomRangeXHalf;
            int x = Playfield.Left + i * distanceX + offset;
            World.Lasers[i].Spawn(x, Playfield.Top, x, Playfield.Bottom, _state, ColLaser, 20, 8);
            World.Sound.PlaySe(6);
        }
        if (PhaseFrame >= keyframeEnd)
        {
            PhaseFrame = 0;
            return 0;
        }
        return 3;
    }

    public override void Hash(ref StateHasher h)
    {
        h.Add(Phase); h.Add(PhaseFrame); h.Add(InvincibilityFrame); h.Add(Hp); h.Add(Invincible);
        h.Add(_entranceStage); h.Add(_patternsDone); h.Add(_subphase4); h.Add(_pattern6); h.Add(_state);
        h.Add(_spiralAngle); h.Add(_spiralDrift); h.Add(_spiralDistance); h.Add(_discAngle); h.Add(_randomRangeXHalf);
        foreach (var s in Souls) s.Hash(ref h);
        for (int i = 0; i < TearCount; i++) { Tears[i].Hash(ref h); h.Add(TearAnimFrame[i]); }
    }
}
