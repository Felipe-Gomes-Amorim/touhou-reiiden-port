namespace Th01.Core.Game;

/// <summary>
/// State and rules every boss shares: phase and frame counters, HP, the Orb-hit response with its 40-frame
/// invincibility, and the entrance / defeat hooks the session drives. ref: th01/main/boss/boss.cpp
/// </summary>
public abstract class Boss
{
    public const int HitInvincibilityFrames = 40;

    protected readonly GameWorld World;

    public int Phase;               // boss_phase
    public int PhaseFrame;          // boss_phase_frame
    public int InvincibilityFrame;
    public int Hp;
    public bool Invincible;
    public int PatternCur;
    public int Left, Top;
    public bool HitboxOrbInactive;

    protected Boss(GameWorld w) => World = w;

    public abstract int Width { get; }
    public abstract int Height { get; }
    public int CenterX => Left + Width / 2;
    public int CenterY => Top + Height / 2;

    /// <summary>True once the fight is won; the session then plays the defeat sequence.</summary>
    public abstract bool Defeated { get; }

    /// <summary>Ticks of the blocking defeat animation (white-out and fade); an assumption until measured.</summary>
    public abstract int DefeatTicks();

    // Defeat white-out geometry. ref: th01/main/boss/defeat.cpp defeat_animate(): white lines spread from a start row
    // by [DefeatLineDistance] rows per tick, the palette whitens one step every [DefeatWhiteoutInterval] ticks, then
    // fades back one step every [DefeatWhiteinInterval] ticks. The stage 5 boss starts at its own centre, the others
    // at the Orb's.
    public virtual int DefeatLineDistance => 2;
    public virtual int DefeatWhiteoutInterval => 7;
    public virtual int DefeatWhiteinInterval => 5;
    public virtual bool DefeatFromBossCenter => false;

    /// <summary>First HP point drawn red/white and first drawn white (hud_hp_first_redwhite / hud_hp_first_white).</summary>
    public virtual int HpFirstRedWhite => 2;
    public virtual int HpFirstWhite => 6;

    /// <summary>Palette indices flashed white while the boss is invincible after a hit (boss_hit_update_and_render).</summary>
    public virtual IReadOnlyList<int> FlashColors => Array.Empty<int>();

    /// <summary>
    /// True while the hit flash is lit: the original lights the colours at invincibility frames 2 and 3 of every 4 (and
    /// leaves them lit at 40, restored on 41); the flash on the hit tick itself is undone in the same call.
    /// </summary>
    public bool FlashOn => Invincible && InvincibilityFrame <= HitInvincibilityFrames &&
        (InvincibilityFrame % 4 >= 2 || InvincibilityFrame == HitInvincibilityFrames);

    /// <summary>False when the fight starts at once (no blocking entrance, no wait for the first shot).</summary>
    public virtual bool HasEntrance => true;

    /// <summary>The original runs most bosses once more at each life start; YuugenMagan is not one of them.</summary>
    public virtual bool RunsAtLifeStart => true;

    /// <summary>The stage 5 boss ends with the Makai/Jigoku menu; the later ones go straight to the bonus.</summary>
    public virtual bool HasRouteSelect => false;

    /// <summary>One tick of the (blocking in the original) entrance. Returns true on the last one.</summary>
    public abstract bool EntranceFrame();

    /// <summary>One tick of the boss (phases 1 and up).</summary>
    public abstract void Update();

    public abstract void Hash(ref StateHasher h);

    /// <summary>
    /// boss_hit_update_and_render(): absorbs player shots in the given box, applies an Orb hit (or a last-life bomb)
    /// unless invincible, and ends the invincibility after 40 frames. The caller increments InvincibilityFrame.
    /// </summary>
    protected void HitCommon(bool orbColliding, int hitScore, int shotLeft, int shotTop, int shotW, int shotH)
    {
        var w = World;
        w.Shots.HitTestBoss(shotLeft, shotTop, shotW, shotH);
        bool bombHit = w.RemLives == 0 && w.Bomb.Damaging && w.FrameRand % 50 == 0;
        if ((orbColliding && !Invincible) || bombHit)
        {
            InvincibilityFrame = 0;
            Hp--;
            Invincible = true;
            var o = w.Orb;
            if (o.IsMovingLeft) o.VelocityX = OrbVelocityX.Right4;
            else if (o.IsMovingRight) o.VelocityX = OrbVelocityX.Left4;
            else if (o.VelocityX == OrbVelocityX.Zero)
                o.VelocityX = o.Left < Playfield.CenterX - Playfield.OrbW / 2 ? OrbVelocityX.Right4 : OrbVelocityX.Left4;
            w.Score += (uint)hitScore;
            w.Sound.PlaySe(3);
        }
        if (Invincible && InvincibilityFrame < HitInvincibilityFrames)
        {
            // flashing: drawing only
        }
        else if (InvincibilityFrame > HitInvincibilityFrames)
        {
            // Runs even when not invincible (original bug: a periodic palette reset); harmless for the state.
            InvincibilityFrame = 0;
            Invincible = false;
        }
    }

    /// <summary>The Orb's previous-frame position inside the box [Left+dx0, Left+dx1] x [Top+dy0, Top+dy1].</summary>
    protected bool OrbPrevInside(int dx0, int dx1, int dy0, int dy1)
    {
        if (HitboxOrbInactive) return false;
        var o = World.Orb;
        return Left + dx0 <= o.PrevLeft && Left + dx1 >= o.PrevLeft && Top + dy0 <= o.PrevTop && Top + dy1 >= o.PrevTop;
    }
}
