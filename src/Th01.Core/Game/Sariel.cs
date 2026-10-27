using Th01.Core.Rng;

namespace Th01.Core.Game;

/// <summary>
/// The Makai stage 20 boss, Sariel: a shield and a face hit by the Orb, 18 HP in the first form (phases 1-8, patterns
/// alternating with background transitions) and 6 HP in the second (phase 100). Delivered in parts; this part has the
/// set-up, the blocking entrance, phase 0 and the hit boxes. The patterns and the later phases are not ported yet, and the
/// boss is not wired into the GameWorld until they are. Rules: GAMEPLAY-NOTES.md section 33.
/// ref: th01/main/boss/b20m.cpp
/// </summary>
public sealed class Sariel : Boss
{
    // ---- coordinates ----
    public const int ShieldLeft = 304, ShieldTop = 144, DressLeft = 280, DressTop = 192, WandLeft = 296, WandTop = 48;
    public const int ShieldCenterX = 320, ShieldCenterY = 164;
    public const int SealCenterX = 320, SealCenterY = 185, SealRadius = 140;
    public const int WandEmitLeft = 340, WandEmitTop = 64;
    public const int FaceLeft = 314, FaceTop = 104, FaceW = 64, FaceH = 64, FaceCenterY = FaceTop + FaceW / 4;
    public const int A2OrbsLeftCenterX = 64, A2OrbsRightCenterX = 568, A2OrbsCenterY = 160;
    public const int HitScore = 10000;
    public const int Id = 4 * StageSetup.StagesPerScene - 1;           // stage 20, Makai route only

    // HP of both forms and the HUD thresholds (hud_hp_first_white / hud_hp_first_redwhite)
    public const int HpForm1 = 18, HpForm1FirstWhite = 8, HpForm1FirstRedWhite = 2;
    public const int HpForm2 = 6, HpForm2FirstWhite = 10, HpForm2FirstRedWhite = 3;

    // special phases
    public const int PhaseForm1Defeated = 99, PhaseForm2 = 100, PhaseForm2Defeated = 101;

    // ---- entrance length (estimated; see rules) ----
    /// <summary>The scroll: one tick per line, from line 399 up to 0.</summary>
    public const int ScrollTicks = 400;
    /// <summary>pagetrans_diagonal_8x8(40): (640 / 8) + (400 / 8) frames of 40 ms, about 2 ticks each (ASSUMPTION). TODO(T5)</summary>
    public const int TransitionFrames = 640 / 8 + 400 / 8, TransitionTicksPerFrame = 2;
    /// <summary>The entrance rings (the same helper as Kikuri; its length was not found). ASSUMPTION. TODO(T5)</summary>
    public const int RingsTicks = Kikuri.RingsFrames;
    public const int EntranceTotal = ScrollTicks + TransitionFrames * TransitionTicksPerFrame + RingsTicks;

    // ---- state ----
    int _entranceFrame;
    public int PatternsDone, PatternsUntilNext;
    public bool InitialHpRendered;

    public Sariel(GameWorld w) : base(w)
    {
        Hp = HpForm1;
        Left = ShieldLeft;
        Top = ShieldTop;
        HitboxOrbInactive = false;
    }

    public override int Width => 48;
    public override int Height => 48;
    public override bool Defeated => Phase == PhaseForm2Defeated;
    public override IReadOnlyList<int> FlashColors { get; } = new[] { 3, 4, 5 };
    public bool IsForm2 => Phase >= PhaseForm2;
    public override int HpFirstWhite => IsForm2 ? HpForm2FirstWhite : HpForm1FirstWhite;
    public override int HpFirstRedWhite => IsForm2 ? HpForm2FirstRedWhite : HpForm1FirstRedWhite;
    public override uint DefeatBonus => 5 * 10000;
    public override bool ClearsGame => true;

    /// <summary>Placeholder until the second form's defeat sequence is ported (a later part).</summary>
    public override int DefeatTicks() => 0;

    int Irand() => World.Rng.Next();

    int ByRank(int easy, int normal, int hard, int lunatic) => World.Rank switch
    {
        Rank.Easy => easy, Rank.Normal => normal, Rank.Hard => hard, Rank.Lunatic => lunatic, _ => normal,
    };

    // ---- entrance ----

    public override bool EntranceFrame() => ++_entranceFrame >= EntranceTotal;

    // ---- hit ----

    /// <summary>
    /// The face: overlap_xy_xywh_le_ge with the CURRENT Orb position; the shield: CBossEntity::hittest_orb() on the PREVIOUS
    /// one, inside the box (-16,-16)-(64,64) shrunk by the Orb hit box (Orb centre within 8..24 of its corner), unless inactive.
    /// </summary>
    bool OrbHitsFace()
    {
        var o = World.Orb;
        return o.Left >= FaceLeft && o.Left <= FaceLeft + FaceW - Playfield.OrbW &&
               o.Top >= FaceTop && o.Top <= FaceTop + FaceH - Playfield.OrbH;
    }

    public bool OrbHitsShield() => OrbPrevInside(-24, 40, -24, 40);

    void HitUpdate() =>
        HitCommon(OrbHitsFace() || OrbHitsShield(), HitScore, FaceLeft, FaceTop, FaceW * 5 / 4 - Shots.W, FaceH * 3 / 4 - Shots.H);

    // ---- update ----

    public override void Update()
    {
        if (Phase == 0)
        {
            // The first call after the entrance: the rings finished, phase 1 starts.
            InvincibilityFrame = 0;
            Invincible = false;
            World.Rng = new IRand(World.FrameRand);
            Phase = 1;
            PatternCur = 0;
            PatternsDone = 0;
            PatternsUntilNext = Irand() % 6 + 1;
            PhaseFrame = 0;
            InitialHpRendered = false;
            Left = ShieldLeft;
            Top = ShieldTop;
            BirdsReset();
            return;
        }
        switch (Phase)
        {
            case 1: FormOnePatternFrame(1, 0, 2, 2); break;
            case 3: FormOnePatternFrame(3, 0, 1, 4); break;
            case 5: FormOnePatternFrame(5, 10, 5, 6); break;
            case 7: FormOnePatternFrame(7, 0, 2, 8); break;
            case 2: BgTransitionFrame(1, 3, () => Irand() % 5 + 1); break;
            case 4: BgTransitionFrame(2, 5, () => Irand() % 4 + 3); break;
            case 6: BgTransitionFrame(3, 7, () => Irand() % 5 + 2); break;
            case 8: BgTransitionFrame(0, 1, () => Irand() % 6 + 1); break;
            // 99, 100 and 101: later parts.
        }
    }

    // ---- form 1 ----

    /// <summary>Which background image is shown (0..3); changes at the end of a transition.</summary>
    public int BackgroundId;
    int _gustId;

    /// <summary>pattern_next(): alternates when max is 1, otherwise cycles min..max; counts a finished pattern.</summary>
    void PatternNext(int ringMin, int ringMax)
    {
        PatternCur = ringMax == 1 ? 1 - PatternCur : PatternCur == ringMax ? ringMin : PatternCur + 1;
        PatternsDone++;
    }

    void FormOnePatternFrame(int phase, int ringMin, int ringMax, int nextPhase)
    {
        PhaseFrame++;
        InvincibilityFrame++;
        if (phase is 1 or 7) UpdateBirds();                  // birds_unput_update_render(), before the pattern
        RunPattern(phase, PatternCur);
        if (PhaseFrame == 0) PatternNext(ringMin, ringMax);
        HitUpdate();
        if (Hp <= 0) Phase = PhaseForm1Defeated;
        if (PatternsDone >= PatternsUntilNext && !Invincible)
        {
            PatternsDone = 0;
            Phase = nextPhase;
            PatternCur = 0;
            PhaseFrame = 0;
        }
    }

    /// <summary>
    /// The patterns of each phase. NOT PORTED YET (later parts): until then a pattern ends at once, which zeroes the phase
    /// frame like the originals do when they finish.
    /// </summary>
    void RunPattern(int phase, int pattern)
    {
        if (phase == 1)
        {
            switch (pattern)
            {
                case 0: PurpleLasers(); return;
                case 1: Vortices(); return;
                case 2: BirdsOnEllipseArc(); return;
            }
        }
        PhaseFrame = 0;
    }

    // ---- patterns ----

    const int ColLaser = 4;
    int _state;                                         // pattern_state: the laser speed (times 8) and the like
    public readonly int[] SpawnerX = new int[10], SpawnerY = new int[10];

    /// <summary>playfield_rand_x(): PLAYFIELD_LEFT + fraction_min * W + irand() % ((max - min) * W), the fractions truncated.</summary>
    int RandX(int min, int max) => Playfield.Left + min + Irand() % (max - min);
    int RandY(int min, int max) => Playfield.Top + min + Irand() % (max - min);

    /// <summary>pattern_random_purple_lasers: ten warning circles, each followed by a laser at a random target on the bottom edge.</summary>
    void PurpleLasers()
    {
        const int LaserCount = 10, Interval = 10, Key0 = 50, Key1 = Key0 + LaserCount * Interval, Key2 = Key1 + 50;
        if (PhaseFrame < Key0) return;
        if (PhaseFrame == Key0)
        {
            _state = ByRank(68, 72, 76, 80);            // 8.5 / 9 / 9.5 / 10 times 8
            for (int i = 0; i < LaserCount; i++)
            {
                SpawnerX[i] = RandX(120, 520);          // fractions 0.1875 .. 0.8125 of 640
                SpawnerY[i] = RandY(36, 136);           // fractions 9/84 .. 34/84 of 336
            }
        }
        if (PhaseFrame < Key1)
        {
            int i = (PhaseFrame - Key0) / Interval;
            if (PhaseFrame % Interval == Interval - 1)
                World.Lasers[i].Spawn(SpawnerX[i], SpawnerY[i], RandX(0, Playfield.Right - Playfield.Left), Playfield.Bottom, _state, ColLaser, 25, 5);
        }
        else if (PhaseFrame > Key2) PhaseFrame = 0;
    }

    // ---- birds (30 slots; positions and velocities in 1/16 pixel) ----

    public const int BirdCount = 30, BirdW = 32, BirdH = 32, BirdHatchTicks = 25;
    public const int BpgAimed = 0, BpgSixRing = 1, BpgRandomRain = 2;
    public readonly bool[] BirdAlive = new bool[BirdCount];
    public readonly int[] BirdLeft = new int[BirdCount], BirdTop = new int[BirdCount];
    readonly int[] _birdVx = new int[BirdCount], _birdVy = new int[BirdCount];
    public readonly int[] BirdHatch = new int[BirdCount];

    void BirdsReset() => Array.Clear(BirdAlive);

    /// <summary>birds_spawn(): the first free slot; it is an egg that hatches for 25 ticks, then flies. A left of 0 means "update" in the original.</summary>
    void BirdSpawn(int left, int top, int vx, int vy)
    {
        if (left == 0) { UpdateBirds(); return; }
        for (int i = 0; i < BirdCount; i++)
        {
            if (BirdAlive[i]) continue;
            BirdAlive[i] = true;
            _birdVx[i] = vx;
            _birdVy[i] = vy;
            BirdHatch[i] = BirdHatchTicks;
            BirdLeft[i] = left << 4;
            BirdTop[i] = top << 4;
            return;
        }
    }

    int BirdPelletLeft(int i) => (BirdLeft[i] >> 4) + (BirdW / 2 - Pellets.W / 2);
    int BirdPelletTop(int i) => (BirdTop[i] >> 4) + (BirdH / 2 - Pellets.H / 2);

    void BirdsFire(int group)
    {
        for (int i = 0; i < BirdCount; i++)
        {
            if (!BirdAlive[i] || BirdHatch[i] != 0) continue;
            switch (group)
            {
                case BpgAimed: World.Pellets.AddGroup(BirdPelletLeft(i), BirdPelletTop(i), PelletGroup.OneAimed, 48); break;
                case BpgSixRing:
                    for (int j = 0; j < 6; j++) World.Pellets.AddSingle(BirdPelletLeft(i), BirdPelletTop(i), j * (0x100 / 6), 64);
                    break;
                case BpgRandomRain:
                    World.Pellets.AddSingle(BirdPelletLeft(i), BirdPelletTop(i), (Irand() & 0x7F) + 0x80, 64, PelletMotion.Gravity, 1);
                    break;
            }
        }
    }

    void UpdateBirds()
    {
        for (int i = 0; i < BirdCount; i++)
            if (BirdAlive[i] && BirdHatch[i] > 0) BirdHatch[i]--;
        var w = World;
        for (int i = 0; i < BirdCount; i++)
        {
            if (!BirdAlive[i] || BirdHatch[i] != 0) continue;
            BirdLeft[i] += _birdVx[i];
            BirdTop[i] += _birdVy[i];
            int x = BirdLeft[i], y = BirdTop[i];
            if (x < 0 || x > (ScreenW - 1) * 16 || y < 0 || y > (ScreenH - 1) * 16)
            {
                BirdAlive[i] = false;
                continue;
            }
            int px = x >> 4, py = y >> 4;
            if (!w.PlayerInvincible &&
                px > w.Player.Left - Playfield.PlayerW / 4 && px < w.Player.Left + Playfield.PlayerW / 4 &&
                py > Player.Top - Playfield.PlayerH / 2 - BirdH / 4 && py < Player.Top + Playfield.PlayerH / 2)
            {
                w.PlayerIsHit = true;
                return;
            }
        }
    }

    const int ScreenW = 640, ScreenH = 400;

    // ---- phase 1, pattern 1: vortices ----

    public const int VortexCount = 2, VortexW = 32, VortexH = 32;
    public readonly int[] VortexLeft = new int[VortexCount], VortexTop = new int[VortexCount];
    int _dirFirst, _dirSecond;
    const int VortexPelletSpeed = 7 * 16;

    int VortexPelletLeft(int left) => left + (VortexW / 2 - Pellets.W / 2);
    int VortexPelletTop(int top) => top + (VortexH / 2 - Pellets.H / 2);

    void VortexFireDown(int i) =>
        World.Pellets.AddGroup(VortexPelletLeft(VortexLeft[i]), VortexPelletTop(VortexTop[i]), PelletGroup.One, VortexPelletSpeed);

    void VortexFireSpread(int i)
    {
        int l = VortexPelletLeft(VortexLeft[i]), t = VortexPelletTop(VortexTop[i]);
        World.Pellets.AddSingle(l, t, 0x40, VortexPelletSpeed);
        int angle = AngleMath.Iatan2(Playfield.Bottom - (VortexH / 2 - Pellets.H / 2) - VortexTop[i], VortexLeft[1 - i] - VortexLeft[i]);
        World.Pellets.AddSingle(l, t, angle, VortexPelletSpeed);
        World.Pellets.AddSingle(l, t, (0x80 - angle) & 0xFF, VortexPelletSpeed);
    }

    public bool VorticesShown => Phase == 1 && PatternCur == 1 && PhaseFrame >= 50;

    void Vortices()
    {
        if (PhaseFrame < 50) return;
        if (PhaseFrame == 50) _state = ByRank(140, 145, 150, 155);
        if (PhaseFrame < 100)
        {
            for (int i = 0; i < VortexCount; i++)
            {
                VortexLeft[i] = i * (Playfield.Right - VortexW);
                VortexTop[i] = Playfield.Top + Playfield.Height * 17 / 42;
                if (PhaseFrame % 4 == 0) VortexFireDown(i);
            }
        }
        else if (PhaseFrame < _state)
        {
            for (int i = 0; i < VortexCount; i++)
            {
                VortexLeft[i] += i == 0 ? 5 : -5;
                VortexTop[i] -= 2;
                if (PhaseFrame % 4 == 0)
                {
                    VortexFireDown(i);
                    int angle = AngleMath.Iatan2(Playfield.Bottom - VortexTop[i] + (VortexH / 2 - Pellets.H / 2),
                        i * (Playfield.Width / 8 * 2) - Playfield.Width / 8 + (VortexW / 2 - Pellets.W / 2));
                    World.Pellets.AddSingle(VortexPelletLeft(VortexLeft[i]), VortexPelletTop(VortexTop[i]), angle, VortexPelletSpeed);
                }
            }
        }
        else if (PhaseFrame < 200)
        {
            for (int i = 0; i < VortexCount; i++) if (PhaseFrame % 4 == 0) VortexFireSpread(i);
        }
        else if (PhaseFrame < 240)
        {
            if (PhaseFrame == 200) _dirFirst = Irand() % 2;
            for (int i = 0; i < VortexCount; i++)
            {
                VortexLeft[i] += 2 - _dirFirst * 4;
                if (PhaseFrame % 4 == 0) VortexFireSpread(i);
            }
        }
        else if (PhaseFrame < 320)
        {
            if (PhaseFrame == 240) _dirSecond = Irand() % 2;
            for (int i = 0; i < VortexCount; i++)
            {
                VortexLeft[i] -= _dirSecond != _dirFirst ? 2 - _dirSecond * 4 : 4 - _dirSecond * 8;
                if (PhaseFrame % 4 == 0) VortexFireSpread(i);
            }
        }
        else if (PhaseFrame > 300)
        {
            PhaseFrame = 0;
        }
    }

    // ---- phase 1, pattern 2: birds on an ellipse arc ----

    public const int SpawnCrossW = 32, SpawnCrossH = 32;
    int _eggsAlive, _birdGroup;
    public readonly int[] EggLeft = new int[BirdCount], EggTop = new int[BirdCount];
    public int SpawnerLeft, SpawnerTop;                   // 1/16 pixel
    int _spawnerVx, _spawnerVy;
    public int EggsAlive => _eggsAlive;

    void BirdsOnEllipseArc()
    {
        if (PhaseFrame < 50) return;
        if (PhaseFrame == 50)
        {
            _eggsAlive = 0;
            SpawnerLeft = WandEmitLeft * 16;
            SpawnerTop = WandEmitTop * 16;
            _spawnerVx = (4 - Irand() % 2 * 8) * 16;
            _spawnerVy = (2 - Irand() % 2 * 4) * 16;
            _state = ByRank(20, 15, 10, 8);
        }
        else if (PhaseFrame < 200)
        {
            _spawnerVx += SpawnerLeft < ShieldCenterX * 16 ? 1 : -1;
            _spawnerVy += SpawnerTop < ShieldCenterY * 16 ? 1 : -1;
            SpawnerLeft += _spawnerVx;
            SpawnerTop += _spawnerVy;
            if (PhaseFrame % _state == 0 && _eggsAlive < BirdCount &&
                SpawnerLeft >= 0 && SpawnerTop >= 0 && SpawnerLeft <= (ScreenW - SpawnCrossW) * 16 && SpawnerTop <= (ScreenH - SpawnCrossH) * 16)
            {
                EggLeft[_eggsAlive] = SpawnerLeft >> 4;
                EggTop[_eggsAlive] = SpawnerTop >> 4;
                _eggsAlive++;
            }
        }
        else if (PhaseFrame < 400)
        {
            if (PhaseFrame == 200)
            {
                if (Math.Abs(_spawnerVy) < 4) _spawnerVy = _spawnerVy < 0 ? -4 : 4;
                if (Math.Abs(_spawnerVx) < 4) _spawnerVx = _spawnerVx < 0 ? -4 : 4;
                for (int i = _eggsAlive - 1; i >= 0; i--)
                {
                    int angle = (Irand() & 0x7F) + 0x80;
                    BirdSpawn(EggLeft[i], EggTop[i], AngleMath.Cos8(angle) * 3 / 16, AngleMath.Sin8(angle) * 3 / 16);
                }
                _birdGroup = Irand() % (BpgSixRing + 1);
            }
            if (SpawnerLeft >= -SpawnCrossW * 16 && SpawnerTop >= -SpawnCrossH * 16 && SpawnerLeft <= ScreenW * 16 && SpawnerTop <= ScreenH * 16)
            {
                SpawnerLeft += _spawnerVx;
                SpawnerTop += _spawnerVy;
            }
            if (PhaseFrame % 16 == 0 || PhaseFrame % 16 == 2) BirdsFire(_birdGroup);
        }
        else if (PhaseFrame == 400)
        {
            PhaseFrame = 0;
        }
    }

    /// <summary>Radius of the warning circle of the laser being prepared (drawing only); -1 when none is shown.</summary>
    public int WarningRadius => Phase == 1 && PatternCur == 0 && PhaseFrame is >= 50 and < 150 && PhaseFrame % 10 is not (8 or 9)
        ? 64 - PhaseFrame % 10 * 8 : -1;

    /// <summary>
    /// bg_transition(): 109 ticks (49 of waiting, then three gusts of 20 calls), 100 irand per call from frame 50 on. The new
    /// background shows at the end; then the next phase starts with a new pattern count.
    /// </summary>
    void BgTransitionFrame(int newBackground, int nextPhase, Func<int> rollPatterns)
    {
        PhaseFrame++;
        if (PhaseFrame == 20) _gustId = 0;
        if (PhaseFrame >= 50)
        {
            for (int row = 0; row < 100; row++) Irand();
            _gustCalls++;
            if (_gustCalls >= 20)
            {
                _gustCalls = 0;
                _gustId++;
                PhaseFrame = 49;
                if (_gustId >= 3)
                {
                    BackgroundId = newBackground;
                    PhaseFrame = 0;
                }
            }
        }
        if (PhaseFrame == 0)
        {
            Phase = nextPhase;
            PhaseFrame = 0;
            PatternsUntilNext = rollPatterns();
        }
    }
    int _gustCalls;

    public void HitUpdateForTests() => HitUpdate();

    public override void Hash(ref StateHasher h)
    {
        h.Add(Phase); h.Add(PhaseFrame); h.Add(InvincibilityFrame); h.Add(Hp); h.Add(Invincible); h.Add(PatternCur);
        h.Add(_entranceFrame); h.Add(BackgroundId); h.Add(_state); h.Add(_dirFirst); h.Add(_dirSecond); h.Add(_eggsAlive); h.Add(_birdGroup); h.Add(SpawnerLeft); h.Add(SpawnerTop); h.Add(_spawnerVx); h.Add(_spawnerVy);
        for (int i = 0; i < BirdCount; i++) { h.Add(BirdAlive[i]); h.Add(BirdLeft[i]); h.Add(BirdTop[i]); h.Add(_birdVx[i]); h.Add(_birdVy[i]); h.Add(BirdHatch[i]); h.Add(EggLeft[i]); h.Add(EggTop[i]); }
        for (int i = 0; i < VortexCount; i++) { h.Add(VortexLeft[i]); h.Add(VortexTop[i]); } for (int i = 0; i < 10; i++) { h.Add(SpawnerX[i]); h.Add(SpawnerY[i]); } h.Add(_gustId); h.Add(_gustCalls); h.Add(PatternsDone); h.Add(PatternsUntilNext); h.Add(InitialHpRendered);
        h.Add(Left); h.Add(Top); h.Add(HitboxOrbInactive);
    }
}
