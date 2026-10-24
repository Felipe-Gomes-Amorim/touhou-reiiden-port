namespace Th01.Core.Game;

/// <summary>
/// The stage 5 boss, SinGyoku: a 96x96 sphere with 8 HP (hit only by the Orb), two attack phases and a person form
/// (woman / man) she turns into for the second phase. State and timing only; sprites come from BOSS1*.BOS through the
/// renderer. Phase 0 (the 200-frame entrance) is run by the session as its own phase.
/// ref: th01/main/boss/b05.cpp, boss.cpp, entity_a.cpp
/// </summary>
public sealed class SinGyoku : Boss
{
    // ---- constants ----
    public const int W = 96, H = 96;
    public const int BaseCenterX = Playfield.CenterX;
    public const int BaseCenterY = Playfield.Top + Playfield.Height / 21 * 5;
    public const int BaseLeft = BaseCenterX - W / 2;
    public const int BaseTop = BaseCenterY - H / 2;
    public const int HpTotal = 8, HpPhase1End = 6, HpPhase2End = 0;
        public const int HitScore = 3000;
    public const int EntranceFrames = 200;
    public const int SphereCels = 8;
    const int PixelNone = -999;

    // transform keyframes (singyoku_transform_keyframe_t)
    const int TkfStart = 100, TkfToPerson = 105, TkfToPersonRerender = 110, TkfToPersonDone = 115,
        TkfPersonAttack1 = 135, TkfExternalPatternStart = 140, TkfPersonAttack2 = 160, TkfPersonStill = 185,
        TkfExternalPatternDone = 220, TkfToSphere = 240, TkfToSphereDone = 250, TkfDone = 260;

    // person cels (singyoku_person_cel_t)
    const int CWomanStill = 0, CWomanAttack1 = 1, CWomanAttack2 = 2, CManStill = 3, CManAttack = 4, CPersonForm = 3;
    const int FormWoman = 0, FormMan = 1;

    readonly GameWorld _w;

    // ---- state (statics of b05.cpp) ----
    public int SphereImage;         // ent_sphere.image()
    public int PersonImage;         // ent_person.image()

    // pattern statics
    int _direction;                 // halfcircle spray: +1 / -1
    int _angle;                     // unsigned char
    int _pelletCount;
    int _velX, _velY;               // slam velocity (pixels)

    // entrance loop locals
    int _entranceInterval = 18;

    public SinGyoku(GameWorld w) : base(w)
    {
        _w = w;
        Hp = HpTotal;
        Left = BaseLeft;
        Top = BaseTop;
    }

    public override int Width => W;
    public override int Height => H;
    public override bool HasRouteSelect => true;

    public override int DefeatTicks()
    {
        int startY = Top + H / 2;
        return Math.Max(startY + 1, Playfield.Bottom - startY) + GameSession.BossDefeatFadeTicks;
    }

    public override bool Defeated => Phase == 8;
    public override int DefeatLineDistance => 1;
    public override int DefeatWhiteoutInterval => 15;
    public override int DefeatWhiteinInterval => 15;
    public override bool DefeatFromBossCenter => true;
    public override IReadOnlyList<int> FlashColors { get; } = new[] { 13 };

    /// <summary>Is the person form on screen (for the renderer): true between the two flashes of a transformation.</summary>
    public bool PersonVisible => Phase == 2 && PatternCur != 4 && PhaseFrame >= TkfToPersonDone && PhaseFrame < TkfToSphere;

    /// <summary>
    /// The white flash sheet (BOSS1_2.BOS) is shown while turning into the person and back: frames 100..114 and 240..259.
    /// ref: transform_to_person_and_back_to_sphere()
    /// </summary>
    public bool Flashing => Phase == 2 && PatternCur != 4 && (PhaseFrame is >= TkfStart and < TkfToPersonDone || PhaseFrame is >= TkfToSphere and < TkfDone);

    /// <summary>
    /// Cel of the flash sheet: the plain sphere (C_SPHERE) on the first and last steps, the form's cel (C_FLASH_FORM + form)
    /// in between: sphere on 100..104, form on 105..114 and 240..249, sphere again on 250..259.
    /// </summary>
    public int FlashCel => PhaseFrame is >= TkfToPerson and < TkfToPersonDone || PhaseFrame is >= TkfToSphere and < TkfToSphereDone ? Form + 1 : 0;

    /// <summary>
    /// The person cel the screen shows. The man's second attack sets image 5, which the sheet does not have; the original's
    /// draw call then silently does nothing, so his attack cel (4) stays up until the still cel at frame 185.
    /// </summary>
    public int PersonCel => Math.Min(PersonImage, CManAttack);

    public int Form => PatternCur is 1 or 3 ? FormMan : FormWoman;

    // ---- rank tables (select_for_rank / select_subpixel_for_rank) ----
    int ByRank(int easy, int normal, int hard, int lunatic) => _w.Rank switch
    {
        Rank.Easy => easy, Rank.Normal => normal, Rank.Hard => hard, Rank.Lunatic => lunatic, _ => normal,
    };

    // ---- entrance (phase 0) ----

    /// <summary>One frame of the blocking entrance loop; returns true on the last one.</summary>
    public override bool EntranceFrame()
    {
        if (PhaseFrame == 0)
        {
            Left = BaseLeft;
            Top = BaseTop;
            _entranceInterval = 18;
        }
        PhaseFrame++;
        if (PhaseFrame % _entranceInterval == 0)
        {
            SphereRotate(1, 1);
            _entranceInterval -= 2;
            if (_entranceInterval <= 0) _entranceInterval = 1;
        }
        if (PhaseFrame < EntranceFrames) return false;
        Phase = 1;
        PatternCur = 0;
        Invincible = false;
        PhaseFrame = 0;
        HitboxOrbInactive = false;
        InvincibilityFrame = 0;
        return true;
    }

    // ---- sphere helpers ----

    void SphereRotate(int interval, int celDelta)
    {
        if (PhaseFrame % interval != 0) return;
        int next = (sbyte)(SphereImage + celDelta);
        if (next > SphereCels - 1) next = 0;
        else if (next < 0) next = SphereCels - 1;
        SphereImage = next;
    }

    void SphereAccelerateRotation(int celDelta)
    {
        if (PhaseFrame < 50)
        {
            SphereImage = 0;
            return;
        }
        if (PhaseFrame == 50) _w.Sound.PlaySe(8);
        if (PhaseFrame < 100 && PhaseFrame % 4 == 0)
        {
            // Only 60 and 68 (and everything above 82) are divisible by 4; the other keyframes never match.
            if (PhaseFrame is 50 or 60 or 68 or 74 or 78 or 82 || PhaseFrame > 82) SphereRotate(1, celDelta);
        }
    }

    void SphereMoveRotate(int dx, int dy, int interval = 1, int celDelta = 1)
    {
        int newTop = Top + dy;
        int newLeft = Math.Min(Math.Max(Left + dx, 0), Playfield.Right - W);
        Left = newLeft;
        Top = newTop;
        if (PhaseFrame % 2 == 0) SphereRotate(interval, celDelta);
    }

    // ---- main ----

    /// <summary>singyoku_main() for phases 1, 2 and 8 (one tick).</summary>
    public override void Update()
    {
        if (Phase == 1)
        {
            FrameCommon();
            if (PatternCur == 0) PatternHalfcircleSprayDownwards();
            else if (PatternCur == 1) PatternSlam();
            if (PhaseFrame == 0) PatternCur = PatternCur == 1 ? 0 : PatternCur + 1;

            HitUpdate();

            if (Hp <= HpPhase1End && !Invincible)
            {
                // Do not stop the slam midway: it would leave her somewhere below the base position.
                if (PatternCur != 1)
                {
                    Phase = 2;
                    PatternCur = 0;
                    PhaseFrame = 0;
                    InvincibilityFrame = 0;
                }
            }
        }
        else if (Phase == 2)
        {
            FrameCommon();
            switch (PatternCur)
            {
                case 0: PatternChasing(); break;
                case 1: PatternRandomDownwards(); break;
                case 2: PatternCrossing(); break;
                case 3: PatternRandomSling(); break;
                case 4: PatternSlam(); break;
            }
            // Alternate between the slam and a random non-slam pattern.
            if (PhaseFrame == 0) PatternCur = PatternCur == 4 ? _w.Rng.Next() % 4 : 4;

            HitUpdate();
            if (Hp <= HpPhase2End)
            {
                Phase = 8;
                _w.Sound.PlaySe(5);
                PhaseFrame = 0;
            }
        }
    }

    void FrameCommon()
    {
        PhaseFrame++;
        InvincibilityFrame++;
    }

    // ---- hit handling (boss_hit_update_and_render + hittest_orb) ----

    // hitbox_orb_set(8, 8, 88, 88) widened by the Orb's own hitbox: the Orb's previous-frame position must lie in
    // [cur, cur + 64] on both axes.
    bool HitTestOrb() => OrbPrevInside(0, 64, 0, 64);

    void HitUpdate() =>
        HitCommon(HitTestOrb(), HitScore, Left + W / 6, Top + H / 3, W + W / 6 - Shots.W, H - H / 3 - Shots.H);

    // ---- phase 1 patterns ----

    void PatternHalfcircleSprayDownwards()
    {
        const int keyframeFire = 100, keyframeFireDone = 160, fireFrames = keyframeFireDone - keyframeFire;
        if (PhaseFrame == 10) _direction = _w.Rng.Next() % 2 == 1 ? 1 : -1;
        if (PhaseFrame < keyframeFire)
        {
            SphereAccelerateRotation(_direction);
            return;
        }
        if (PhaseFrame == keyframeFire)
        {
            _pelletCount = ByRank(10, 15, 20, 30);
            _angle = _direction == -1 ? 0x00 : 0x80;
        }
        if (PhaseFrame < keyframeFireDone)
        {
            SphereRotate(_direction, 1);
            if (PhaseFrame % (fireFrames / _pelletCount) == 0)
            {
                _w.Pellets.AddSingle(CenterX - Pellets.W / 2, CenterY - Pellets.H / 2, _angle, 50 /* to_sp(3.125) */);
                _angle = (_angle - _direction * 0x80 / _pelletCount) & 0xFF;
            }
        }
        else
        {
            PhaseFrame = 0;
        }
    }

    void PatternSlam()
    {
        var w = _w;
        if (PhaseFrame < 100)
        {
            SphereAccelerateRotation(1);
            return;
        }
        if (PhaseFrame == 100)
        {
            int speed = ByRank(4, 4, 5, 6);   // pixels per frame
            // vector2_between(from = centre - player size / 2, to = player_left/top)
            int fromX = CenterX - Playfield.PlayerW / 2, fromY = CenterY - Playfield.PlayerH / 2;
            int a = AngleMath.Iatan2(Player.Top - fromY, w.Player.Left - fromX);
            _velX = AngleMath.PolarX(0, speed, a);
            _velY = AngleMath.PolarY(0, speed, a);
        }

        if (_velX != -PixelNone)
        {
            SphereMoveRotate(_velX, _velY);
            if (Top > Playfield.Bottom - H)
            {
                // The same fields tell the two sub-phases apart: dive, then rise at 4 px/frame.
                _velX = -PixelNone;
                _velY = -4;
            }
        }
        else if (_velY == -4)
        {
            SphereMoveRotate(0, _velY);
            // "<" and no clamp: she overshoots the base position.
            if (Top < BaseTop) _velY = 0;
        }
        else
        {
            PhaseFrame = 0;
        }

        // A square hitbox covering all 96 pixels; more lenient than a circle.
        if (!w.PlayerInvincible && Left <= w.Player.Left && Left + (W - Playfield.PlayerW) >= w.Player.Left &&
            Top >= Player.Top - H)
            w.PlayerIsHit = true;
    }

    // ---- phase 2 patterns ----

    /// <summary>transform_to_person_and_back_to_sphere(): the 260-frame cycle shared by four patterns.</summary>
    void Transform(int form, Action? onAttack1 = null, Action? onAttack2 = null, Action? onStill = null)
    {
        int personForm = CPersonForm * form;
        if (PhaseFrame < TkfStart)
        {
            SphereAccelerateRotation(1);
            return;
        }
        if (PhaseFrame == TkfPersonAttack1)
        {
            PersonImage = CWomanAttack1 + personForm;
            onAttack1?.Invoke();
        }
        else if (PhaseFrame == TkfPersonAttack2)
        {
            PersonImage = CWomanAttack2 + personForm;   // the man's second attack cel is C_MAN_ATTACK (render only)
            onAttack2?.Invoke();
        }
        else if (PhaseFrame == TkfPersonStill)
        {
            PersonImage = CWomanStill + personForm;
            onStill?.Invoke();
        }
        else if (PhaseFrame == TkfToPersonDone)
        {
            PersonImage = CWomanStill + personForm;
        }
        else if (PhaseFrame == TkfDone)
        {
            SphereImage = 0;
            PhaseFrame = 0;
        }
    }

    void PatternChasing()
    {
        Transform(FormWoman);
        if (PhaseFrame > TkfExternalPatternStart && PhaseFrame % 8 == 0 && PhaseFrame <= TkfExternalPatternDone)
            FireChasingPellets();
    }

    void PatternCrossing()
    {
        Transform(FormWoman);
        if (PhaseFrame > TkfExternalPatternStart && PhaseFrame % 8 == 0) FireCrossingPellets();
    }

    void PatternRandomDownwards() => Transform(FormMan, FireRandomDownwardsPellets, FireRandomDownwardsPellets, FireRandomDownwardsPellets);

    void PatternRandomSling() => Transform(FormMan, FireRandomSlingPellets, FireRandomSlingPellets, FireRandomSlingPellets);

    void FireChasingPellets()
    {
        int chaseSpeed = ByRank(55, 58, 62, 65);    // to_sp(3.4375, 3.625, 3.875, 4.0625)
        _w.Pellets.AddSingle(CenterX - Pellets.W / 2, CenterY - Pellets.H / 2, 0x00 - 0x10, 16, PelletMotion.Chase, chaseSpeed);
        _w.Pellets.AddSingle(CenterX - Pellets.W / 2, CenterY - Pellets.H / 2, 0x80 + 0x10, 16, PelletMotion.Chase, chaseSpeed);
    }

    void FireCrossingPellets()
    {
        int speed = ByRank(60, 70, 75, 80);         // to_sp(3.75, 4.375, 4.6875, 5.0)
        const int hand = 16;                        // WOMAN_HAND_DISTANCE_FROM_EDGE
        _w.Pellets.AddSingle(Left + hand - Pellets.W / 2, CenterY - Pellets.H / 2, 0x40 - 0x10, speed);
        _w.Pellets.AddSingle(Left + W - hand + Pellets.W / 2, CenterY - Pellets.H / 2, 0x40 + 0x10, speed);
    }

    void FireRandomDownwardsPellets()
    {
        int speed = ByRank(48, 54, 60, 66);         // to_sp(3.0, 3.375, 3.75, 4.125)
        for (int i = 0; i < 10; i++)
        {
            int angle = _w.Rng.Next() % 0x80;
            _w.Pellets.AddSingle(CenterX - Pellets.W / 2, CenterY - Pellets.H / 2, angle, speed);
        }
    }

    void FireRandomSlingPellets()
    {
        int speed = ByRank(48, 64, 80, 96);         // to_sp(3, 4, 5, 6)
        for (int i = 0; i < 10; i++)
        {
            int ox = _w.Rng.Next() % W;
            int oy = _w.Rng.Next() % H;
            _w.Pellets.AddSingle(Left + ox, Top + oy, 0, 0, PelletMotion.SlingAimed, speed);
        }
    }

    public override void Hash(ref StateHasher h)
    {
        h.Add(Phase); h.Add(PhaseFrame); h.Add(InvincibilityFrame); h.Add(Hp); h.Add(Invincible);
        h.Add(PatternCur); h.Add(Left); h.Add(Top); h.Add(SphereImage); h.Add(PersonImage); h.Add(HitboxOrbInactive);
        h.Add(_direction); h.Add(_angle); h.Add(_pelletCount); h.Add(_velX); h.Add(_velY); h.Add(_entranceInterval);
    }
}
