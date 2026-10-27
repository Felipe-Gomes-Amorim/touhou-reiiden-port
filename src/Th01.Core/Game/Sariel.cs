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
            return;
        }
        // Phases 1 and up: later parts.
    }

    public void HitUpdateForTests() => HitUpdate();

    public override void Hash(ref StateHasher h)
    {
        h.Add(Phase); h.Add(PhaseFrame); h.Add(InvincibilityFrame); h.Add(Hp); h.Add(Invincible); h.Add(PatternCur);
        h.Add(_entranceFrame); h.Add(PatternsDone); h.Add(PatternsUntilNext); h.Add(InitialHpRendered);
        h.Add(Left); h.Add(Top); h.Add(HitboxOrbInactive);
    }
}
