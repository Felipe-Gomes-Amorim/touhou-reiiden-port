namespace Th01.Core.Game;

/// <summary>
/// Elis, the stage 15 boss of the Makai route: a 128x96 girl who teleports in a wave effect, fires lasers, missiles and
/// pellet rings in phases 1 and 3 (the latter behind a Star of David), and in phase 5 turns into a 48x32 bat that flies
/// around while the girl's patterns alternate with it. 14 HP. State and timing only: waves, circles, rifts, spheres and
/// stars are drawing in the original and are left to the renderer (or not drawn yet).
/// ref: th01/main/boss/b15m.cpp
/// </summary>
public sealed class Elis : Boss
{
    public const int GirlW = 128, GirlH = 96, BatW = 48, BatH = 32;
    public const int Id = 3 * StageSetup.StagesPerScene - 1;   // stage 15, Makai route only
    public const int BaseCenterX = Playfield.CenterX;
    public const int BaseCenterY = Playfield.Top + Playfield.Height / 21 * 5;
    public const int BaseLeft = BaseCenterX - GirlW / 2;
    public const int BaseTop = BaseCenterY - GirlH / 2;
    public const int HpTotal = 14, HpPhase1End = 10, HpPhase3End = 6, HpPhase5End = 0;
    public const int HitScore = 7000;
    public const int BatCels = 3, BatFramesPerCel = 3, BatCycleFrames = BatCels * BatFramesPerCel, BatSpeedDivisor = 3;
    public const int SphereW = 32, SphereH = 32;

    // elis_entity_cel_t
    public const int CStill = 0, CHand = 1, CWave1 = 2, CWave2 = 3, CWave3 = 4, CPrepare = 0, CAttack1 = 1, CAttack2 = 2;

    const int BigCircleRadius = GirlW * 2 / 2;
    const int SurroundAreaW = Playfield.Width * 3 / 10;
    const int SurroundAreaH = Playfield.Height * 8 / 21;
    const int ColWhite = 7;
    const int Choose = 0;                                     // CHOOSE_NEW
    const int TransformStart = 20, TransformEnd = 100;

    // form (elis_form_t)
    public const int FGirl = 0, FBat = 1;

    // ---- entrance timing (the loops are blocking in the original) ----
    int _entranceStage;               // 0 trailing spheres, 1 spheres leave / the girl waves in
    bool _entranceStarted;
    int _entranceWait;

    // ---- state of elis_main() and the statics around it ----
    public int Form;                  // static elis_form_t form
    int _cur;                         // phase.cur: pattern id, or the form in phase 4 (a union in the original)
    bool _teleportDone;
    int _batVx, _batVy;
    int _batLock;                     // CBossEntity::lock_frame of the bat
    public int BatLeft, BatTop, BatImage;
    bool _defeated;

    // wave_teleport(): five stars
    public readonly int[] StarLeft = new int[5], StarTop = new int[5];

    // pattern_state (one union shared by every pattern)
    int _state;

    // pattern_11_lasers_across
    int _lasersDirection;
    // rifts of the patterns that have them (static EntitiesTopleft<5> each)
    readonly int[] _riftsMissileL = new int[5], _riftsMissileT = new int[5];
    readonly int[] _riftsFromL = new int[5], _riftsFromT = new int[5];
    readonly int[] _riftsGirlToBatL = new int[5], _riftsGirlToBatT = new int[5];
    readonly int[] _riftsBatToGirlL = new int[5], _riftsBatToGirlT = new int[5];
    // StarCircle statics (angle, frame)
    int _ringsAngle, _ringsFrame;                      // pattern_pellets_along_circle
    int _starAngle, _starFrame;                        // star_of_david
    int _safetyFrame, _safetyTargetLeft, _safetyAngle; // pattern_safety_circle_and_rain_from_top
    int _fiveAngle, _fiveFrame;                        // pattern_aimed_5_spreads_and_lasers_followed_by_ring
    // phase 3
    int _phase3Cur;                                    // phase_3()'s static pattern_cur
    // spheres of pattern_clusters_from_spheres
    readonly int[] _sphereL = new int[10], _sphereT = new int[10];
    // phase 5
    int _p5Subphase;                                   // P5_PATTERN = 0, P5_TRANSFORM = 1
    int _p5BatPattern;
    int _p5GirlPattern;
    int _p5GirlSubphase;                               // SP_STAR_OF_DAVID = 0, SP_PATTERN = 1
    int _batTargetFrames;

    public Elis(GameWorld w) : base(w)
    {
        Hp = HpTotal;
        Left = BaseLeft;
        Top = BaseTop;
        BatLeft = BaseLeft;
        BatTop = BaseTop;
    }

    public override int Width => GirlW;
    public override int Height => GirlH;
    public override bool Defeated => _defeated;
    public override IReadOnlyList<int> FlashColors { get; } = new[] { 3, 6, 8, 2 };
    public override int HpFirstRedWhite => HpPhase3End;
    public override int HpFirstWhite => HpPhase1End;

    public override int DefeatTicks()
    {
        int startY = World.Orb.Top + Playfield.OrbH / 2;
        return Math.Max(startY + 1, Playfield.Bottom - startY) / DefeatLineDistance + 15 * DefeatWhiteinInterval;
    }

    /// <summary>The current pattern id of phases 1 and 3 (0 = choose / teleport); settable for tests and traces.</summary>
    public int Pattern { get => _cur; set => _cur = value; }
    public bool TeleportDone { get => _teleportDone; set => _teleportDone = value; }

    // ---- drawing helpers (state the renderer reads) ----
    public bool IsBat => Form == FBat;
    public bool TeleportInProgress => (Phase is 1 or 3 && _cur == Choose && !_teleportDone) || Phase == 2 || (Phase == 4 && !_teleportDone);
    /// <summary>Girl cel of the wave teleport (frames 20..75), or -1 while she is gone (frames 44..51), or CStill.</summary>
    public int WaveCel
    {
        get
        {
            int f = PhaseFrame;
            if (f is >= 20 and < 28 or >= 68 and < 76) return CWave1;
            if (f is >= 28 and < 36 or >= 60 and < 68) return CWave2;
            if (f is >= 36 and < 44 or >= 52 and < 60) return CWave3;
            if (f is >= 44 and < 52) return -1;
            return CStill;
        }
    }
    /// <summary>The entrance finishes with the girl waving in; she shows from entrance frame 24 of the second loop.</summary>
    public bool EntranceGirlVisible => Phase == 0 && _entranceStage == 1 && InvincibilityFrame >= 24;

    // ---- drawing views (read-only; derived from the simulation state, nothing here is hashed or changes it) ----

    public const string GirlSheet = "BOSS5.BOS", AttackSheet = "BOSS5_2.BOS";
    public const int SphereSmall = 1, SphereMedium = 2, SphereLarge = 3, RiftCel = 4;
    public const int EntranceSphereCount = 2;

    /// <summary>The wave-in of the girl in the second entrance loop: (len, amp, phase) of the distortion, or null when she is plain or hidden.</summary>
    public (int len, int amp, int phase)? EntranceWave
    {
        get
        {
            if (!EntranceGirlVisible) return null;
            int f = InvincibilityFrame;
            if (f < 100) { int g = f & ~3; return (g + 2, 100 - g, g * 6); }
            if (f is 128 or 129) return (3, 8, 64);
            return null;
        }
    }

    const int MoveStep = Playfield.Width / 40 * 4;     // MOVE_VELOCITY * MOVE_INTERVAL

    // Head of an entrance sphere on its ring (slightly off-centre, as in the original) at the given tick of the circling;
    // k > 0 are the steps of the slide: sphere 0 goes right, sphere 1 left.
    (int x, int y) EntranceHead(int sphere, int k)
    {
        if (k <= 0)
        {
            int tick = (_entranceStage == 0 ? Math.Max(InvincibilityFrame, 1) : 120) + k;
            int angle = ((tick - 1) * (0x300 / 32) + sphere * (0x100 / EntranceSphereCount)) & 0xFF;
            return (AngleMath.PolarX(BaseCenterX - SphereW / 2, GirlH, angle), AngleMath.PolarY(BaseCenterY - SphereH / 2 - 14, GirlH, angle));
        }
        var h0 = EntranceHead(sphere, 0);
        return (h0.x + k * MoveStep * (sphere == 0 ? 1 : -1), h0.y);
    }

    /// <summary>
    /// The entrance spheres on screen now: first two circling the girl with a two-image trail, then sliding sideways off the
    /// screen. cel = SphereLarge (head), SphereMedium, SphereSmall (trail).
    /// </summary>
    public IEnumerable<(int cel, int left, int top)> EntranceSpheres()
    {
        if (Phase != 0 || !_entranceStarted) yield break;
        int steps = _entranceStage == 1 ? InvincibilityFrame / 4 : 0;
        int last = 0;
        for (int k = 1; k <= steps; k++)
        {
            last = k;
            // the slide stops once the oldest trail image has left the screen on both sides
            if (EntranceHead(0, k - 3).x > Playfield.Right && EntranceHead(1, k - 3).x < -SphereW) break;
        }
        bool slide = _entranceStage == 1;
        int tick = Math.Max(InvincibilityFrame, 1);
        for (int i = 0; i < EntranceSphereCount; i++)
        {
            var head = EntranceHead(i, last);
            yield return (SphereLarge, head.x, head.y);
            if (slide || tick > 1) { var t0 = EntranceHead(i, last - 1); yield return (SphereMedium, t0.x, t0.y); }
            if (slide || tick > 2) { var t1 = EntranceHead(i, last - 2); yield return (SphereSmall, t1.x, t1.y); }
        }
    }

    /// <summary>Which sprite the girl is drawn with now: the wave cels while teleporting, the hand and the attack cels in the patterns, else standing. cel -1 = not drawn.</summary>
    public (string sheet, int cel) GirlPose()
    {
        if (TeleportInProgress) return (GirlSheet, WaveCel);
        (string, int) still = (GirlSheet, CStill), hand = (GirlSheet, CHand);
        (string, int) Attack(bool star) => star
            ? PhaseFrame >= 10 ? (AttackSheet, CPrepare) : still
            : PhaseFrame < 4 ? (AttackSheet, CPrepare) : (AttackSheet, PhaseFrame % 8 < 4 ? CAttack1 : CAttack2);
        switch (Phase)
        {
            case 1:
                if (_cur == 1 && PhaseFrame is >= 50 and < 150) return hand;
                if (_cur is 2 or 3 && PhaseFrame >= 50) return hand;
                break;
            case 3:
                if (_cur != Choose) return Attack(_phase3Cur == 0);
                break;
            case 4:
                if (PhaseFrame is >= TransformStart and <= TransformEnd) return (AttackSheet, CPrepare);
                break;
            case 5:
                if (Form != FGirl) break;
                if (_p5Subphase == 1) { if (PhaseFrame >= TransformStart) return (AttackSheet, CPrepare); }
                else if (_p5GirlPattern != Choose) return Attack(_p5GirlSubphase == 0);
                break;
        }
        return still;
    }

    /// <summary>The wave teleport's five small stars (frames 4..67 of the teleport).</summary>
    public bool StarsShown => TeleportInProgress && PhaseFrame is >= 4 and < 68;

    /// <summary>The rifts (spawn points) shown now, as the top-left of their 32x32 cels; the cel alternates every 4 frames.</summary>
    public IEnumerable<(int left, int top)> Rifts()
    {
        int[]? l = null, t = null;
        bool on = false;
        switch (Phase)
        {
            case 1:
                if (_cur == 2) { l = _riftsMissileL; t = _riftsMissileT; on = PhaseFrame is >= 60 and < 160; }
                break;
            case 3:
                if (_cur == 3 && _phase3Cur == 3) { l = _riftsFromL; t = _riftsFromT; on = PhaseFrame is >= 60 and < 200; }
                break;
            case 4:
                if (_teleportDone && Form == FGirl) { l = _riftsGirlToBatL; t = _riftsGirlToBatT; on = PhaseFrame is >= TransformStart and < TransformEnd; }
                break;
            case 5:
                if (_p5Subphase == 1)
                {
                    bool bat = Form == FBat;
                    l = bat ? _riftsBatToGirlL : _riftsGirlToBatL;
                    t = bat ? _riftsBatToGirlT : _riftsGirlToBatT;
                    on = PhaseFrame is >= TransformStart and < TransformEnd;
                }
                break;
        }
        if (!on || l == null || t == null) yield break;
        for (int i = 0; i < 5; i++) yield return (l[i], t[i]);
    }

    /// <summary>The ten spheres of the "clusters" pattern: (left, top, cel), with the rift cel on the one that has just fired.</summary>
    public IEnumerable<(int left, int top, int cel)> PatternSpheres()
    {
        if (Phase != 3 || _cur != 2 || _phase3Cur != 2) yield break;
        int f = PhaseFrame;
        int firstShown = f > 130 ? (f - 130) / 10 : 0;
        int count = f <= 100 ? f / 10 : 10;
        for (int i = firstShown; i < count; i++) yield return (_sphereL[i], _sphereT[i], SphereLarge);
        if (f > 100 && f <= 200)
        {
            int fired = (f - 100) / 10 - 1;
            if (fired is >= 0 and < 10) yield return (_sphereL[fired], _sphereT[fired], RiftCel);
        }
    }

    /// <summary>A big circle around the girl (or the safety circle around the player) being summoned or flashing.</summary>
    public readonly record struct BigCircle(int CenterX, int CenterY, int Angle, int Frame);

    /// <summary>The circle currently on screen, if any. <paramref name="star"/>: the Star of David is drawn inside it.</summary>
    public BigCircle? ActiveCircle(out bool star)
    {
        star = false;
        if (Phase == 1 && _cur == 3 && PhaseFrame >= 60)
            return new BigCircle(GirlCenterX, GirlCenterY, _ringsAngle, _ringsFrame);
        bool starStage = (Phase == 3 && _cur != Choose && _phase3Cur == 0)
            || (Phase == 5 && Form == FGirl && _p5Subphase == 0 && _p5GirlPattern != Choose && _p5GirlSubphase == 0);
        if (starStage && PhaseFrame >= 10)
        {
            star = _starAngle >= 0x40 && _starFrame >= 10;
            return new BigCircle(GirlCenterX, GirlCenterY, _starAngle, _starFrame);
        }
        if (Phase == 5 && Form == FGirl && _p5Subphase == 0 && _p5GirlSubphase == 1)
        {
            if (_p5GirlPattern == 3 && PhaseFrame >= 60) return new BigCircle(GirlCenterX, GirlCenterY, _fiveAngle, _fiveFrame);
            if (_p5GirlPattern == 2 && PhaseFrame >= 60 && _safetyFrame is > 0 and <= 200)
                return new BigCircle(_safetyTargetLeft + Playfield.PlayerW / 2, PlayerCenterY, _safetyAngle, _safetyFrame);
        }
        return null;
    }

    /// <summary>The growing ring that follows the hand in the "lasers across" pattern (frames 60..119): centre and radius.</summary>
    public (int cx, int cy, int radius)? HandRing =>
        Phase == 1 && _cur == 1 && PhaseFrame is >= 60 and < 120
            ? (GirlCenterX, Top + GirlH / 3, GirlW / 4 + 8 * Math.Max(0, (PhaseFrame - 60) / 2))
            : null;

    int ByRank(int easy, int normal, int hard, int lunatic) => World.Rank switch
    {
        Rank.Easy => easy, Rank.Normal => normal, Rank.Hard => hard, Rank.Lunatic => lunatic, _ => normal,
    };

    int Irand() => World.Rng.Next();
    int PlayerCenterX => World.Player.Left + Playfield.PlayerW / 2;
    static int PlayerCenterY => Player.Top + Playfield.PlayerH / 2;
    int GirlCenterX => Left + GirlW / 2;
    int GirlCenterY => Top + GirlH / 2;
    int BatCenterX => BatLeft + BatW / 2;
    int BatCenterY => BatTop + BatH / 2;
    int FormCenterX(int form) => form == FGirl ? GirlCenterX : BatCenterX;
    int FormCenterY(int form) => form == FGirl ? GirlCenterY : BatCenterY;
    int LefteyeX => Left + 60;
    int LefteyeY => Top + 28;

    // ---- random positions ----
    int RandomLeft() => Playfield.Left + Irand() % (Playfield.Width - GirlW);
    int RandomTop() => Playfield.Top + Irand() % (Playfield.Height * 17 / 42);

    /// <summary>surround_random_left(): (left + irand() % 192) - (192 - w) / 2.</summary>
    int SurroundLeft(int left, int w) => left + Irand() % SurroundAreaW - (SurroundAreaW - w) / 2;
    /// <summary>surround_random_top(): the girl's area sits above her, the bat's is centred.</summary>
    int SurroundTop(int top, bool bat) => top + Irand() % SurroundAreaH + (bat ? -((SurroundAreaH - BatH) / 2) : -(SurroundAreaH - GirlH));

    // ---- entrance (phase 0) ----

    public override bool EntranceFrame()
    {
        if (!_entranceStarted)
        {
            _entranceStarted = true;
            World.Missiles.Update();
            InvincibilityFrame = 0;
        }
        if (_entranceWait > 0)
        {
            _entranceWait--;
            return false;
        }
        if (_entranceStage == 0)
        {
            // trailing rotation: 120 iterations, each followed by frame_delay(((120 - tick) / 12) + 1)
            InvincibilityFrame++;
            if (InvincibilityFrame < 120)
            {
                _entranceWait = (120 - InvincibilityFrame) / 12;
                return false;
            }
            _entranceStage = 1;
            InvincibilityFrame = 0;                       // the second loop starts right away
        }
        // spheres leave and the girl waves in: 141 iterations of frame_delay(1)
        InvincibilityFrame++;
        if (InvincibilityFrame <= 140) return false;
        PhaseFrame = 0;
        InvincibilityFrame = 0;
        Phase = 1;
        HitboxOrbInactive = false;
        _teleportDone = false;
        _cur = 1;
        return true;
    }

    // ---- main ----

    public override void Update()
    {
        var w = World;
        w.Missiles.Update();
        switch (Phase)
        {
            case 1:
                PhaseFrameCommon(1);
                HitUpdate();
                if (!Invincible && PhaseDone(HpPhase1End)) NextPhase(2, Choose);
                break;
            case 2:
                PhaseFrame++;
                _teleportDone = WaveTeleport(BaseLeft, BaseTop);
                if (_teleportDone)
                {
                    _phase3Cur = Choose;                // phase_3(99)
                    NextPhase(3, 1);
                }
                break;
            case 3:
                PhaseFrameCommon(3);
                HitUpdate();
                if (!Invincible && PhaseDone(HpPhase3End))
                {
                    NextPhase(4, Choose);
                    Form = FGirl;
                }
                break;
            case 4:
                PhaseFrame++;
                if (!_teleportDone) _teleportDone = WaveTeleport(BaseLeft, BaseTop);
                else _cur = TransformGirlToBat();
                if (_cur == FBat)
                {
                    NextPhase(5, Choose);
                    Form = FBat;
                    // phase_5(..., reset = true)
                    _p5Subphase = 0;
                    _p5BatPattern = Choose;
                    _p5GirlPattern = Choose;
                    _p5GirlSubphase = 0;
                }
                break;
            case 5:
                PhaseFrame++;
                InvincibilityFrame++;
                if (Form != FGirl)
                {
                    BatLockedMove(_batVx, _batVy);
                    int c = PhaseFrame % BatCycleFrames;
                    if (c == 0) BatImage = 0;
                    else if (c == BatFramesPerCel) BatImage = 1;
                    else if (c == BatFramesPerCel * 2) BatImage = 2;
                }
                Phase5();
                HitUpdate();
                if (Hp <= HpPhase5End)
                {
                    w.Missiles.Reset();
                    w.Lasers.ResetAll();
                    _defeated = true;
                }
                break;
        }
        PatternCur = _cur;
    }

    void PhaseFrameCommon(int phaseId)
    {
        PhaseFrame++;
        InvincibilityFrame++;
        // phase_frame_common(): run the current pattern, or teleport first
        if (_cur != Choose)
        {
            _cur = PhaseFunc(phaseId, _cur);
        }
        else if (!_teleportDone)
        {
            // wave_teleport(elis_playfield_random_left(), elis_playfield_random_top()): the arguments are evaluated on
            // EVERY call; a cdecl compiler of the time evaluates them right to left, so the top is drawn first.
            // ASSUMPTION (order of the two draws). TODO(T5)
            int top = RandomTop();
            int left = RandomLeft();
            _teleportDone = WaveTeleport(left, top);
        }
        else
        {
            _cur = PhaseFunc(phaseId, _cur);
            _teleportDone = false;
        }
    }

    bool PhaseDone(int endHp) => Hp <= endHp && _teleportDone;

    void NextPhase(int phase, int pattern)
    {
        Phase = phase;
        PhaseFrame = 0;
        InvincibilityFrame = 0;
        _cur = pattern;
        _teleportDone = false;
    }

    int PhaseFunc(int phaseId, int id) => phaseId == 1 ? Phase1(id) : Phase3(id);

    // ---- hit handling ----

    void HitUpdate()
    {
        // The orb box and the shot box depend on the form. The bat's shot box top uses cur_left (ZUN quirk).
        if (Form == FGirl)
            HitCommon(OrbPrevInside(8, 88, -12, 56), HitScore, Left + GirlW / 4, Top + GirlH / 3, GirlW / 8 * 5 - Shots.W, GirlH / 3 * 2 - Shots.H);
        else
        {
            var o = World.Orb;
            bool inside = BatLeft - 16 <= o.PrevLeft && BatLeft + 32 >= o.PrevLeft && BatTop - 16 <= o.PrevTop && BatTop + 16 >= o.PrevTop;
            HitCommon(inside, HitScore, BatLeft, BatLeft, BatW - Shots.W, BatH * 3 / 2 - Shots.H);
        }
    }

    // ---- bat movement ----

    void BatLockedMove(int dx, int dy)
    {
        if (_batLock == 0)
        {
            BatLeft = Math.Min(Math.Max(BatLeft + dx, Playfield.Left), Playfield.Right + 96);
            BatTop = Math.Min(Math.Max(BatTop + dy, Playfield.Top), Playfield.Bottom - 96);
            _batLock = 1;
        }
        else if (_batLock >= BatSpeedDivisor - 1) _batLock = 0;
        else _batLock++;
    }

    // ---- teleport ----

    bool WaveTeleport(int targetLeft, int targetTop)
    {
        int f = PhaseFrame;
        if (f is 20 or 28 or 36 or 44 or 60 or 68) HitboxOrbInactive = true;
        else if (f == 52)
        {
            HitboxOrbInactive = true;
            Left = targetLeft;
            Top = targetTop;
        }
        else if (f == 76) HitboxOrbInactive = false;
        else if (f > 80)
        {
            PhaseFrame = 0;
            return true;
        }
        if (f % 4 != 0) return false;
        for (int i = 0; i < 5; i++)
        {
            if (f < 40 || f > 52)
            {
                StarLeft[i] = SurroundLeft(Left, GirlW);
                StarTop[i] = SurroundTop(Top, false);
            }
            else
            {
                StarLeft[i] += (SurroundLeft(Left, GirlW) - StarLeft[i]) / 3;
                StarTop[i] += (SurroundTop(Top, false) - StarTop[i]) / 3;
            }
        }
        return false;
    }

    /// <summary>rifts_update_and_render(): the state half. A random colour (transformations) costs one more draw per rift.</summary>
    void RiftsUpdate(int[] left, int[] top, bool bat, int start, int end, bool randomColor)
    {
        if (PhaseFrame < start || PhaseFrame > end || PhaseFrame % 4 != 0) return;
        for (int i = 0; i < 5; i++)
        {
            if (PhaseFrame % 16 == i * 4 % 16 || PhaseFrame == start)
            {
                left[i] = SurroundLeft(bat ? BatLeft : Left, bat ? BatW : GirlW);
                top[i] = SurroundTop(bat ? BatTop : Top, bat);
            }
            if (PhaseFrame < end)
            {
                Irand();                                  // the rift's cel (irand() % RIFT_CELS)
                if (randomColor) Irand();                 // irand() % COLOR_COUNT
            }
        }
        World.Sound.PlaySe(7);
    }

    // ---- Star of David and the big circle ----

    /// <summary>bigcircle_summon_update_and_render(): the angle grows 2 per even frame.</summary>
    bool SummonFrame(int start) => PhaseFrame >= start && PhaseFrame % 2 == 0;

    int Phase1(int id)
    {
        switch (id)
        {
            case Choose: return Irand() % 4;     // includes CHOOSE_NEW: a 25% chance of teleporting again
            case 1: return Pattern11LasersAcross();
            case 2: return PatternRandomDownwardsMissiles();
            case 3: return PatternPelletsAlongCircle();
        }
        return Choose;
    }

    int Phase3(int id)
    {
        switch (id)
        {
            case Choose: return Irand() % 4;
            case 1: return StarOfDavidThen(1, PatternCurved5StackRings);
            case 2: return StarOfDavidThen(2, PatternClustersFromSpheres);
            case 3: return StarOfDavidThen(3, PatternRandomFromRifts);
        }
        return Choose;
    }

    int StarOfDavidThen(int id, Func<int> func)
    {
        if (_phase3Cur == 0)
        {
            _phase3Cur = StarOfDavid() ? 1 : 0;
            return id;
        }
        return _phase3Cur = func();
    }

    /// <summary>star_of_david(): returns SP_PATTERN (true) when the summon, flash and star are done.</summary>
    bool StarOfDavid()
    {
        if (PhaseFrame < 5)
        {
            _starFrame = 0;
            _starAngle = 0;
        }
        if (PhaseFrame < 10) return false;
        if (PhaseFrame == 10)
        {
            _starAngle = 0;
            _starFrame = 0;
        }
        if (SummonFrame(10) && _starFrame == 0)
        {
            if (PhaseFrame == 10) World.Sound.PlaySe(8);
            _starAngle += 0x02;
            if (_starAngle >= 0x40) _starFrame = 1;
        }
        else if (_starAngle >= 0x40)
        {
            _starFrame++;
            if (_starFrame > 60)
            {
                PhaseFrame = 0;
                _starAngle = 0;
                _starFrame = 0;
                return true;
            }
        }
        return false;
    }

    // ---- phase 1 patterns ----

    int Pattern11LasersAcross()
    {
        const int interval = 10;
        if (PhaseFrame == 50)
        {
            _lasersDirection = Irand() % 2;
            _state = ByRank(50, 55, 60, 65);              // select_laser_speed_for_rank: to_sp(6.25 / 2, ...)
        }
        if (PhaseFrame >= 70 && PhaseFrame % interval == 0)
        {
            int step = (PhaseFrame - 70) / interval * (Playfield.Width / 10);
            int targetLeft = _lasersDirection == 0 ? Playfield.Left + step : Playfield.Right - step;
            World.Lasers[PhaseFrame / interval].Spawn(LefteyeX, LefteyeY, targetLeft, Playfield.Bottom, _state, ColWhite, 25, 4);
            World.Sound.PlaySe(6);
            // "<=" is why 11 lasers are fired
            if ((_lasersDirection == 0 && targetLeft >= Playfield.Right) || (_lasersDirection == 1 && targetLeft <= Playfield.Left))
            {
                PhaseFrame = 0;
                return Choose;
            }
        }
        return 1;
    }

    int PatternRandomDownwardsMissiles()
    {
        if (PhaseFrame == 50) _state = ByRank(0x0F, 0x15, 0x19, 0x1D);   // angle range
        if (PhaseFrame > 60 && PhaseFrame % 3 == 0)
        {
            int i = Irand() % 5;
            int angle = (Irand() % _state - (_state - 0x01) / 2 + 0x40) & 0xFF;
            int vx = AngleMath.PolarX(0, 7, angle), vy = AngleMath.PolarY(0, 7, angle);
            World.Missiles.Add(_riftsMissileL[i], _riftsMissileT[i], vx << 4, vy << 4);
        }
        RiftsUpdate(_riftsMissileL, _riftsMissileT, false, 60, 160, false);
        if (PhaseFrame > 170)
        {
            PhaseFrame = 0;
            return Choose;
        }
        return 2;
    }

    int PatternPelletsAlongCircle()
    {
        if (PhaseFrame < 10) _ringsFrame = 0;
        if (PhaseFrame == 50)
        {
            _ringsAngle = 0;
            _state = (int)ByRank((int)PelletGroup.OneAimed, (int)PelletGroup.OneRandomNarrowAimed,
                (int)PelletGroup.ThreeSpreadWideAimed, (int)PelletGroup.FiveSpreadNarrowAimed);
        }
        // bigcircle_summon_and_flash(circle, 60, 0.05)
        if (SummonFrame(60) && _ringsFrame == 0)
        {
            if (PhaseFrame == 60) World.Sound.PlaySe(8);
            _ringsAngle += 0x02;
            if (_ringsAngle >= 0x40) _ringsFrame = 1;
        }
        else if (_ringsFrame != 0 && _ringsFrame < 40)
        {
            _ringsFrame++;
        }
        else if (_ringsFrame != 0)
        {
            _ringsAngle = 0;
            for (int i = 0; i < 32; i++)
            {
                // "lefteye?!": the X centre is the left eye, the Y centre the girl's
                int left = AngleMath.PolarX(LefteyeX, BigCircleRadius, _ringsAngle);
                int top = AngleMath.PolarY(GirlCenterY, BigCircleRadius, _ringsAngle);
                World.Pellets.AddGroup(left, top, (PelletGroup)_state, 4 /* to_sp(0.25) */);
                _ringsAngle = (_ringsAngle + 0x100 / 32) & 0xFF;
            }
            PhaseFrame = 0;
            _ringsFrame = 0;
            return Choose;
        }
        return 3;
    }

    // ---- phase 3 patterns ----

    void FireRing(int ring, int angleOffset, int speed)
    {
        for (int i = 0; i < ring; i++)
            World.Pellets.AddSingle(GirlCenterX - Pellets.W / 2, GirlCenterY - Pellets.H / 2, (0x100 / ring * i + angleOffset) & 0xFF, speed);
    }

    int PatternCurved5StackRings()
    {
        switch (PhaseFrame)
        {
            case 10: _state = ByRank(14, 16, 18, 20); FireRing(_state, 0x00, 48); break;
            case 16: FireRing(_state, 0x02, 54); break;
            case 24: FireRing(_state, 0x04, 64); break;
            case 32: FireRing(_state, 0x06, 72); break;
            case 40: FireRing(_state, 0x08, 80); break;
            default:
                if (PhaseFrame > 60)
                {
                    PhaseFrame = 0;
                    return Choose;
                }
                break;
        }
        return 1;
    }

    int PatternClustersFromSpheres()
    {
        const int interval = 10, sphereCount = 10, keyframeFire = sphereCount * interval, keyframeRemove = keyframeFire + 30;
        const int keyframeFireDone = keyframeFire + sphereCount * interval, keyframeRemoveDone = keyframeRemove + sphereCount * interval;
        const int keyframeDone = keyframeRemoveDone + 20;
        if (PhaseFrame <= keyframeFire && PhaseFrame % interval == 0)
        {
            int i = PhaseFrame / interval;
            if (Left < BaseLeft)
                _sphereL[i - 1] = i * ((Playfield.Right - SphereW - Left) / sphereCount) + (GirlCenterX - SphereW / 2);
            else
                _sphereL[i - 1] = i * ((Playfield.Left - Left) / sphereCount) + (GirlCenterX - SphereW / 2);
            int edge = Playfield.Height * 25 / 42;
            _sphereT[i - 1] = i * ((Irand() % edge + edge - Top) / sphereCount) + (GirlCenterY - SphereH / 2);
        }
        else if (PhaseFrame > keyframeFire && PhaseFrame <= keyframeRemoveDone && PhaseFrame % interval == 0)
        {
            if (PhaseFrame <= keyframeFireDone)
            {
                int i = (PhaseFrame - keyframeFire) / interval;
                World.Sound.PlaySe(7);
                _state = ByRank(5, 7, 9, 13);
                for (int k = 0; k < _state; k++)
                {
                    int angle = Irand() & 0x7F;
                    int speed = (Irand() + 24 /* to_sp(1.5) */) & (64 /* to_sp(4.0) */ - 1);
                    World.Pellets.AddSingle(_sphereL[i - 1], _sphereT[i - 1], angle, speed);
                }
            }
        }
        else if (PhaseFrame > keyframeDone)
        {
            PhaseFrame = 0;
            return Choose;
        }
        return 2;
    }

    int PatternRandomFromRifts()
    {
        const int keyframe0 = 60, keyframe1 = 120, keyframe2 = 200, keyframe3 = 220;
        if (PhaseFrame == keyframe0 / 2) _state = ByRank(6, 3, 2, 2);
        if (PhaseFrame > keyframe0 && _state > 0 && PhaseFrame % _state == 0 && PhaseFrame < keyframe1)
        {
            int i = Irand() % 5;
            int angle = (Irand() % 0x15 + 0x36) & 0xFF;
            World.Pellets.AddSingle(_riftsFromL[i], _riftsFromT[i], angle, 96 /* to_sp(6.0) */);
        }
        else if (PhaseFrame >= keyframe1 && PhaseFrame < keyframe2)
        {
            int i = Irand() % 5;
            int angle = (Irand() % 0x15 + 0x36) & 0xFF;
            int angleOffset = ((Irand() & 1) != 0 ? +(PhaseFrame - keyframe1) : -(PhaseFrame - keyframe1)) & 0xFF;
            angle = angle * (angleOffset / 2) & 0xFF;
            World.Pellets.AddSingle(_riftsFromL[i], _riftsFromT[i], angle, 96);
        }
        RiftsUpdate(_riftsFromL, _riftsFromT, false, keyframe0, keyframe2, false);
        if (PhaseFrame > keyframe3)
        {
            PhaseFrame = 0;
            return Choose;
        }
        return 3;
    }

    // ---- phase 4: transformations ----

    int TransformGirlToBat()
    {
        RiftsUpdate(_riftsGirlToBatL, _riftsGirlToBatT, false, TransformStart, TransformEnd, true);
        if (PhaseFrame > TransformEnd)
        {
            BatLeft = Left + (GirlW - BatW) / 2;
            BatTop = Top + (GirlH - BatH) / 2;
            BatImage = 0;
            PhaseFrame = 0;
            return FBat;
        }
        return FGirl;
    }

    int TransformBatToGirl()
    {
        RiftsUpdate(_riftsBatToGirlL, _riftsBatToGirlT, true, TransformStart, TransformEnd, true);
        if (PhaseFrame > TransformEnd)
        {
            int left = BatCenterX - GirlW / 2, top = BatCenterY - GirlH / 2;
            if (left < Playfield.Left) left = Playfield.Left;
            else if (left > Playfield.Right - GirlW) left = Playfield.Right - GirlW;
            if (top < Playfield.Top) top = Playfield.Top;
            Left = left;
            Top = top;
            PhaseFrame = 0;
            return FGirl;
        }
        return FBat;
    }

    // ---- phase 5 ----

    void Phase5()
    {
        if (Form == FBat)
        {
            if (_p5Subphase == 0)
            {
                _p5Subphase = BatFlyRandom();
                if (_p5BatPattern == Choose) _p5BatPattern = Irand() % 4 + 1;   // no pattern cycle is skipped here
                switch (_p5BatPattern)
                {
                    case 1: PatternBatSlowSpreads(); break;
                    case 2: PatternBatAlternatingNarrowAndWide2Spreads(); break;
                    case 3: PatternBatRandomRain(); break;
                    case 4: PatternBatAlternating2And3Spreads(); break;
                }
            }
            else
            {
                Form = TransformBatToGirl();
                if (Form == FGirl)
                {
                    _p5BatPattern = Choose;
                    _p5Subphase = 0;
                }
            }
        }
        else
        {
            if (_p5Subphase == 0) _p5Subphase = Phase5Girl();
            else
            {
                Form = TransformGirlToBat();
                if (Form == FBat) _p5Subphase = 0;
            }
        }
    }

    /// <summary>bat_fly_random(): picks a far target at frame 3 and flies there; 1 = P5_TRANSFORM when it arrives.</summary>
    int BatFlyRandom()
    {
        const int speed = 2;
        if (PhaseFrame < BatSpeedDivisor)
        {
            _batVx = 0;
            _batVy = 0;
            return 0;
        }
        if (PhaseFrame == BatSpeedDivisor)
        {
            int targetLeft, targetTop;
            do
            {
                targetLeft = RandomLeft();
                targetTop = RandomTop() + (GirlH - BatH) / 2;
            } while (Math.Abs(BatLeft - targetLeft) < Playfield.Width / 5 && Math.Abs(BatTop - targetTop) < Playfield.Height * 4 / 21);
            int a = AngleMath.Iatan2(targetTop - BatTop, targetLeft - BatLeft);
            _batVx = AngleMath.PolarX(0, speed, a);
            _batVy = AngleMath.PolarY(0, speed, a);
            _batTargetFrames = Math.Abs(targetLeft - BatLeft) / speed;
        }
        // "gravity": always +0 (integer division) but the draw happens
        _batVy += (Irand() % 5 - 2) / 10;
        if (PhaseFrame / BatSpeedDivisor >= _batTargetFrames)
        {
            _batVx = 0;
            _batVy = 0;
            PhaseFrame = 0;
            return 1;
        }
        return 0;
    }

    void FireGroup(int form, PelletGroup group, int speed) =>
        World.Pellets.AddGroup(FormCenterX(form) - Pellets.W / 2, FormCenterY(form) - Pellets.H / 2, group, speed);

    void PatternBatSlowSpreads()
    {
        if (PhaseFrame % 16 != 0) return;
        // Not aimed on Lunatic (compare Kikuri's souls)
        var group = World.Rank switch
        {
            Rank.Easy => PelletGroup.ThreeSpreadWideAimed,
            Rank.Normal => PelletGroup.FiveSpreadWideAimed,
            Rank.Hard => PelletGroup.FiveSpreadNarrowAimed,
            _ => PelletGroup.FiveSpreadNarrow,
        };
        FireGroup(FBat, group, 56 /* to_sp(3.5) */);
    }

    void PatternBatAlternatingNarrowAndWide2Spreads()
    {
        if (PhaseFrame % 4 != 0) return;
        if ((Irand() & 1) != 0) FireGroup(FBat, PelletGroup.TwoSpreadNarrowAimed, 88 /* to_sp(5.5) */);
        else FireGroup(FBat, PelletGroup.TwoSpreadWideAimed, 88);
    }

    void PatternBatAlternating2And3Spreads()
    {
        int m = PhaseFrame % 50;
        if (m < 10 && PhaseFrame % 4 == 0) FireGroup(FBat, PelletGroup.TwoSpreadNarrowAimed, 85 /* to_sp(5.3125) */);
        if (m >= 25 && m < 35 && PhaseFrame % 4 == 0) FireGroup(FBat, PelletGroup.ThreeSpreadNarrowAimed, 85);
    }

    void PatternBatRandomRain()
    {
        if (PhaseFrame % 4 != 0) return;
        World.Pellets.AddSingle(BatCenterX - Pellets.W / 2, BatCenterY - Pellets.H / 2, Irand() & 0xFF, 2 /* to_sp(0.125) */,
            PelletMotion.Gravity, 1 /* to_sp(0.0625) */);
    }

    /// <summary>phase_5_girl(): returns P5_TRANSFORM (1) when a star pattern has finished.</summary>
    int Phase5Girl()
    {
        switch (_p5GirlPattern)
        {
            case Choose:
                _p5GirlPattern = Irand() % 3 + 1;       // no pattern cycle is skipped here
                break;
            case 1: return StarThenPattern(PatternThreeSymmetric4StacksThenSymmetricArc);
            case 2: return StarThenPattern(PatternSafetyCircleAndRainFromTop);
            case 3: return StarThenPattern(PatternAimed5SpreadsAndLasersFollowedByRing);
        }
        return 0;
    }

    int StarThenPattern(Func<bool> func)
    {
        if (_p5GirlSubphase == 0)
        {
            _p5GirlSubphase = StarOfDavid() ? 1 : 0;
            return 0;
        }
        _p5GirlSubphase = func() ? 1 : 0;
        if (_p5GirlSubphase == 0)
        {
            _p5GirlPattern = Choose;
            return 1;
        }
        return 0;
    }

    // ---- phase 5 girl patterns (true = SP_PATTERN, still running) ----

    void FireSymmetric(int angleRight, int speed)
    {
        World.Pellets.AddSingle(GirlCenterX - Pellets.W / 2 - 4, GirlCenterY - Pellets.H / 2 - 4, (0x80 - angleRight) & 0xFF, speed);
        World.Pellets.AddSingle(GirlCenterX - Pellets.W / 2 + 4, GirlCenterY - Pellets.H / 2 - 4, angleRight & 0xFF, speed);
    }

    bool PatternThreeSymmetric4StacksThenSymmetricArc()
    {
        if (PhaseFrame == 50)
        {
            _state = ByRank(2, 3, 3, 4);
            for (int i = 0; i < 4; i++) FireSymmetric(0x40, (i + _state) * 16);
        }
        else if (PhaseFrame == 60)
        {
            for (int i = 0; i < 4; i++) FireSymmetric(0x30, (i + _state) * 16);
            World.Sound.PlaySe(7);
        }
        else if (PhaseFrame == 70)
        {
            for (int i = 0; i < 4; i++) FireSymmetric(0x18, (i + _state) * 16);
            World.Sound.PlaySe(7);
        }
        else if (PhaseFrame == 80)
        {
            int angle = 0x00;
            for (int i = 0; i < 10; i++)
            {
                FireSymmetric(angle, (2 + _state) * 16);
                angle += 0x06;
            }
            World.Sound.PlaySe(7);
        }
        else if (PhaseFrame >= 120)
        {
            PhaseFrame = 0;
            return false;
        }
        return true;
    }

    bool PatternSafetyCircleAndRainFromTop()
    {
        const int circleFrames = 200, safetyLeft = BigCircleRadius, safetyRight = BigCircleRadius - Playfield.PlayerW / 2 - Playfield.PlayerW / 4;
        var w = World;
        if (PhaseFrame < 10) _safetyFrame = 0;
        if (PhaseFrame < 60) return true;
        if (PhaseFrame == 60)
        {
            _state = ByRank(4, 2, 2, 2);
            _safetyAngle = 0;
        }
        if (SummonFrame(60) && _safetyFrame == 0)
        {
            if (PhaseFrame == 60)
            {
                w.Sound.PlaySe(8);
                _safetyTargetLeft = w.Player.Left;
            }
            _safetyAngle += 0x02;
            if (_safetyAngle >= 0x40) _safetyFrame = 1;
        }
        else if (_safetyFrame != 0 && _safetyFrame < circleFrames)
        {
            _safetyFrame++;
            if (!w.PlayerInvincible)
            {
                if (_safetyTargetLeft + safetyRight <= w.Player.Left || _safetyTargetLeft - safetyLeft >= w.Player.Left)
                    w.PlayerIsHit = true;               // (the original also blocks for delay(100) first)
            }
            if (w.PlayerIsHit) _safetyFrame = circleFrames;
            if (PhaseFrame % _state == 0)
            {
                w.Pellets.AddGroup(Playfield.Left + Irand() % (Playfield.Width - Pellets.W), Playfield.Top,
                    w.Rank == Rank.Lunatic ? PelletGroup.OneRandomWide : PelletGroup.One, 72 /* to_sp(4.5) */);
            }
            if (_safetyFrame > circleFrames - 20 && _safetyFrame % 4 == 0) FireGroup(FGirl, PelletGroup.OneAimed, 72);
        }
        else if (_safetyFrame < circleFrames)
        {
            return true;
        }
        else
        {
            _safetyFrame++;
            if (_safetyFrame % 8 == 0)
            {
                // ZUN quirk: relative to the bat's top-left corner, not the girl's
                w.Pellets.AddGroup(BatLeft + GirlW / 2 - Pellets.W / 2, BatTop + GirlH / 2 - Pellets.H / 2, PelletGroup.OneAimed, 72);
            }
            if (_safetyFrame > circleFrames + 60)
            {
                PhaseFrame = 0;
                _safetyFrame = 0;
                _safetyAngle = 0;
                return false;
            }
        }
        return true;
    }

    bool PatternAimed5SpreadsAndLasersFollowedByRing()
    {
        const int keyframeInit = 50, keyframeStart = 60;
        var w = World;
        if (PhaseFrame < keyframeInit) return true;
        if (PhaseFrame == keyframeInit)
        {
            _fiveAngle = 0x00;
            _state = ByRank(40, 30, 20, 15);
        }
        // bigcircle_summon_and_flash(circle, 60, 0x02)
        if (SummonFrame(keyframeStart) && _fiveFrame == 0)
        {
            if (PhaseFrame == keyframeStart) w.Sound.PlaySe(8);
            _fiveAngle += 0x02;
            if (_fiveAngle >= 0x40) _fiveFrame = 1;
        }
        else if (_fiveFrame != 0 && _fiveFrame < 40)
        {
            _fiveFrame++;
        }
        else if (_fiveFrame != 0)
        {
            _fiveAngle = 0x00;
            // Depending on Elis' position, most of these pellets might be clipped.
            for (int i = 0; i < 32; i++)
            {
                int left = AngleMath.PolarX(GirlCenterX, BigCircleRadius, _fiveAngle);
                int top = AngleMath.PolarY(LefteyeY, BigCircleRadius, _fiveAngle);   // "lefteye?!"
                w.Pellets.AddGroup(left, top, PelletGroup.One, 64 /* to_sp(4.0) */);
                _fiveAngle = (_fiveAngle + 0x100 / 32) & 0xFF;
            }
            PhaseFrame = 0;
            _fiveFrame = 0;
            return false;
        }
        if (PhaseFrame % _state == 0)
        {
            w.Pellets.AddGroup(GirlCenterX - 38 - Pellets.W / 2, GirlCenterY - 4 - Pellets.H / 2, PelletGroup.FiveSpreadWideAimed, 64);
            w.Pellets.AddGroup(GirlCenterX + 40 - Pellets.W / 2, GirlCenterY - 4 - Pellets.H / 2, PelletGroup.FiveSpreadWideAimed, 64);
        }
        if (PhaseFrame % (_state * 2) == 0)
        {
            w.Lasers[PhaseFrame / 10].Spawn(GirlCenterX - 8 / 2, LefteyeY, w.Player.Left, Player.Top + Playfield.PlayerH,
                100 / 2 /* to_sp(6.25) / 2 */, ColWhite, 20, 8);
            w.Sound.PlaySe(6);
        }
        return true;
    }

    public override void Hash(ref StateHasher h)
    {
        h.Add(Phase); h.Add(PhaseFrame); h.Add(InvincibilityFrame); h.Add(Hp); h.Add(Invincible); h.Add(PatternCur);
        h.Add(Left); h.Add(Top); h.Add(HitboxOrbInactive); h.Add(_defeated);
        h.Add(_entranceStage); h.Add(_entranceStarted); h.Add(_entranceWait);
        h.Add(Form); h.Add(_cur); h.Add(_teleportDone); h.Add(_batVx); h.Add(_batVy); h.Add(_batLock);
        h.Add(BatLeft); h.Add(BatTop); h.Add(BatImage);
        h.Add(_state); h.Add(_lasersDirection);
        h.Add(_ringsAngle); h.Add(_ringsFrame); h.Add(_starAngle); h.Add(_starFrame);
        h.Add(_safetyFrame); h.Add(_safetyTargetLeft); h.Add(_safetyAngle); h.Add(_fiveAngle); h.Add(_fiveFrame);
        h.Add(_phase3Cur); h.Add(_p5Subphase); h.Add(_p5BatPattern); h.Add(_p5GirlPattern); h.Add(_p5GirlSubphase); h.Add(_batTargetFrames);
        for (int i = 0; i < 5; i++)
        {
            h.Add(StarLeft[i]); h.Add(StarTop[i]);
            h.Add(_riftsMissileL[i]); h.Add(_riftsMissileT[i]); h.Add(_riftsFromL[i]); h.Add(_riftsFromT[i]);
            h.Add(_riftsGirlToBatL[i]); h.Add(_riftsGirlToBatT[i]); h.Add(_riftsBatToGirlL[i]); h.Add(_riftsBatToGirlT[i]);
        }
        for (int i = 0; i < 10; i++) { h.Add(_sphereL[i]); h.Add(_sphereT[i]); }
    }
}
