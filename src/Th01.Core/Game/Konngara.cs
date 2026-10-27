using Th01.Core.Rng;

namespace Th01.Core.Game;

/// <summary>
/// The Jigoku stage 20 boss, Konngara: 18 HP, hit only by the Orb (and shots) inside a box around the head. Phases 1, 3,
/// 5 and 7 pick patterns at random, phases 2, 4 and 6 only flash the seal syllables. State and timing only: the head,
/// face, slash images, snakes and sword ray are drawn from the public state. The blocking entrance and defeat sequences
/// are given estimated lengths. Rules: GAMEPLAY-NOTES.md section 32.
/// ref: th01/main/boss/b20j.cpp
/// </summary>
public sealed class Konngara : Boss
{
    // ---- coordinates ----
    public const int HitboxLeft = 288, HitboxTop = 120;
    public const int HitboxShotW = 112, HitboxShotH = 56, HitboxOrbW = 96, HitboxOrbH = 72;
    public const int SwordCenterX = 410, SwordCenterY = 70;
    public const int Slash4CornerX = 432, Slash4CornerY = 232, Slash5CornerX = 198, Slash5CornerY = 198;
    public const int CupLeft = 296, CupTop = 188, CupW = 32, LeftSleeveLeft = 290, LeftSleeveTop = 200;
    public const int EyeCenterX = 316, EyeBottom = 140;
    const int CupRight = CupLeft + CupW, CupCenterX = CupLeft + CupW / 2;
    public const int DiamondW = 8, DiamondH = 8;
    public const int HpTotal = 18, HpFirstWhiteValue = 16, HpFirstRedWhiteValue = 10, HitScore = 10000;
    public const int Id = 4 * StageSetup.StagesPerScene - 1;           // stage 20, Jigoku route only

    // slash spawner path
    const int Dist2To4X = Slash4CornerX - SwordCenterX, Dist2To4Y = Slash4CornerY - SwordCenterY;
    const int Dist5To4X = Slash5CornerX - Slash4CornerX, Dist5To4Y = Slash5CornerY - Slash4CornerY;
    const int Dist5To6X = Slash5CornerX - SwordCenterX, Dist5To6Y = Slash5CornerY - SwordCenterY;
    const int Slash0 = 50, Slash1 = 60, Slash2 = 100, Slash3 = 120, Slash4 = 140, Slash45 = 150, Slash5 = 160, Slash6 = 170;
    const int FramesFrom2To4 = Slash4 - Slash2, FramesFrom5To45 = Slash45 - Slash5, FramesFrom45To6 = Slash6 - Slash45;

    const int ChooseNew = 99;
    const int SnakeTrail = 5, PixelNoneNeg = 999;
    const int SnakeHomingThreshold = Playfield.Top + Playfield.Height * 5 / 7 - DiamondH / 2;

    // face_direction_t / face_expression_t
    public const int FdRight = 0, FdLeft = 1, FdCenter = 2;
    public const int FeNeutral = 0, FeClosed = 1, FeGlare = 2, FeAim = 3;

    /// <summary>Several snakes with a trail of five diamonds each (drawn, and hurt by the head point).</summary>
    public sealed class Snakes
    {
        public readonly int[][] Left, Top;
        public readonly int[] VelX, VelY, TargetLeft;
        public readonly bool[] Locked;
        public Snakes(int count)
        {
            Left = new int[count][]; Top = new int[count][];
            for (int i = 0; i < count; i++) { Left[i] = new int[SnakeTrail]; Top[i] = new int[SnakeTrail]; }
            VelX = new int[count]; VelY = new int[count]; TargetLeft = new int[count]; Locked = new bool[count];
        }
        public int Count => VelX.Length;
        public bool Alive(int i) => Left[i][0] != PixelNoneNeg;
        public void Hash(ref StateHasher h)
        {
            for (int i = 0; i < Count; i++)
            {
                for (int j = 0; j < SnakeTrail; j++) { h.Add(Left[i][j]); h.Add(Top[i][j]); }
                h.Add(VelX[i]); h.Add(VelY[i]); h.Add(TargetLeft[i]); h.Add(Locked[i]);
            }
        }
    }

    // ---- entrance length (estimated; see rules) ----
    /// <summary>The first-line-on-screen of each tick of the decelerating scroll loop of konngara_load_and_entrance().</summary>
    static readonly int[] ScrollLines = BuildScrollLines();
    static readonly int ScrollTicks = ScrollLines.Length;
    // lengths of the later parts of the entrance, in ticks: wait, shake below, pan up (13 steps of 32 lines), shake, wait, seal syllables
    public const int WaitTicks = 40, ShakeBelowTicks = 32, PanTicks = 13, ShakeTicks = 32, WaitSealTicks = 30, SealSteps = 16, SealStepTicks = 10;
    public static readonly int EntranceTotal = ScrollTicks + WaitTicks + ShakeBelowTicks + PanTicks + ShakeTicks + WaitSealTicks + SealSteps * SealStepTicks;

    // the decelerating scroll loop: one tick per iteration but the last
    static int[] BuildScrollLines()
    {
        var lines = new List<int>();
        int quarters = 32, inQuarter = 0, line = 0;
        while (true)
        {
            int shown = line;
            line += quarters;
            if (inQuarter == 0 && line > 100) { inQuarter++; quarters--; }
            if (inQuarter == 1 && line > 200) { inQuarter++; quarters--; }
            if (inQuarter == 2 && line > 300) { inQuarter++; quarters--; }
            if (inQuarter == 3 && line > 400) { inQuarter = 0; quarters--; line -= 400; }
            if (quarters <= 0) break;
            lines.Add(shown);
        }
        return lines.ToArray();
    }

    /// <summary>Ticks run so far of the blocking entrance (drawing only).</summary>
    public int EntranceTick => _entranceFrame;

    /// <summary>What the entrance shows at the given tick (1-based): the scroll start line, which pictures fill the page and the seal fade step.</summary>
    public readonly record struct EntranceView(int ScrollLine, int PanLine, int PaletteSteps, int SealStep, bool Revealed);

    /// <summary>
    /// The entrance as the original plays it: a decelerating vertical scroll of the first picture while the palette fades in;
    /// a wait; a shake with the view below the screen; a pan up that copies the boss picture 32 lines at a time; a shake; a wait;
    /// then the four seal syllables appear in white and fade to blue over 16 steps. PanLine = first page row that already holds
    /// the boss picture (400 = none yet). ref: th01/main/boss/b20j.cpp konngara_load_and_entrance
    /// </summary>
    public EntranceView ViewAt(int tick)
    {
        tick = Math.Max(tick, 1);
        int t = tick;
        int steps = (Math.Min(tick, ScrollTicks) + 7) / 8;
        if (t <= ScrollTicks) return new(ScrollLines[t - 1], 400, steps, -1, false);
        t -= ScrollTicks;
        if (t <= WaitTicks) return new(0, 400, steps, -1, false);
        t -= WaitTicks;
        if (t <= ShakeBelowTicks) return new(416 - (t - 1) % 2 * 32, 400, steps, -1, false);
        t -= ShakeBelowTicks;
        if (t <= PanTicks) { int l = 400 - 32 * (t - 1); return new(l, l, 15, -1, false); }
        t -= PanTicks;
        if (t <= ShakeTicks) return new(416 - (t - 1) % 2 * 16, 16, 15, -1, true);
        t -= ShakeTicks;
        if (t <= WaitSealTicks) return new(0, 16, 15, -1, true);
        t -= WaitSealTicks;
        return new(0, 16, 15, Math.Min(SealSteps, t / SealStepTicks), true);
    }

    /// <summary>Defeat: nine "kuji" images, a 64-tick shake and the final scroll fade, plus the white-out (estimate).</summary>
    public const int DefeatSequenceTicks = 9 * 10 + 64 + 270;

    // ---- state ----
    int _entranceFrame;
    int _patternPrev, _patternsDone;
    /// <summary>Read/write access to the pattern counter for tests.</summary>
    public int PatternsDoneForTests { get => _patternsDone; set => _patternsDone = value; }
    int _interval;          // pattern_state union: interval / speed / delta_x all share one slot
    int _speed;
    public int FaceDirection = FdCenter, FaceExpression = FeNeutral;
    public bool FaceDirectionCanChange = true;
    /// <summary>The last slash image shown (-1 none); drawn from the GRX sheet.</summary>
    public int SlashImage = -1;

    // pattern 1 (diamonds)
    public readonly int[] DiamondLeft = new int[4], DiamondTop = new int[4];
    public bool DiamondsActive;
    int _velBlX, _velBlY, _velTlX, _velTlY, _diamondsAtEdgesFrame;
    // pattern 2 (symmetrical from the cup)
    int _cupAngle;
    // pattern 3 (two snakes) and 6 (four snakes)
    public readonly Snakes TwoSnakes = new(2), FourSnakes = new(4);
    // pattern 4 (aimed rows)
    public int RowLeft, RowTop;
    public bool RowActive;
    int _rowDir, _rowSpeed, _rowVelX, _rowVelY;
    // pattern 5 (aimed spray)
    int _sprayOffset, _sprayDelta, _sprayCount;
    // pattern 7 (rain from the edges)
    public int RayEndX, RayEndY;
    public bool RayActive;
    // patterns 8..11 (slashes), 9 (lasers)
    int _spawnerLeft, _spawnerTop;
    bool _rightToLeft;

    public Konngara(GameWorld w) : base(w)
    {
        Hp = HpTotal;
        Left = HitboxLeft;
        Top = HitboxTop;
        for (int i = 0; i < FourSnakes.Count; i++) FourSnakes.Left[i][0] = PixelNoneNeg;
        for (int i = 0; i < TwoSnakes.Count; i++) TwoSnakes.Left[i][0] = PixelNoneNeg;
    }

    public override int Width => HitboxOrbW;
    public override int Height => HitboxOrbH;
    public override bool Defeated => Phase == 7 && Hp <= 0;
    public override uint DefeatBonus => 5 * 10000;      // "Jigoku clear!" granted in the fanciest way
    public override bool ClearsGame => true;
    public override IReadOnlyList<int> FlashColors { get; } = new[] { 3, 4, 5 };
    public override int HpFirstRedWhite => HpFirstRedWhiteValue;
    public override int HpFirstWhite => HpFirstWhiteValue;

    public override int DefeatTicks()
    {
        int startY = World.Orb.Top + Playfield.OrbH / 2;
        return Math.Max(startY + 1, Playfield.Bottom - startY) / DefeatLineDistance + 15 * DefeatWhiteinInterval + DefeatSequenceTicks;
    }

    /// <summary>The seal syllable colour has been fading in for this many steps during phases 2, 4 and 6 (drawing only).</summary>
    public int SiddhamSteps => Phase is 2 or 4 or 6 ? Math.Max(0, (PhaseFrame - 48) / 4) : 0;
    public bool SiddhamFlash => Phase is 2 or 4 or 6 && PhaseFrame >= 50;

    int RankIndex => (int)World.Rank;

    int ByRank(int easy, int normal, int hard, int lunatic) => World.Rank switch
    {
        Rank.Easy => easy, Rank.Normal => normal, Rank.Hard => hard, Rank.Lunatic => lunatic, _ => normal,
    };

    int PlayerLeft => World.Player.Left;
    static int PlayerTop => Player.Top;
    static int PlayerCenterY => Player.Top + Playfield.PlayerH / 2;
    int Irand() => World.Rng.Next();

    /// <summary>Convert a float speed in pixels to subpixels the way to_sp() does (1/16 px).</summary>
    static int Sp(float v) => (int)(v * 16);

    // ---- entrance ----

    public override bool EntranceFrame() => ++_entranceFrame >= EntranceTotal;

    // ---- face ----

    void FaceDirectionSet(int dir)
    {
        if (!FaceDirectionCanChange || FaceDirection == dir) return;
        FaceDirection = dir;
    }

    void FaceExpressionSet(int e) => FaceExpression = e;

    // ---- phase plumbing ----

    void Next(int phase)
    {
        Phase = phase;
        PhaseFrame = 0;
        InvincibilityFrame = 0;
        PatternCur = ChooseNew;
        _patternsDone = 0;
        if ((phase & 1) != 0) FaceExpressionSet(FeNeutral);
    }

    void FrameCommon()
    {
        PhaseFrame++;
        InvincibilityFrame++;
        FaceDirectionSet(PlayerLeft < 198 ? FdLeft : PlayerLeft > 396 ? FdRight : FdCenter);
    }

    void Choose(int frameMin, int countOnFirstTry, int countOnSecondTry)
    {
        if (PhaseFrame <= frameMin) return;
        PhaseFrame = 1;
        PatternCur = Irand() % countOnFirstTry;
        if (PatternCur == _patternPrev) PatternCur = Irand() % countOnSecondTry;
        _patternPrev = PatternCur;
        _patternsDone++;
    }

    // overlap_xy_xywh_le_ge_2(orb_cur_left, orb_cur_top, ...): the CURRENT Orb position (not the previous one).
    bool OrbCurInside()
    {
        var o = World.Orb;
        return o.Left >= HitboxLeft && o.Left <= HitboxLeft + HitboxOrbW - Playfield.OrbW &&
               o.Top >= HitboxTop && o.Top <= HitboxTop + HitboxOrbH - Playfield.OrbH;
    }

    void HitUpdate() =>
        HitCommon(OrbCurInside(), HitScore,
            HitboxLeft, HitboxTop, HitboxShotW - Shots.W, HitboxShotH - Shots.H);

    void AfterPattern()
    {
        if (PhaseFrame == 0)
        {
            FaceExpressionSet(FeNeutral);
            PatternCur = ChooseNew;
        }
    }

    void SiddhamPhase(int nextPhase)
    {
        FrameCommon();
        HitUpdate();
        if (!Invincible && PhaseFrame > 120) Next(nextPhase);
    }

    public override void Update()
    {
        switch (Phase)
        {
            case 0:
                Phase = 1;
                _patternPrev = ChooseNew;
                PatternCur = ChooseNew;
                _patternsDone = 0;
                PhaseFrame = 0;
                InvincibilityFrame = 0;
                Invincible = false;
                World.Rng = new IRand(World.FrameRand);
                return;
            case 1:
                FrameCommon();
                switch (PatternCur)
                {
                    case 0: PatternDiamondCross(); break;
                    case 1: PatternSymmetricalFromCup(); break;
                    case 2: PatternTwoSnakes(); break;
                    case ChooseNew: Choose(120, 3, 3); break;
                }
                AfterPattern();
                HitUpdate();
                if (!Invincible && (_patternsDone >= 7 || Hp < 16) && PatternCur == ChooseNew) Next(2);
                break;
            case 2: SiddhamPhase(3); break;
            case 3:
                FrameCommon();
                switch (PatternCur)
                {
                    case 0: PatternAimedRows(); break;
                    case 1: PatternAimedSpray(); break;
                    case 2: PatternFourSnakes(); break;
                    case 3: PatternRainFromEdges(); break;
                    case ChooseNew: Choose(120, 4, 4); break;
                }
                AfterPattern();
                HitUpdate();
                if (!Invincible && (_patternsDone >= 9 || Hp < 13) && PatternCur == ChooseNew) Next(4);
                break;
            case 4: SiddhamPhase(5); break;
            case 5:
                FrameCommon();
                switch (PatternCur)
                {
                    case 0: PatternSlashRain(); break;
                    case 1: PatternLasersAndThreeSpread(); break;
                    case 2: PatternSlashTriangular(); break;
                    case ChooseNew: Choose(120, 3, 2); break;
                }
                AfterPattern();
                HitUpdate();
                if (!Invincible && (_patternsDone >= 6 || Hp < 10) && PatternCur == ChooseNew) Next(6);
                break;
            case 6: SiddhamPhase(7); break;
            case 7:
                FrameCommon();
                switch (PatternCur)
                {
                    case 0: PatternDiamondCross(); break;
                    case 1: PatternSymmetricalFromCup(); break;
                    case 2: PatternTwoSnakes(); break;
                    case 3: PatternAimedRows(); break;
                    case 4: PatternAimedSpray(); break;
                    case 5: PatternFourSnakes(); break;
                    case 6: PatternRainFromEdges(); break;
                    case 7: PatternSlashRain(); break;
                    case 8: PatternLasersAndThreeSpread(); break;
                    case 9: PatternSlashTriangular(); break;
                    case 10: PatternSemicircleRain(); break;
                    case 11: PatternSlashAimed(); break;
                    case ChooseNew: Choose(5, 12, 2); break;
                }
                AfterPattern();
                HitUpdate();
                break;
        }
    }

    // ---- helpers shared by patterns ----

    void RainFire(int left, int top, int angle, int speed) =>
        World.Pellets.AddSingle(left, top, angle, speed, PelletMotion.Gravity, 1 /* to_sp(0.0625) */);

    // ---- snakes ----

    static int SnakeTargetOffsetLeft(int toLeft) => toLeft + Playfield.PlayerW / 2 - DiamondW / 2;

    void SnakeWobblyAim(Snakes s, int i, int toLeft, int speed)
    {
        int angle = AngleMath.Iatan2(PlayerCenterY - s.Top[i][0], SnakeTargetOffsetLeft(toLeft) - s.Left[i][0]) & 0xFF;
        angle = (angle + (Irand() % 2 == 0 ? 0x28 : -0x28)) & 0xFF;
        s.VelX[i] = AngleMath.PolarX(0, speed, angle);
        s.VelY[i] = AngleMath.PolarY(0, speed, angle);
    }

    void SnakeSpawn(Snakes s, int i, int originX, int originY)
    {
        for (int j = 0; j < SnakeTrail; j++) { s.Left[i][j] = originX; s.Top[i][j] = originY; }
        SnakeWobblyAim(s, i, PlayerLeft, 6);
    }

    void SnakesUpdate(Snakes s)
    {
        var w = World;
        for (int i = 0; i < s.Count; i++)
        {
            if (!s.Alive(i)) continue;
            if (!s.Locked[i]) s.TargetLeft[i] = PlayerLeft;
            SnakeWobblyAim(s, i, s.TargetLeft[i], 7);
            if (s.Top[i][0] > SnakeHomingThreshold) s.Locked[i] = true;
            for (int j = SnakeTrail - 1; j >= 1; j--) { s.Left[i][j] = s.Left[i][j - 1]; s.Top[i][j] = s.Top[i][j - 1]; }
            s.Left[i][0] += s.VelX[i];
            s.Top[i][0] += s.VelY[i];
            if (s.Left[i][0] > PlayerLeft && s.Left[i][0] < PlayerLeft + Playfield.PlayerW - DiamondW &&
                s.Top[i][0] > PlayerTop + (w.PlayerSliding ? 10 : 0) && s.Top[i][0] < PlayerTop + Playfield.PlayerH - DiamondH &&
                !w.PlayerInvincible)
                w.PlayerIsHit = true;
        }
    }

    // ---- pattern 1: diamonds to the edges, then rain ----

    void PatternDiamondCross()
    {
        const int OriginX = Playfield.CenterX - DiamondW / 2, OriginY = Playfield.Top + Playfield.Height / 2 + DiamondH / 2;
        var w = World;
        if (PhaseFrame == 10) FaceExpressionSet(FeNeutral);
        if (PhaseFrame < 100) return;
        if (PhaseFrame == 100)
        {
            var group = ByRank((int)PelletGroup.TwoSpreadNarrowAimed, (int)PelletGroup.ThreeSpreadNarrowAimed,
                (int)PelletGroup.FiveSpreadWideAimed, (int)PelletGroup.FiveSpreadNarrowAimed);
            int a = AngleMath.Iatan2(PlayerCenterY - OriginY, Playfield.Left - OriginX);
            _velBlX = AngleMath.PolarX(0, 7, a); _velBlY = AngleMath.PolarY(0, 7, a);
            a = AngleMath.Iatan2(Playfield.Top - OriginY, Playfield.Left - OriginX);
            _velTlX = AngleMath.PolarX(0, 7, a); _velTlY = AngleMath.PolarY(0, 7, a);
            for (int i = 0; i < 4; i++) { DiamondLeft[i] = OriginX; DiamondTop[i] = OriginY; }
            DiamondsActive = true;
            w.Pellets.AddGroup(Playfield.Left + Playfield.Width / 2 - Pellets.W, Playfield.Top + Playfield.Height * 8 / 21 - Pellets.H / 2,
                (PelletGroup)group, Sp(3.0f));
            _interval = ByRank(18, 16, 14, 12);
            w.Sound.PlaySe(12);
        }
        else if (DiamondLeft[0] > Playfield.Left)
        {
            DiamondLeft[0] += _velBlX; DiamondTop[0] += _velBlY;
            DiamondLeft[1] -= _velBlX; DiamondTop[1] += _velBlY;
            DiamondLeft[2] += _velTlX; DiamondTop[2] += _velTlY;
            DiamondLeft[3] -= _velTlX; DiamondTop[3] += _velTlY;
            if (DiamondLeft[0] <= Playfield.Left)
            {
                DiamondLeft[0] = Playfield.Left;
                DiamondLeft[2] = Playfield.Left;
                DiamondLeft[1] = Playfield.Right - DiamondW;
                DiamondLeft[3] = Playfield.Right - DiamondW;
            }
            return;
        }
        else if (DiamondTop[0] > Playfield.Top)
        {
            DiamondTop[0] -= 3; DiamondTop[1] -= 3;
            DiamondLeft[2] += 6; DiamondLeft[3] -= 6;
            if (DiamondTop[0] <= Playfield.Top) DiamondTop[0] = Playfield.Top;
            return;
        }
        else if (_diamondsAtEdgesFrame < 200)
        {
            _diamondsAtEdgesFrame++;
            if (_diamondsAtEdgesFrame % _interval == 0)
            {
                int speed = Sp(2.5f);
                int fromLeft = Playfield.Left;
                int fromTop = Playfield.Top + Irand() % 200;                          // playfield_rand_y(0, 25/42)
                int toLeft = Playfield.Left + 240 + Irand() % 400;                    // playfield_rand_x(0.375, 1)
                w.Pellets.AddSingle(fromLeft, fromTop, AngleMath.Iatan2(Playfield.Bottom - fromTop, toLeft - fromLeft), speed);

                fromLeft = Playfield.Right - Pellets.W;
                fromTop = Playfield.Top + Irand() % 200;
                toLeft = Playfield.Left + Irand() % 400;                              // playfield_rand_x(0, 0.625)
                w.Pellets.AddSingle(fromLeft, fromTop, AngleMath.Iatan2(Playfield.Bottom - fromTop, toLeft - fromLeft), speed);

                for (int k = 0; k < 2; k++)
                {
                    fromTop = Playfield.Top;
                    fromLeft = Playfield.Left + Irand() % Playfield.Width;
                    toLeft = Playfield.Left + Irand() % Playfield.Width;
                    w.Pellets.AddSingle(fromLeft, fromTop, AngleMath.Iatan2(Playfield.Bottom - fromTop, toLeft - fromLeft), speed);
                }

                fromLeft = Playfield.Left + Irand() % Playfield.Width;
                w.Pellets.AddGroup(fromLeft, Playfield.Top, PelletGroup.OneAimed, speed);
            }
            return;
        }
        else
        {
            PhaseFrame = 0;
            DiamondsActive = false;
        }
        _diamondsAtEdgesFrame = 0;
    }

    // ---- pattern 2: symmetrical from the cup ----

    void CupFire(int angle)
    {
        World.Pellets.AddSingle(CupRight, CupTop, angle, _speed);
        World.Pellets.AddSingle(CupLeft, CupTop, angle, _speed);
    }

    void PatternSymmetricalFromCup()
    {
        if (PhaseFrame == 10) FaceExpressionSet(FeClosed);
        if (PhaseFrame < 100) return;
        if (PhaseFrame == 100)
        {
            _cupAngle = 0x40;
            _speed = ByRank(Sp(5.0f), Sp(5.0f), Sp(6.0f), Sp(7.0f));
        }
        if (PhaseFrame < 140 && PhaseFrame % 8 == 0) { CupFire(0x40); return; }
        if (PhaseFrame < 220 && PhaseFrame % 8 == 0)
        {
            _cupAngle = (_cupAngle + 0x05) & 0xFF;
            CupFire(_cupAngle);
            CupFire(0x80 - _cupAngle);
            return;
        }
        if (PhaseFrame < 300 && PhaseFrame % 8 == 0)
        {
            _cupAngle = (_cupAngle - 0x0C) & 0xFF;
            CupFire(_cupAngle);
            CupFire(0x80 - _cupAngle);
        }
        if (PhaseFrame >= 300) PhaseFrame = 0;
    }

    // ---- pattern 3: two homing snakes and semicircle spreads ----

    void PatternTwoSnakes()
    {
        var w = World;
        if (PhaseFrame == 10) FaceExpressionSet(FeGlare);
        if (PhaseFrame < 100) return;
        if (PhaseFrame == 100)
        {
            TwoSnakes.Locked[0] = false; TwoSnakes.Locked[1] = false;
            SnakeSpawn(TwoSnakes, 0, CupCenterX, CupTop);
            TwoSnakes.Left[1][0] = PixelNoneNeg;
            w.Sound.PlaySe(12);
            return;
        }
        if (PhaseFrame < 500)
        {
            SnakesUpdate(TwoSnakes);
            if (PhaseFrame == 150)
            {
                SnakeSpawn(TwoSnakes, 1, CupCenterX, CupTop);
                w.Sound.PlaySe(12);
            }
            if (PhaseFrame > 240 - RankIndex * 40 && PhaseFrame % 40 == 0)
            {
                const int Spread = 10;
                int angle = Irand() % (0x80 / Spread);
                int m = PhaseFrame % 120;
                int pelletLeft = m == 0 ? SwordCenterX : m == 40 ? EyeCenterX : CupCenterX;
                int pelletTop = m == 0 ? SwordCenterY : m == 40 ? EyeBottom : CupTop;
                for (int i = 0; i < Spread; i++)
                {
                    int speed = Sp(2.5f) + (i % 4 == 0 ? 0 : i % 4 == 1 ? Sp(1.0f) : i % 4 == 2 ? 0 : Sp(2.0f));
                    w.Pellets.AddSingle(pelletLeft, pelletTop, 0x80 - angle, speed);
                    w.Pellets.AddSingle(pelletLeft, pelletTop, angle, speed);
                    angle = (angle + 0x80 / Spread) & 0xFF;
                }
                w.Sound.PlaySe(7);
            }
        }
        else
        {
            PhaseFrame = 0;
        }
    }

    // ---- pattern 4: aimed rows from the top ----

    void PatternAimedRows()
    {
        const int DiamondSpeed = 8, RowMargin = Playfield.Width / 10;
        const int Right = 0, LeftDir = 1, DownStart = 2, DownEnd = DownStart + RowMargin / DiamondSpeed, ToInitial = 99;
        var w = World;
        if (PhaseFrame == 10) FaceExpressionSet(FeNeutral);
        if (PhaseFrame < 100) return;
        if (PhaseFrame == 100)
        {
            int a = AngleMath.Iatan2(Playfield.Top - LeftSleeveTop, Playfield.Left + RowMargin - LeftSleeveLeft);
            _rowVelX = AngleMath.PolarX(0, DiamondSpeed * 2, a);
            _rowVelY = AngleMath.PolarY(0, DiamondSpeed * 2, a);
            RowLeft = LeftSleeveLeft; RowTop = LeftSleeveTop;
            _rowSpeed = Sp(3.0f);
            RowActive = true;
            w.Sound.PlaySe(12);
            _rowDir = ToInitial;
            _interval = ByRank(12, 10, 8, 6);
            return;
        }
        if (_rowDir == ToInitial)
        {
            RowLeft += _rowVelX; RowTop += _rowVelY;
            if (RowTop <= Playfield.Top)
            {
                RowLeft = Playfield.Left + RowMargin;
                RowTop = Playfield.Top;
                _rowDir = Right;
            }
        }
        else if (_rowDir < ToInitial)
        {
            RowLeft += DiamondSpeed + (_rowDir == Right ? 0 : _rowDir == LeftDir ? -DiamondSpeed * 2 : -DiamondSpeed);
            if (_rowDir >= DownStart)
            {
                RowTop += DiamondSpeed;
                _rowDir++;
                if (_rowDir >= DownEnd) _rowDir = RowLeft > Playfield.CenterX ? LeftDir : Right;
            }
            if (RowLeft > Playfield.Right - RowMargin) { _rowDir = DownStart; RowLeft = Playfield.Right - RowMargin; }
            else if (RowLeft < Playfield.Left + RowMargin) { _rowDir = DownStart; RowLeft = Playfield.Left + RowMargin; }
            if (RowTop >= Playfield.Top + RowMargin * 3)
            {
                PhaseFrame = 0;
                RowActive = false;
                return;
            }
            if (_rowDir < DownStart && PhaseFrame % _interval == 0)
            {
                w.Pellets.AddGroup(RowLeft, RowTop, PelletGroup.OneAimed, _rowSpeed);
                _rowSpeed += 2;                                   // += 0.125f
            }
        }
    }

    // ---- pattern 5: aimed spray from the cup ----

    void PatternAimedSpray()
    {
        if (PhaseFrame == 10) FaceExpressionSet(FeClosed);
        if (PhaseFrame < 100) return;
        if (PhaseFrame == 100)
        {
            _sprayOffset = 0x20;
            _sprayDelta = -0x08;
            _sprayCount = 0;
            _interval = ByRank(5, 4, 3, 2);
        }
        if (PhaseFrame % _interval == 0)
        {
            int angle = AngleMath.Iatan2(PlayerTop - (CupTop - 4), PlayerLeft - (CupCenterX - 34)) & 0xFF;
            angle = (angle + _sprayOffset) & 0xFF;
            _sprayOffset = (_sprayOffset + _sprayDelta) & 0xFF;
            _sprayCount++;
            if (_sprayCount > 8)
            {
                _sprayDelta *= -1;
                _sprayCount = 0;
            }
            World.Pellets.AddSingle(CupCenterX, CupTop, angle, Sp(3.0f));
        }
        if (PhaseFrame >= 700) PhaseFrame = 0;
    }

    // ---- pattern 6: four homing snakes ----

    void PatternFourSnakes()
    {
        var w = World;
        if (PhaseFrame == 10) FaceExpressionSet(FeGlare);
        if (PhaseFrame < 100) return;
        if (PhaseFrame == 100)
        {
            for (int i = 0; i < FourSnakes.Count; i++) FourSnakes.Locked[i] = false;
            SnakeSpawn(FourSnakes, 0, CupCenterX, CupTop);
            for (int i = 1; i < FourSnakes.Count; i++) FourSnakes.Left[i][0] = PixelNoneNeg;
            _interval = ByRank(18, 16, 14, 12);                      // the union slot "unused"
            w.Sound.PlaySe(12);
            return;
        }
        if (PhaseFrame < 400)
        {
            SnakesUpdate(FourSnakes);
            if (PhaseFrame == 150) { SnakeSpawn(FourSnakes, 1, CupCenterX, CupTop); w.Sound.PlaySe(12); }
            if (PhaseFrame == 200) { SnakeSpawn(FourSnakes, 2, LeftSleeveLeft, LeftSleeveTop); w.Sound.PlaySe(12); }
            if (PhaseFrame == 250) { SnakeSpawn(FourSnakes, 3, LeftSleeveLeft, LeftSleeveTop); w.Sound.PlaySe(12); }
        }
        else
        {
            PhaseFrame = 0;
        }
    }

    // ---- pattern 7: rain from the edges (sword ray) ----

    void PatternRainFromEdges()
    {
        const int Speed = 8, FramesVertical = 25, FramesHorizontal = Playfield.Width / Speed;
        const int K0 = 100, K1 = K0 + FramesVertical, K2 = K1 + FramesHorizontal, K3 = K2 + FramesVertical,
            K4 = K3 + FramesVertical, K5 = K4 + FramesHorizontal, K6 = K5 + FramesVertical;
        var w = World;
        if (PhaseFrame == 10) FaceExpressionSet(FeAim);
        if (PhaseFrame < K0) return;
        if (PhaseFrame == K0)
        {
            RayEndX = Playfield.Left;
            RayEndY = Playfield.Top + Speed * FramesVertical;
            RayActive = true;
            _interval = ByRank(5, 3, 2, 2);
        }
        void Move(int dx, int dy) { RayEndX += dx; RayEndY += dy; }
        if (PhaseFrame < K1) Move(0, -Speed);
        else if (PhaseFrame == K1) { RayEndX = Playfield.Left; RayEndY = Playfield.Top; Move(Speed, 0); }
        else if (PhaseFrame < K2) Move(Speed, 0);
        else if (PhaseFrame == K2) { RayEndX = Playfield.Right - 1; RayEndY = Playfield.Top; Move(0, Speed); }
        else if (PhaseFrame < K3) Move(0, Speed);
        else if (PhaseFrame == K3) { RayEndX = Playfield.Right - 1; RayEndY = Playfield.Top + Speed * FramesVertical; Move(0, -Speed); }
        else if (PhaseFrame < K4) Move(0, -Speed);
        else if (PhaseFrame == K4) { RayEndX = Playfield.Right - 1; RayEndY = Playfield.Top; Move(-Speed, 0); }
        else if (PhaseFrame < K5) Move(-Speed, 0);
        else if (PhaseFrame == K5) { RayEndX = Playfield.Left; RayEndY = Playfield.Top; Move(0, Speed); }
        else if (PhaseFrame < K6) Move(0, Speed);
        else if (PhaseFrame == K6)
        {
            RayEndY -= Speed;
            RayActive = false;
            PhaseFrame = 0;
        }
        if (PhaseFrame % 10 == 0) w.Sound.PlaySe(6);
        if (PhaseFrame % _interval == 0)
        {
            RainFire(RayEndX, RayEndY, Irand() & 0x7F, Sp(2.0f));
            RainFire(RayEndX, RayEndY, Irand() & 0x7F, Sp(2.0f));
        }
    }

    // ---- patterns 8, 10 (7 in phase 5), 12: slashes ----

    void SlashAnimate()
    {
        var w = World;
        if (PhaseFrame < Slash0) return;
        if (PhaseFrame == Slash0) { SlashImage = 0; w.Sound.PlaySe(8); }
        if (PhaseFrame < Slash1) return;
        if (PhaseFrame == Slash1) SlashImage = 1;
        if (PhaseFrame < Slash2) return;
        if (PhaseFrame == Slash2) SlashImage = 2;
        if (PhaseFrame < Slash3) return;
        if (PhaseFrame == Slash3) SlashImage = 3;
        if (PhaseFrame < Slash4) return;
        if (PhaseFrame == Slash4) SlashImage = 4;
        if (PhaseFrame < Slash5) return;
        if (PhaseFrame == Slash5) SlashImage = 5;
        if (PhaseFrame < Slash6) return;
        if (PhaseFrame == Slash6) SlashImage = 6;
        if (PhaseFrame > 200)
        {
            PhaseFrame = 0;
            FaceDirectionCanChange = true;
            SlashImage = -1;
        }
    }

    void SlashBegin(int expression)
    {
        FaceDirectionSet(FdCenter);
        FaceExpressionSet(expression);
        FaceDirectionCanChange = false;
        _spawnerLeft = SwordCenterX;
        _spawnerTop = SwordCenterY;
    }

    void Step2To4(int steps) { _spawnerLeft += Dist2To4X / (FramesFrom2To4 / steps); _spawnerTop += Dist2To4Y / (FramesFrom2To4 / steps); }
    void Step4To45(int steps) { _spawnerLeft -= Dist5To4X / (FramesFrom5To45 / steps); _spawnerTop -= Dist5To4Y / (FramesFrom5To45 / steps); }
    void Step45To6(int steps) { _spawnerLeft -= Dist5To6X / (FramesFrom45To6 / steps); _spawnerTop -= Dist5To6Y / (FramesFrom45To6 / steps); }

    void SlashRainFire()
    {
        RainFire(_spawnerLeft, _spawnerTop, Irand() % 0x7F, 0);
        RainFire(_spawnerLeft, _spawnerTop, Irand() % 0x7F, 0);
    }

    void PatternSlashRain()
    {
        if (PhaseFrame == 10)
        {
            SlashBegin(FeClosed);
            _interval = ByRank(5, 3, 2, 1);
        }
        SlashAnimate();
        if (PhaseFrame < Slash2) return;
        if (PhaseFrame < Slash4 && PhaseFrame % _interval == 0)
        {
            SlashRainFire();
            Step2To4(_interval);
        }
        if (PhaseFrame == Slash4)
        {
            _spawnerLeft = Slash4CornerX;
            _spawnerTop = Slash4CornerY;
            _interval = ByRank(3, 2, 2, 2);                       // pattern_state.unused: shares the slot with interval
        }
        if (PhaseFrame < Slash4) return;
        if (PhaseFrame < Slash45)
        {
            SlashRainFire();
            Step4To45(1);
        }
        if (PhaseFrame == Slash45)
        {
            _spawnerLeft = Slash5CornerX;
            _spawnerTop = Slash5CornerY;
            _interval = ByRank(3, 2, 1, 1);
        }
        if (PhaseFrame < Slash45) return;
        if (PhaseFrame < Slash6 && PhaseFrame % _interval == 0)
        {
            SlashRainFire();
            Step45To6(_interval);
        }
    }

    void SlashFireTwo() { World.Pellets.AddSingle(_spawnerLeft, _spawnerTop, 0x20, _speed); World.Pellets.AddSingle(_spawnerLeft, _spawnerTop, 0x60, _speed); }
    void SlashFireAimed() => World.Pellets.AddGroup(_spawnerLeft, _spawnerTop, PelletGroup.OneAimed, _speed);

    void SlashLinear(Action fire)
    {
        SlashAnimate();
        if (PhaseFrame < Slash2) return;
        if (PhaseFrame < Slash4 && PhaseFrame % 3 == 0) { fire(); Step2To4(3); }
        if (PhaseFrame == Slash4) { _spawnerLeft = Slash4CornerX; _spawnerTop = Slash4CornerY; }
        if (PhaseFrame < Slash4) return;
        if (PhaseFrame < Slash45) { fire(); Step4To45(1); }
        if (PhaseFrame == Slash45) { _spawnerLeft = Slash5CornerX; _spawnerTop = Slash5CornerY; }
        if (PhaseFrame < Slash45) return;
        if (PhaseFrame < Slash6 && PhaseFrame % 2 == 0) { fire(); Step45To6(2); }
    }

    void PatternSlashTriangular()
    {
        if (PhaseFrame == 10)
        {
            SlashBegin(FeAim);
            _speed = ByRank(Sp(2.0f), Sp(3.0f), Sp(4.0f), Sp(4.5f));
        }
        SlashLinear(SlashFireTwo);
    }

    void PatternSlashAimed()
    {
        if (PhaseFrame == 10)
        {
            SlashBegin(FeAim);
            _speed = ByRank(Sp(4.0f), Sp(5.0f), Sp(5.5f), Sp(6.0f));
        }
        SlashLinear(SlashFireAimed);
    }

    // ---- pattern 9: lasers across the floor and 3-way spreads ----

    void PatternLasersAndThreeSpread()
    {
        const int Interval = 10;
        var w = World;
        if (PhaseFrame == 10) FaceExpressionSet(FeAim);
        if (PhaseFrame < 100) return;
        if (PhaseFrame == 100)
        {
            _rightToLeft = Irand() % 2 != 0;
            _interval = ByRank(Playfield.Width / 5, (int)(Playfield.Width / 6.66), Playfield.Width / 8, Playfield.Width / 10);
        }
        if (PhaseFrame % Interval == 0)
        {
            int targetLeft = _rightToLeft
                ? Playfield.Right - (PhaseFrame - 100) / Interval * _interval
                : Playfield.Left + (PhaseFrame - 100) / Interval * _interval;
            w.Lasers[PhaseFrame / Interval].Spawn(SwordCenterX, SwordCenterY, targetLeft, Playfield.Bottom,
                Sp(8.5f) / 2, 7, 30, 5);
            w.Sound.PlaySe(6);
            if ((!_rightToLeft && targetLeft >= Playfield.Right) || (_rightToLeft && targetLeft <= Playfield.Left))
                PhaseFrame = 0;
            w.Pellets.AddGroup(SwordCenterX, SwordCenterY, PelletGroup.ThreeSpreadWideAimed, Sp(4.5f));
        }
    }

    // ---- pattern 11: semicircle rain from the sleeve ----

    void PatternSemicircleRain()
    {
        const int Spread = 10;
        var w = World;
        if (PhaseFrame == 10) FaceExpressionSet(FeAim);
        if (PhaseFrame < 100) return;
        if (PhaseFrame % 20 == 0)
        {
            for (int i = 0, angle = 0; i < Spread; i++, angle = (angle - 0x80 / Spread) & 0xFF)
                RainFire(LeftSleeveLeft, LeftSleeveTop, angle, Sp(2.0f));
            w.Sound.PlaySe(7);
        }
        else if (PhaseFrame % 20 == 10)
        {
            for (int i = 0, angle = 0; i < Spread; i++, angle = (angle - 0x80 / Spread) & 0xFF)
                w.Pellets.AddSingle(LeftSleeveLeft, LeftSleeveTop, angle, Sp(2.0f),
                    PelletMotion.FallStraightFromTopThenRegular, Sp(3.0f));
            w.Sound.PlaySe(7);
        }
        if (PhaseFrame >= 300) PhaseFrame = 0;
    }

    // ---- hash ----

    public override void Hash(ref StateHasher h)
    {
        h.Add(Phase); h.Add(PhaseFrame); h.Add(InvincibilityFrame); h.Add(Hp); h.Add(Invincible); h.Add(PatternCur);
        h.Add(_entranceFrame); h.Add(_patternPrev); h.Add(_patternsDone); h.Add(_interval); h.Add(_speed);
        h.Add(FaceDirection); h.Add(FaceExpression); h.Add(FaceDirectionCanChange); h.Add(SlashImage);
        for (int i = 0; i < 4; i++) { h.Add(DiamondLeft[i]); h.Add(DiamondTop[i]); }
        h.Add(DiamondsActive); h.Add(_velBlX); h.Add(_velBlY); h.Add(_velTlX); h.Add(_velTlY); h.Add(_diamondsAtEdgesFrame);
        h.Add(_cupAngle);
        TwoSnakes.Hash(ref h); FourSnakes.Hash(ref h);
        h.Add(RowLeft); h.Add(RowTop); h.Add(RowActive); h.Add(_rowDir); h.Add(_rowSpeed); h.Add(_rowVelX); h.Add(_rowVelY);
        h.Add(_sprayOffset); h.Add(_sprayDelta); h.Add(_sprayCount);
        h.Add(RayEndX); h.Add(RayEndY); h.Add(RayActive);
        h.Add(_spawnerLeft); h.Add(_spawnerTop); h.Add(_rightToLeft);
    }
}
