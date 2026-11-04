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

    // ---- entrance length (derived from the original's code, not measured; see the audit) ----
    /// <summary>The scroll: one tick per line (frame_delay(1)), from line 399 up to 0.</summary>
    public const int ScrollTicks = 400;
    /// <summary>
    /// pagetrans_diagonal_8x8(40): (640 / 8) + (400 / 8) = 130 frames of delay(40) = 5200 ms = 293 ticks at 56.423 Hz. TODO(T5)
    /// </summary>
    public const int TransitionFrames = 640 / 8 + 400 / 8, TransitionTicks = 293;
    /// <summary>The frame (0..129) of the diagonal page transition shown after the given number of its ticks (1-based).</summary>
    public static int TransitionFrameAt(int tick) => Math.Clamp(tick * 1000 / 2257, 0, TransitionFrames - 1);

    /// <summary>
    /// The entrance rings: every 4th frame the radius base grows by 16 from 16 and the loop ends when every one of the 128
    /// squares is off the screen; the loop waits one tick per two frames. Simulated here from the same formulas as the drawing.
    /// </summary>
    public static readonly int RingsFrames = ComputeRingsFrames();
    public static readonly int RingsTicks = (RingsFrames + 1) / 2;
    public static readonly int EntranceTotal = ScrollTicks + TransitionTicks + RingsTicks;

    static int ComputeRingsFrames()
    {
        for (int frame = 4; ; frame += 4)
        {
            int radiusBase = 16 + 16 * (frame / 4);
            bool anyOnScreen = false;
            for (int i = 32; i < 5 * 32 && !anyOnScreen; i++)
            {
                int radius = radiusBase * (i / 32), angle = i % 32 * (0x100 / 32);
                int left = AngleMath.PolarY(ScreenW / 2 - 16, radius, angle) & ~7;
                int top = AngleMath.PolarX(ScreenH / 2 - 4 - 16, radius, angle);
                anyOnScreen = !(left < 0 || top < 0 || left + 32 > ScreenW || top + 32 > ScreenH);
            }
            if (!anyOnScreen) return frame;
        }
    }

    // ---- state ----
    int _entranceFrame;
    public int PatternsDone, PatternsUntilNext;
    public bool InitialHpRendered;

    /// <summary>Hit points drawn so far while the bar is still filling up (hud_hp_increment_render: one point per frame).</summary>
    public int HpShown;
    /// <summary>Drawing only: the shield and the dress appear on their first animation frame (10 and 15) and stay until the picture is replaced.</summary>
    public bool ShieldShown, DressShown;
    public int ShieldCel, DressCel;

    void HpIncrement()
    {
        if (InitialHpRendered) return;
        HpShown = Math.Min(Hp, PhaseFrame + 1);
        if (PhaseFrame == Hp - 1) InitialHpRendered = true;
    }

    /// <summary>shield_render_both() and dress_render_both(): the cel changes on its frame multiple and is otherwise left on screen.</summary>
    void ShieldAndDress()
    {
        if (PhaseFrame % 10 == 0) { ShieldShown = true; ShieldCel = PhaseFrame % 40 / 10; }
        DressAnimation();
    }

    void DressAnimation()
    {
        if (PhaseFrame % 15 == 0) { DressShown = true; DressCel = PhaseFrame % 60 / 15; }
    }

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
    public override int DefeatTicks() => 231 * 2;       // phase 101: 231 iterations of frame_delay(2) (shake, fade out)

    int Irand() => World.Rng.Next();

    int ByRank(int easy, int normal, int hard, int lunatic) => World.Rank switch
    {
        Rank.Easy => easy, Rank.Normal => normal, Rank.Hard => hard, Rank.Lunatic => lunatic, _ => normal,
    };

    // ---- entrance ----

    public override bool EntranceFrame() => ++_entranceFrame >= EntranceTotal;

    /// <summary>Ticks of the blocking entrance run so far (drawing only).</summary>
    public int EntranceTick => _entranceFrame;

    /// <summary>How far the background wipe of a transition phase has gone, 0..1 (three gusts of 20 calls).</summary>
    public double TransitionProgress => Phase is 2 or 4 or 6 or 8 && PhaseFrame >= 50 ? (_gustId * 20 + _gustCalls) / 60.0 : 0.0;
    /// <summary>The background the current transition phase leads to.</summary>
    public int NextBackground => Phase switch { 2 => 1, 4 => 2, 6 => 3, _ => 0 };

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
            _wandFrame = 0;
            ShieldShown = DressShown = false;
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
            case PhaseForm1Defeated: Form1Defeated(); break;
            case PhaseForm2: Form2(); break;
        }
    }

    // ---- form 1 ----

    /// <summary>Which background image is shown (0..3); changes at the end of a transition.</summary>
    public int BackgroundId;
    int _gustId;
    public int GustId => _gustId;
    public int GustCalls => _gustCalls;

    /// <summary>pattern_next(): alternates when max is 1, otherwise cycles min..max; counts a finished pattern.</summary>
    void PatternNext(int ringMin, int ringMax)
    {
        PatternCur = ringMax == 1 ? 1 - PatternCur : PatternCur == ringMax ? ringMin : PatternCur + 1;
        PatternsDone++;
    }

    void FormOnePatternFrame(int phase, int ringMin, int ringMax, int nextPhase)
    {
        if (phase == 1) HpIncrement();                       // hud_hp_increment_render(), before the frame counter moves
        PhaseFrame++;
        InvincibilityFrame++;
        if (phase is 1 or 3 or 5 or 7) ShieldAndDress();
        if (phase == 5) ParticlesWavy();
        else if (phase == 3) ParticlesVertical(false);
        else if (phase == 7) ParticlesVertical(true);
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
        if (phase == 7)
        {
            switch (pattern)
            {
                case 0: SymmetricBirdsFromBottom(); break;
                case 1: FourSemicircleSpreads(); break;
                case 2: VerticalStacksThenRain(); break;
            }
            return;
        }
        if (phase == 5)
        {
            switch (pattern)
            {
                case 0: FourAimedLasers(); break;
                case 1: ShakeFor50Frames(); break;
                case 2: FourAimedLasers(); RainFromTop(); break;
                case 3: RadialStacksAndLasers(); break;
                case 4: ShakeFor50Frames(); break;
                case 5: FourAimedLasers(); RainFromTop(); break;
            }
            return;                                         // other values (the 10, 11, ... after the ring) run nothing
        }
        if (phase == 3)
        {
            switch (pattern)
            {
                case 0: DetonatingSnowflake(); TwoRingsFromA2Orbs(); return;
                case 1: AimedSlingClusters(); TwoRingsFromA2Orbs(); return;
            }
        }
        PhaseFrame = 0;
    }

    // ---- patterns ----

    const int ColLaser = 4, ColWhite = 7;
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

    bool _vortWandDone, _arcWandDone;

    void Vortices()
    {
        if (!_vortWandDone) _vortWandDone = WandRaise();
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
            if (PhaseFrame == 200)
            {
                _dirFirst = Irand() % 2;
                if (_dirFirst == 1) WandStage = 0;                 // X_LEFT: wand_lower_both()
            }
            for (int i = 0; i < VortexCount; i++)
            {
                VortexLeft[i] += 2 - _dirFirst * 4;
                if (PhaseFrame % 4 == 0) VortexFireSpread(i);
            }
        }
        else if (PhaseFrame < 320)
        {
            if (PhaseFrame == 240)
            {
                _dirSecond = Irand() % 2;
                if (_dirSecond == 0 && _dirFirst == 0) WandStage = 0;
                else if (_dirSecond == 1 && _dirFirst == 1) _vortWandDone = false;   // X_LEFT twice: the wand is raised again
            }
            for (int i = 0; i < VortexCount; i++)
            {
                VortexLeft[i] -= _dirSecond != _dirFirst ? 2 - _dirSecond * 4 : 4 - _dirSecond * 8;
                if (PhaseFrame % 4 == 0) VortexFireSpread(i);
            }
        }
        else if (PhaseFrame > 300)
        {
            PhaseFrame = 0;
            _vortWandDone = false;
            WandStage = 0;
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
        if (PhaseFrame < 40) return;
        if (!_arcWandDone) _arcWandDone = WandRaise();
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
                    BirdSpawn(EggLeft[i], EggTop[i], AngleMath.PolarX(0, 3, angle) * 16, AngleMath.PolarY(0, 3, angle) * 16);
                }
                _birdGroup = Irand() % (BpgSixRing + 1);
                WandStage = 0;
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
            _arcWandDone = false;
        }
    }

    // ---- the wand-raise animation (render-only in the original, but its static frame is shared by the patterns) ----

    int _wandFrame;

    /// <summary>wand_render_raise_both(): 16 calls to finish (the first only starts it); true on the last.</summary>
    bool WandRaise()
    {
        if (_wandFrame == 0) { _wandFrame = 1; return false; }
        _wandFrame++;
        if (_wandFrame == 2) WandStage = 1;
        if (_wandFrame == 16) { _wandFrame = 0; WandStage = 2; return true; }
        return false;
    }

    /// <summary>Drawing only: 0 = lowered (part of the background picture), 1 = first raised image, 2 = fully raised.</summary>
    public int WandStage;

    // ---- 2x2 particles (decoration, but they draw random numbers and need free slots) ----

    public const int ParticleCount = 30;
    public readonly bool[] ParticleAlive = new bool[ParticleCount];
    public readonly int[] ParticleLeft = new int[ParticleCount], ParticleTop = new int[ParticleCount], ParticleVy = new int[ParticleCount];
    public readonly int[] ParticleAge = new int[ParticleCount];

    /// <summary>particles2x2_vertical_unput_update_render(): one new particle every 7th frame, moved on even frames; gone off the top or bottom edge.</summary>
    void ParticlesVertical(bool fromBottom)
    {
        if (PhaseFrame % 7 == 0)
        {
            for (int i = 0; i < ParticleCount; i++)
            {
                if (ParticleAlive[i]) continue;
                ParticleLeft[i] = Irand() % ScreenW;
                ParticleTop[i] = fromBottom ? ScreenH - 1 - 2 : 0;
                ParticleVy[i] = fromBottom ? -(Irand() % 15) - 8 : Irand() % 15 + 2;
                ParticleAlive[i] = true;
                break;
            }
        }
        if (PhaseFrame % 2 != 0) return;
        for (int i = 0; i < ParticleCount; i++)
        {
            if (!ParticleAlive[i]) continue;
            ParticleTop[i] += ParticleVy[i];
            if (ParticleTop[i] >= ScreenH - 1 || ParticleTop[i] < 0) ParticleAlive[i] = false;   // vram offset past row 398 or negative
        }
    }

    // ---- phase 3, pattern 0: the detonating snowflake ----

    public int SnowflakeState = -1;                       // phase_t, and then the detonation frame (a union in the original)
    public int SnowflakeLeft, SnowflakeTop;               // 1/16 pixel
    int _snowVx, _snowVy;
    public int SnowStarLeft, SnowStarTop, SnowRadiusOuter1, SnowRadiusOuter2, SnowRadiusInner;
    const int SnowHitboxW = 96, SnowRadiusMax = SnowHitboxW / 2, FlakeW = 8, FlakeH = 8;
    public const int SnowFlakeMoving = 2, SnowDetonationStart = 3, SnowDetonationActive = 5;

    void DetonatingSnowflake()
    {
        if (PhaseFrame < 10)
        {
            SnowflakeState = -1;
            _state = 160;
        }
        if (PhaseFrame % _state == 0) SnowflakeState = 0;
        if (SnowflakeState == 0) SnowflakeState = WandRaise() ? 1 : 0;
        if (SnowflakeState == 1)
        {
            World.Sound.PlaySe(6);
            SnowflakeLeft = WandEmitLeft * 16;
            SnowflakeTop = WandEmitTop * 16;
            int tx = World.Player.Left + Playfield.PlayerW / 2 - FlakeW / 2, ty = Player.Top + Playfield.PlayerH / 2 - FlakeH / 2;
            int a = AngleMath.Iatan2(ty - WandEmitTop, tx - WandEmitLeft);
            _snowVx = AngleMath.PolarX(0, 7 * 16, a);
            _snowVy = AngleMath.PolarY(0, 7 * 16, a);
            SnowflakeState = SnowFlakeMoving;
        }
        if (SnowflakeState == SnowFlakeMoving)
        {
            SnowflakeTop += _snowVy;
            if (SnowflakeTop >= (Playfield.Bottom - FlakeH / 2) * 16)
            {
                SnowflakeState = SnowDetonationStart;
                WandStage = 0;
                return;
            }
            SnowflakeLeft += _snowVx;
        }
        if (SnowflakeState == SnowDetonationStart)
        {
            SnowRadiusOuter1 = SnowRadiusMax;
            SnowRadiusOuter2 = 16;
            SnowRadiusInner = 24;
            SnowStarLeft = SnowflakeLeft;                   // ZUN bug: a subpixel assigned to a pixel; affects one frame
            SnowStarTop = Playfield.Bottom - Irand() % SnowRadiusMax;
            World.Sound.PlaySe(10);
        }
        if (SnowflakeState >= SnowDetonationStart) SnowflakeState++;
        if (SnowflakeState >= SnowDetonationActive && SnowflakeState % 8 == 0)
        {
            SnowRadiusOuter1 -= 6;
            SnowRadiusOuter2 += 6;
            SnowRadiusInner += 4;
            SnowStarLeft = Irand() % (SnowHitboxW * 2 / 3) + (SnowflakeLeft >> 4) - (SnowHitboxW * 2 / 6 - FlakeW / 2);
            SnowStarTop = Playfield.Bottom - Irand() % SnowRadiusMax;
            if (SnowRadiusOuter1 <= 8)
            {
                SnowflakeState = -1;
                return;
            }
        }
        if (!World.PlayerInvincible &&
            (SnowflakeLeft >> 4) + (SnowHitboxW / 2 - Playfield.PlayerW / 2) > World.Player.Left &&
            (SnowflakeLeft >> 4) - (SnowHitboxW / 2 + Playfield.PlayerW / 2) < World.Player.Left &&
            SnowflakeState >= SnowDetonationStart)
            World.PlayerIsHit = true;
    }

    // ---- phase 3, both patterns: two rings from the A2 orbs ----

    int _ringsAngleA2, _ringsInterval;

    void TwoRingsFromA2Orbs()
    {
        if (PhaseFrame < 5)
        {
            _ringsAngleA2 = 0;
            _ringsInterval = ByRank(18, 12, 10, 8);
        }
        if (PhaseFrame % _ringsInterval == 0)
        {
            World.Pellets.SpawnWithCloud = true;           // never turned off again in the original (see 33.5)
            World.Pellets.AddSingle(A2OrbsRightCenterX, A2OrbsCenterY, (0x00 - _ringsAngleA2) & 0xFF, 56);
            World.Pellets.AddSingle(A2OrbsRightCenterX, A2OrbsCenterY, (0x80 - _ringsAngleA2) & 0xFF, 56);
            World.Pellets.AddSingle(A2OrbsLeftCenterX, A2OrbsCenterY, (0x00 + _ringsAngleA2) & 0xFF, 56);
            World.Pellets.AddSingle(A2OrbsLeftCenterX, A2OrbsCenterY, (0x80 + _ringsAngleA2) & 0xFF, 56);
            _ringsAngleA2 = (_ringsAngleA2 + _ringsInterval) & 0xFF;
        }
        if (PhaseFrame > 479) PhaseFrame = 0;
    }

    // ---- phase 3, pattern 1: aimed sling clusters ----

    void AimedSlingClusters()
    {
        if (PhaseFrame == 10) _state = ByRank(4, 3, 2, 2);
        if (PhaseFrame % 200 >= 150 && PhaseFrame % _state == 0)
        {
            int angle = Irand() & 0xFF;
            int left = AngleMath.PolarY(Playfield.CenterX, Playfield.Width / 16, angle);
            int top = AngleMath.PolarX(SealCenterY - Pellets.H / 2 - 1, Playfield.Height * 5 / 42, angle);
            World.Pellets.AddSingle(left, top, 0, 0, PelletMotion.SlingAimed, 72);
        }
        if (PhaseFrame >= 299) PhaseFrame = 0;
    }

    // ---- wavy particles (phase 5; decoration that draws random numbers) ----

    public readonly bool[] WavyAlive = new bool[ParticleCount];
    public readonly int[] WavyLeft = new int[ParticleCount], WavyTop = new int[ParticleCount], WavyAge = new int[ParticleCount];

    /// <summary>particles2x2_wavy_unput_update_render(): one new particle every 7th frame at a random x on the bottom edge; rising 1 px on even frames for 100 of them.</summary>
    void ParticlesWavy()
    {
        if (PhaseFrame % 7 == 0)
        {
            for (int i = 0; i < ParticleCount; i++)
            {
                if (WavyAlive[i]) continue;
                WavyLeft[i] = Irand() % ScreenW;
                WavyTop[i] = ScreenH;
                WavyAge[i] = 0;
                WavyAlive[i] = true;
                break;
            }
        }
        if (PhaseFrame % 2 != 0) return;
        for (int i = 0; i < ParticleCount; i++)
        {
            if (!WavyAlive[i]) continue;
            WavyTop[i]--;
            WavyAge[i]++;
            if (WavyAge[i] >= 100) WavyAlive[i] = false;
        }
    }

    // ---- phase 5, patterns 0/2/5: four aimed lasers ----

    const int SealOriginDistanceX1 = SealCenterX - SealRadius, FaceOriginY = FaceCenterY;
    int _laserTargetFirst;
    /// <summary>Where the aiming ray of the four-lasers pattern starts (drawing only; the lasers use the same origins).</summary>
    public int RayOriginX, RayOriginY;

    /// <summary>The aiming ray of the four-lasers pattern: starts at the origin of the laser that was just fired and moves by this velocity each frame.</summary>
    public int RayVelX, RayVelY, RayStartFrame;

    void FireAimedLaser(int slot, int originX, int originY, int targetLeft)
    {
        int angle = AngleMath.Iatan2(Playfield.Bottom - originY, targetLeft - originX);
        RayVelX = AngleMath.PolarX(0, 16, angle);
        RayVelY = AngleMath.PolarY(0, 16, angle);
        RayStartFrame = PhaseFrame;
        World.Lasers[slot].Spawn(originX, originY, targetLeft, Playfield.Bottom, _state, ColWhite, 50, 8);
        World.Sound.PlaySe(6);
    }

    void FourAimedLasers()
    {
        if (PhaseFrame < 50) return;
        int playerLeft = World.Player.Left;
        if (PhaseFrame == 50)
        {
            RayOriginX = Playfield.Right - SealOriginDistanceX1; RayOriginY = FaceOriginY;
            _laserTargetFirst = playerLeft;
            _state = ByRank(60, 64, 68, 72);               // 7.5 / 8 / 8.5 / 9 times 8
            FireAimedLaser(0, RayOriginX, RayOriginY, playerLeft);
        }
        else if (PhaseFrame == 100)
        {
            RayOriginX = Playfield.Left + SealOriginDistanceX1; RayOriginY = FaceOriginY;
            FireAimedLaser(1, RayOriginX, RayOriginY, Playfield.Right - 1 - _laserTargetFirst);
        }
        else if (PhaseFrame == 150)
        {
            RayOriginX = Playfield.Right - Playfield.Width * 4 / 80; RayOriginY = ShieldCenterY;
            FireAimedLaser(2, RayOriginX, RayOriginY, playerLeft);
        }
        else if (PhaseFrame == 200)
        {
            RayOriginX = Playfield.Left + Playfield.Width * 3 / 80; RayOriginY = ShieldCenterY;
            FireAimedLaser(3, RayOriginX, RayOriginY, playerLeft);
        }
        else if (PhaseFrame > 300)
        {
            PhaseFrame = 0;
        }
    }

    // ---- phase 5, patterns 1/4: the screen shakes for 50 frames ----

    void ShakeFor50Frames()
    {
        if (PhaseFrame == 50) PhaseFrame = 0;
    }

    /// <summary>The scroll line the shake pattern sets (drawing only; 0 when it is not shaking). Alternates 396 / 4 every 4 frames.</summary>
    public int ShakeLine => Phase == 5 && PatternCur is 1 or 4 && PhaseFrame is > 0 and < 50 ? (PhaseFrame % 8 < 4 ? ScreenH - 4 : 4) : 0;

    // ---- phase 5, patterns 2/5: rain from the top ----

    void RainFromTop()
    {
        if (PhaseFrame % 8 != 0) return;
        int left = Playfield.Left + Irand() % Playfield.Width;
        var group = World.Rank == Rank.Lunatic ? PelletGroup.OneRandomWide : PelletGroup.One;
        World.Pellets.AddGroup(left, Playfield.Top, group, 44);
    }

    // ---- phase 5, pattern 3: radial stacks and lasers ----

    int _radialAngle;

    void RadialStacksAndLasers()
    {
        const int centerX = Playfield.CenterX, centerY = ShieldCenterY - 8 / 2;
        if (PhaseFrame < 150) return;
        if (PhaseFrame == 150)
        {
            _radialAngle = 0;
            _state = ByRank(60, 65, 70, 75);               // 7.5 / 8.125 / 8.75 / 9.375 times 8
            World.Sound.PlaySe(6);
        }
        else if (PhaseFrame < 200) return;                  // the ray sweeps (drawing only)
        else if (PhaseFrame == 200) _radialAngle = 0;
        else if (PhaseFrame <= 400)
        {
            if (PhaseFrame % 5 != 0) return;
            int a = _radialAngle;
            int targetX = AngleMath.PolarX(centerX, 600, a), targetY = AngleMath.PolarY(centerY, 600, a);
            World.Sound.PlaySe(7);
            if (PhaseFrame % 15 == 0)
                World.Lasers[(PhaseFrame - 215) / 15].Spawn(centerX, centerY, targetX, targetY, _state, ColWhite, 20, 4);
            else
                World.Pellets.AddSingle(centerX, centerY, a, 100 /* to_sp(6.25) */);
            World.Pellets.AddSingle(centerX, centerY, (a - 0x04) & 0xFF, 96);
            World.Pellets.AddSingle(centerX, centerY, (a + 0x04) & 0xFF, 96);
            if (World.Rank >= Rank.Hard)
            {
                World.Pellets.AddSingle(centerX, centerY, (a - 0x04) & 0xFF, 64);
                World.Pellets.AddSingle(centerX, centerY, (a + 0x04) & 0xFF, 64);
                if (World.Rank == Rank.Lunatic)
                {
                    World.Pellets.AddSingle(centerX, centerY, a, 64);
                    World.Pellets.AddSingle(centerX, centerY, a, 48);
                }
            }
            _radialAngle = (a + 0x0C) & 0xFF;
        }
        else if (PhaseFrame == 450) PhaseFrame = 0;
    }

    // ---- phase 7: the symmetric spawn rays (SymmetricSpawnraysWithDebris) ----

    /// <summary>Left ray target x (the right one mirrors it: 639 - x) and y; the rays start at the face. Drawing reads these.</summary>
    public int RaysTargetLeftX, RaysTargetY;
    int RaysTargetX(bool right) => right ? Playfield.Right - 1 - RaysTargetLeftX : RaysTargetLeftX;

    /// <summary>fire_random_to_bottom(): aimed from the ray target at a random x on the floor; one random number.</summary>
    void FireRandomToBottom(bool right, int speed)
    {
        int angle = AngleMath.Iatan2(Playfield.Bottom - RaysTargetY, Playfield.Left + Irand() % Playfield.Width - RaysTargetLeftX) & 0xFF;
        World.Pellets.AddSingle(RaysTargetX(right), RaysTargetY, angle, speed);
    }

    // ---- phase 7, pattern 0: symmetric birds from the bottom ----

    void SymmetricBirdsFromBottom()
    {
        const int velocityX = 2;
        if (PhaseFrame < 50) return;
        if (PhaseFrame == 50)
        {
            _state = ByRank(6, 7, 8, 9);
            RaysTargetLeftX = velocityX;
            RaysTargetY = Playfield.Bottom - 1;
            return;
        }
        if (PhaseFrame < 100)
        {
            RaysTargetLeftX += velocityX;
            if (PhaseFrame % 10 == 0)
            {
                World.Sound.PlaySe(6);
                int targetX = (PhaseFrame - 60) * 10 + RaysTargetLeftX;
                int angle = AngleMath.Iatan2(0 - (RaysTargetY - BirdH), targetX - RaysTargetLeftX);
                int vx = AngleMath.PolarX(0, 4, angle), vy = AngleMath.PolarY(0, 4, angle);
                BirdSpawn(RaysTargetX(false), RaysTargetY - BirdH, vx * 16, vy * 16);
                BirdSpawn(RaysTargetX(true), RaysTargetY - BirdH, -vx * 16, vy * 16);
            }
            if (PhaseFrame % 6 == 0) BirdsFire(BpgRandomRain);
            return;
        }
        if (PhaseFrame == 100) return;
        if (PhaseFrame % 10 == 0) BirdsFire(BpgRandomRain);
        if (PhaseFrame >= 300) PhaseFrame = 0;
    }

    // ---- phase 7, pattern 1: four semicircle spreads ----

    void FourSemicircleSpreads()
    {
        const int originX = ShieldCenterX, originY = ShieldCenterY - 8 / 2;
        void Ring(int count, int angle, int step, int speed)
        {
            for (int i = 0; i < count; i++, angle += step) World.Pellets.AddSingle(originX, originY, angle & 0xFF, speed);
        }
        if (PhaseFrame < 50) return;
        if (PhaseFrame == 50) Ring(20, 0x00, 0x07, 32);
        else if (PhaseFrame == 60) Ring(10, 0x04, 0x0C, 48);
        else if (PhaseFrame == 70) Ring(8, 0x04, 0x0E, 64);
        else if (PhaseFrame == 80) Ring(10, 0x0E, 0x0A, 48);
        else if (PhaseFrame > 150) PhaseFrame = 0;
    }

    // ---- phase 7, pattern 2: vertical stacks from the bottom, then random rain from the top ----

    void VerticalStacksThenRain()
    {
        const int columnInterval = 10, columnCount = 20, rayVelocityX = Playfield.Width / 80, columnDistanceX = Playfield.Width / columnCount;
        const int key0 = 50, key1 = key0 + Playfield.Width / rayVelocityX, key2 = key1 + 20;
        const int key3 = key2 + columnCount * columnInterval, key4 = key3 + columnCount * columnInterval, key5 = key4 + 50;
        if (PhaseFrame < key0) return;
        if (PhaseFrame == key0)
        {
            _state = ByRank(16, 13, 10, 8);
            RaysTargetLeftX = 2;
            RaysTargetY = Playfield.Bottom - 1;
        }
        else if (PhaseFrame < key1)
        {
            RaysTargetLeftX += rayVelocityX;
        }
        else if (PhaseFrame == key1)
        {
            RaysTargetLeftX = Playfield.Left;
        }
        else if (PhaseFrame > key2 && PhaseFrame < key3)
        {
            if (PhaseFrame % columnInterval == 0)
            {
                int playerCenterX = World.Player.Left + Playfield.PlayerW / 2;
                for (int i = 0; i < 4 * 16; i += 16)
                {
                    if (Math.Abs(playerCenterX - RaysTargetX(false)) > Playfield.PlayerW)
                        World.Pellets.AddSingle(RaysTargetX(false), RaysTargetY - Pellets.H, 0xC0, i + 48);
                    if (Math.Abs(playerCenterX - RaysTargetX(true)) > Playfield.PlayerW)
                        World.Pellets.AddSingle(RaysTargetX(true), RaysTargetY - Pellets.H, 0xC0, i + 48);
                }
                RaysTargetLeftX += columnDistanceX;
                World.Sound.PlaySe(7);
            }
        }
        else if (PhaseFrame == key3)
        {
            RaysTargetLeftX = Playfield.Left;
            RaysTargetY = Playfield.Top;
        }
        else if (PhaseFrame < key4)
        {
            // (also reached from frame 131 to 150, as in the original: the rain starts from the bottom rays before the columns)
            if (PhaseFrame % _state == 0)
            {
                for (int i = 0; i < 2; i++)
                {
                    FireRandomToBottom(false, 48);
                    FireRandomToBottom(true, 48);
                }
                RaysTargetLeftX += columnDistanceX;
            }
        }
        else if (PhaseFrame > key5) PhaseFrame = 0;
    }

    // ---- form 1 defeated (phase 99): a blocking animation in the original ----

    /// <summary>Ticks of the diagonal page transition after the 200 frames of shaking (ASSUMPTION: 130 frames of about 2 ticks). TODO(T5)</summary>
    public const int Form1DefeatShakeTicks = 200, Form1DefeatTransitionFrames = 130, Form1DefeatTransitionTicks = 260;
    bool _form1DefeatStarted;

    /// <summary>The session must not run anything else while this is true: the original blocks inside this animation.</summary>
    public override bool BlockingTransition => Phase == PhaseForm1Defeated;

    /// <summary>Scroll line of the shake (drawing only): alternates every frame during the first 200.</summary>
    public int Form1DefeatShakeLine => Phase == PhaseForm1Defeated && PhaseFrame is >= 1 and < Form1DefeatShakeTicks
        ? (PhaseFrame % 2 == 0 ? ScreenH + 16 : ScreenH - 14) % ScreenH : 0;

    /// <summary>True on the even frames of the shake, when the original flashes palette entry 15 to white.</summary>
    public bool Form1DefeatFlash => Phase == PhaseForm1Defeated && PhaseFrame is >= 1 and < Form1DefeatShakeTicks && PhaseFrame % 2 == 0;

    void Form1Defeated()
    {
        var w = World;
        if (!_form1DefeatStarted)
        {
            _form1DefeatStarted = true;
            PhaseFrame = 0;
            InvincibilityFrame = 399;
            w.Shots.Reset();
            w.Pellets.ResetNonClouds();
            w.Lasers.ResetAll();
            w.Orb.StageStart();                              // orb_cur / force / velocity back to their start values
            w.Player.Left = Playfield.PlayerLeftStart;
            w.Player.Reset();
            Invincible = false;
            w.Rng = new IRand(w.FrameRand);
            return;
        }
        PhaseFrame++;
        if (PhaseFrame <= Form1DefeatShakeTicks) return;
        // frames 201 on: the diagonal page transition, then the second form starts
        if (PhaseFrame < Form1DefeatShakeTicks + Form1DefeatTransitionTicks) return;
        PhaseFrame = 0;
        InvincibilityFrame = 0;
        PatternCur = 0;
        PatternsDone = 0;                                    // pulse_fade_direction shares this slot
        Phase = PhaseForm2;
        w.PlayerInvincibilityTime = 0;
        w.PlayerInvincible = false;
        Hp = HpForm2;
        InitialHpRendered = false;
        ShieldShown = DressShown = false;
        _pulseR = _pulseG = _pulseB = 0;
        Array.Clear(_leafFlag);                              // pattern_swaying_leaves(frame, 999)
    }

    // ---- form 2 (phase 100) ----

    public int PulseR => _pulseR;
    public int PulseG => _pulseG;
    public int PulseB => _pulseB;
    int _pulseR, _pulseG, _pulseB;

    // horizontal particles (decoration; random numbers and free slots only). Positions and velocity in tenths of a pixel.
    public readonly bool[] HParticleAlive = new bool[ParticleCount];
    public readonly int[] HParticleLeft = new int[ParticleCount], HParticleTop = new int[ParticleCount], HParticleVx = new int[ParticleCount];

    void ParticlesHorizontal(int frame)
    {
        if (frame % 7 == 0)
        {
            for (int i = 0; i < ParticleCount; i++)
            {
                if (HParticleAlive[i]) continue;
                HParticleLeft[i] = Irand() % ScreenW * 10;
                HParticleTop[i] = Irand() % ScreenH;
                HParticleVx[i] = Irand() % 2 == 0 ? -60 : 60;
                HParticleAlive[i] = true;
                break;
            }
        }
        if (frame % 2 != 0) return;
        for (int i = 0; i < ParticleCount; i++)
        {
            if (!HParticleAlive[i]) continue;
            HParticleLeft[i] += HParticleVx[i];
            HParticleVx[i] += HParticleVx[i] < 0 ? 1 : -1;
            if (HParticleLeft[i] >= ScreenW * 10 || HParticleLeft[i] < 0 || Math.Abs(HParticleVx[i]) < 5) HParticleAlive[i] = false;
        }
    }

    // the curved spray: pellets from the seal centre at the angle of a target sweeping along the floor, slowing down
    sealed class CurvedSpray
    {
        public int TargetX, TargetY, Speed, SubpatternId;
    }

    readonly CurvedSpray _sprayOnce = new(), _sprayTwice = new();

    void SprayInit(CurvedSpray sp)
    {
        sp.TargetX = Playfield.Left;
        sp.TargetY = Playfield.Bottom;
        sp.Speed = 7 * 16;
        sp.SubpatternId = 0;
        _state = ByRank(Playfield.Width / 20, 24, 18, Playfield.Width / 40);     // 640/26.66 and 640/35.55, truncated
    }

    void SprayFireAndAdvance(CurvedSpray sp, bool right)
    {
        int angle = AngleMath.Iatan2(sp.TargetY - SealCenterY, sp.TargetX - SealCenterX);
        World.Pellets.AddSingle(SealCenterX, SealCenterY, angle, sp.Speed);
        sp.Speed -= 4;
        sp.TargetX += right ? _state : -_state;
    }

    void CurvedSprayLeftRightOnce()
    {
        if (PhaseFrame < 100) return;
        var sp = _sprayOnce;
        if (PhaseFrame == 100) SprayInit(sp);
        if (sp.SubpatternId == 0)
        {
            SprayFireAndAdvance(sp, true);
            if (sp.TargetX >= Playfield.Right) { sp.SubpatternId = 1; sp.Speed = 7 * 16; sp.TargetX = Playfield.Right; }
        }
        else
        {
            SprayFireAndAdvance(sp, false);
            if (sp.TargetX < Playfield.Left) PhaseFrame = 0;
        }
    }

    void CurvedSprayLeftRightTwice()
    {
        if (PhaseFrame < 80) return;
        var sp = _sprayTwice;
        if (PhaseFrame == 80) SprayInit(sp);
        if (sp.SubpatternId % 2 == 0)
        {
            SprayFireAndAdvance(sp, true);
            if (sp.TargetX >= Playfield.Right) { sp.SubpatternId++; sp.Speed = 7 * 16; sp.TargetX = Playfield.Right; }
        }
        else
        {
            SprayFireAndAdvance(sp, false);
            if (sp.TargetX < Playfield.Left)
            {
                sp.SubpatternId++;
                sp.TargetX = Playfield.Left;
                sp.Speed = 7 * 16;
                if (sp.SubpatternId == 4) PhaseFrame = 0;
            }
        }
    }

    /// <summary>The rain from the seal centre: a spawn ray climbs from the floor to the seal (drawing reads RaysTarget*), then a row of rain pellets.</summary>
    void RainFromSealCenter()
    {
        const int velocityY = 8;
        if (PhaseFrame < 50) return;
        if (PhaseFrame == 50)
        {
            RaysTargetLeftX = Playfield.Left;
            RaysTargetY = Playfield.Bottom - 1;
            _state = ByRank(30, 35, 40, 45);
        }
        if (RaysTargetY > SealCenterY)
        {
            if (PhaseFrame % 2 == 1) RaysTargetY -= velocityY;
            return;
        }
        World.Sound.PlaySe(3);
        for (int i = 0; i < _state; i++)
            World.Pellets.AddSingle(Playfield.Left + Playfield.Width / _state * i, SealCenterY, Irand() & 0xFF, 16, PelletMotion.Gravity, 1);
        PhaseFrame = 0;
    }

    // ---- swaying leaves ----

    public const int LeafCount = 30, LeafW = 8, LeafH = 8;
    public const int LfFree = 0, LfSpark = 1, LfSplash = 2, LfSplashDone = LfSplash + 3, LfLeaf = LfSplashDone + 1;
    public readonly int[] _leafFlag = new int[LeafCount];
    public readonly int[] LeafLeft = new int[LeafCount], LeafTop = new int[LeafCount];          // tenths of a pixel
    readonly int[] _leafVx = new int[LeafCount], _leafVy = new int[LeafCount];
    public int[] LeafFlag => _leafFlag;
    public int LeafVelocityX(int i) => _leafVx[i];

    bool LeafOnScreen(int i) => LeafLeft[i] >= 0 && LeafTop[i] >= 0 && LeafLeft[i] < (ScreenW - LeafW) * 10 && LeafTop[i] < (ScreenH - LeafH) * 10;

    void SwayingLeaves(int spawnInterval)
    {
        int frame = PhaseFrame;
        if (spawnInterval < 1) spawnInterval = 1;          // never 0 in the original's order of patterns; keeps a forced pattern from dividing by zero
        if (frame % spawnInterval == 0)
        {
            for (int i = 0; i < LeafCount; i++)
            {
                if (_leafFlag[i] != LfFree) continue;
                LeafLeft[i] = Playfield.Left * 10 + Irand() % (Playfield.Width * 10);
                LeafTop[i] = FaceCenterY * 10 + Irand() % (Playfield.Height * 25 / 84 * 10);
                int angle = AngleMath.Iatan2(Playfield.Top - LeafTop[i] / 10, Irand() % Playfield.Width - LeafLeft[i] / 10);
                _leafVx[i] = AngleMath.PolarX(0, 40, angle);
                _leafVy[i] = AngleMath.PolarY(0, 40, angle);
                _leafFlag[i] = LfSpark;
                break;
            }
        }
        if (frame % 2 != 0) return;
        for (int i = 0; i < LeafCount; i++)
        {
            int f = _leafFlag[i];
            if (f == LfFree) continue;
            if (f == LfSpark)
            {
                LeafLeft[i] += _leafVx[i];
                LeafTop[i] += _leafVy[i];
                if (LeafTop[i] < Playfield.Top * 10) _leafFlag[i] = LfSplash;
            }
            else if (f <= LfSplashDone)
            {
                if (f == LfSplashDone)
                {
                    _leafFlag[i] = LfLeaf;
                    _leafVy[i] = 3 + Irand() % 2;
                    _leafVx[i] = 2;
                }
            }
            else if (f == LfLeaf)
            {
                LeafLeft[i] += _leafVx[i];
                LeafTop[i] += _leafVy[i];
                _leafVy[i]--;
                if (_leafVy[i] < -1)
                {
                    _leafVy[i] = 30 + Irand() % 20;
                    _leafVx[i] = _leafVx[i] < 0 ? +20 : -20;
                }
                if (LeafTop[i] / 10 > Playfield.Bottom) _leafFlag[i] = LfFree;
            }
        }
        var w = World;
        for (int i = 0; i < LeafCount; i++)
        {
            int f = _leafFlag[i];
            if (f == LfFree || f == LfSpark) continue;
            if (f <= LfSplashDone)
            {
                if (frame % 4 == 0) _leafFlag[i]++;
            }
            else if (f == LfLeaf)
            {
                if (LeafTop[i] > Player.Top * 10 && LeafTop[i] < (Player.Top + Playfield.PlayerH - LeafH) * 10 &&
                    LeafLeft[i] / 10 > w.Player.Left && LeafLeft[i] / 10 < w.Player.Left + Playfield.PlayerW - LeafW &&
                    !w.PlayerInvincible)
                    w.PlayerIsHit = true;
            }
        }
    }

    // ---- the second form ----

    void Form2()
    {
        HpIncrement();                                       // the second form's bar fills up too
        PhaseFrame++;
        InvincibilityFrame++;
        ParticlesHorizontal(PhaseFrame);
        switch (PatternCur)
        {
            case 0:
                _state = ByRank(56, 32, 24, 20);
                if (PhaseFrame > 100) PhaseFrame = 0;
                break;
            case 1: CurvedSprayLeftRightOnce(); break;
            case 2: RainFromSealCenter(); break;
            case 3: CurvedSprayLeftRightTwice(); break;
            case 4:
                SwayingLeaves(_state);
                if (PhaseFrame > 300) PhaseFrame = 0;
                break;
            case 5:
                SwayingLeaves(500);
                if (PhaseFrame > 200) PhaseFrame = 0;
                break;
            case 6: SwayingLeaves(500); CurvedSprayLeftRightOnce(); break;
            case 7: SwayingLeaves(500); RainFromSealCenter(); break;
            case 8: SwayingLeaves(32); CurvedSprayLeftRightTwice(); break;
        }
        if (PhaseFrame == 0) PatternNext(4, 8);
        HitUpdate();
        if (PhaseFrame % 10 == 0)
        {
            // the pulse of palette entry 6; PatternsDone doubles as the fade direction (a union in the original)
            if (PatternsDone == 0)
            {
                if (_pulseR < 0xA) _pulseR++; else PatternsDone = 1;
                if (_pulseG < 0xA) _pulseG++;
                if (_pulseB < 0x6) _pulseB++;
            }
            else
            {
                if (_pulseR > 0) _pulseR--; else PatternsDone = 0;
                if (_pulseG > 0) _pulseG--;
                if (_pulseB > 0) _pulseB--;
            }
        }
        if (Hp <= 0)
        {
            Phase = PhaseForm2Defeated;
            PhaseFrame = 0;
            InvincibilityFrame = 0;
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
        if (PhaseFrame == 0 && _gustId >= 3) ShieldShown = false;      // the new picture has no shield on it
        DressAnimation();                                    // dress_render_both() after every call of the transition
        if (PhaseFrame == 0)
        {
            Phase = nextPhase;
            PhaseFrame = 0;
            PatternsUntilNext = rollPatterns();
            WandStage = 0;
        }
    }
    int _gustCalls;

    public void HitUpdateForTests() => HitUpdate();

    public override void Hash(ref StateHasher h)
    {
        h.Add(Phase); h.Add(PhaseFrame); h.Add(InvincibilityFrame); h.Add(Hp); h.Add(Invincible); h.Add(PatternCur);
        h.Add(_entranceFrame); h.Add(BackgroundId); h.Add(_state); h.Add(_wandFrame); h.Add(_vortWandDone); h.Add(_arcWandDone); h.Add(SnowflakeState); h.Add(SnowflakeLeft); h.Add(SnowflakeTop); h.Add(_snowVx); h.Add(_snowVy);
        h.Add(SnowStarLeft); h.Add(SnowStarTop); h.Add(SnowRadiusOuter1); h.Add(SnowRadiusOuter2); h.Add(SnowRadiusInner); h.Add(_ringsAngleA2); h.Add(_ringsInterval);
        for (int i = 0; i < ParticleCount; i++) { h.Add(ParticleAlive[i]); h.Add(ParticleLeft[i]); h.Add(ParticleTop[i]); h.Add(ParticleVy[i]); h.Add(ParticleAge[i]); }
        h.Add(HpShown); h.Add(ShieldShown); h.Add(DressShown); h.Add(ShieldCel); h.Add(DressCel); h.Add(RayVelX); h.Add(RayVelY); h.Add(RayStartFrame); h.Add(_laserTargetFirst); h.Add(_radialAngle); h.Add(RayOriginX); h.Add(RayOriginY);
        for (int i = 0; i < ParticleCount; i++) { h.Add(WavyAlive[i]); h.Add(WavyLeft[i]); h.Add(WavyTop[i]); h.Add(WavyAge[i]); }
        h.Add(RaysTargetLeftX); h.Add(RaysTargetY);
        h.Add(_form1DefeatStarted); h.Add(_pulseR); h.Add(_pulseG); h.Add(_pulseB);
        foreach (var sp in new[] { _sprayOnce, _sprayTwice }) { h.Add(sp.TargetX); h.Add(sp.TargetY); h.Add(sp.Speed); h.Add(sp.SubpatternId); }
        for (int i = 0; i < ParticleCount; i++) { h.Add(HParticleAlive[i]); h.Add(HParticleLeft[i]); h.Add(HParticleTop[i]); h.Add(HParticleVx[i]); }
        for (int i = 0; i < LeafCount; i++) { h.Add(_leafFlag[i]); h.Add(LeafLeft[i]); h.Add(LeafTop[i]); h.Add(_leafVx[i]); h.Add(_leafVy[i]); }
        h.Add(_dirFirst); h.Add(_dirSecond); h.Add(_eggsAlive); h.Add(_birdGroup); h.Add(SpawnerLeft); h.Add(SpawnerTop); h.Add(_spawnerVx); h.Add(_spawnerVy);
        for (int i = 0; i < BirdCount; i++) { h.Add(BirdAlive[i]); h.Add(BirdLeft[i]); h.Add(BirdTop[i]); h.Add(_birdVx[i]); h.Add(_birdVy[i]); h.Add(BirdHatch[i]); h.Add(EggLeft[i]); h.Add(EggTop[i]); }
        for (int i = 0; i < VortexCount; i++) { h.Add(VortexLeft[i]); h.Add(VortexTop[i]); } for (int i = 0; i < 10; i++) { h.Add(SpawnerX[i]); h.Add(SpawnerY[i]); } h.Add(_gustId); h.Add(_gustCalls); h.Add(PatternsDone); h.Add(PatternsUntilNext); h.Add(InitialHpRendered);
        h.Add(Left); h.Add(Top); h.Add(HitboxOrbInactive);
    }
}
