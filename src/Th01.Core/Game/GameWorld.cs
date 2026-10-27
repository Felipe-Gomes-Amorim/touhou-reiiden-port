using Th01.Core.Audio;
using Th01.Core.Rng;

namespace Th01.Core.Game;

public sealed record GameOptions
{
    /// <summary>frame_rand at start: the title screen's frame counter, handed over by OP.EXE.</summary>
    public uint FrameRand { get; init; }
    public Rank Rank { get; init; } = Rank.Normal;
    /// <summary>rem_lives: credit_lives_extra + 2 (original default 2 extra lives => 4).</summary>
    public int Lives { get; init; } = 4;
    public int Bombs { get; init; } = 1;
    public uint Score { get; init; }
    /// <summary>resident->hiscore at the start of the stage (shown in the HUD; the table itself is the platform's).</summary>
    public uint HiScore { get; init; }
    /// <summary>resident->point_value (16-bit): value of the next point item; persists across stages.</summary>
    public ushort PointValue { get; init; }
    /// <summary>resident->pellet_speed (1/40 px); the title screen starts a game at -4.</summary>
    public int PelletSpeed { get; init; } = -4;
    /// <summary>The card stage to play. Null = sandbox without cards/obstacles (player and Orb only).</summary>
    public StageSetup? Stage { get; init; }
    /// <summary>Start in the stage banner phase (the platform sets this; tests mostly skip it).</summary>
    public bool Entrance { get; init; }
    /// <summary>State from the previous stage; overrides FrameRand/Lives/Bombs/Score/PointValue when set.</summary>
    public SessionCarryOver? CarryOver { get; init; }
}

/// <summary>
/// The globals of REIIDEN.EXE that the player/orb/shot/bomb code shares. One instance per session.
/// ref: th01/main_01.cpp (globals), th01/main/player/*.cpp
/// </summary>
public sealed class GameWorld
{
    public IRand Rng;
    public readonly ISoundSink Sound;
    public readonly InputLatch Input = new();
    public readonly Orb Orb;
    public readonly Shots Shots;
    public readonly Player Player;
    public readonly Bomb Bomb = new();
    public readonly Items Items;
    public readonly StageTimer Timer = new();
    public readonly Pellets Pellets;
    /// <summary>RNG state right after the game-start draws (card_flip_cycle etc.); lets the trace show the pinned seed.</summary>
    public uint SeedAfterInit;
    public readonly InvincibilitySparkles Sparkles;
    public readonly ParticleField Particles;
    public readonly Missiles Missiles;
    public readonly ShootoutLasers Lasers;
    public readonly StageObjects? Stage;
    public readonly Boss? Boss;
    /// <summary>True when a stage (card or boss) is loaded, i.e. the timer runs.</summary>
    public readonly bool HasStage;

    public Rank Rank;
    public int RemLives;
    public int RemBombs;
    public uint Score;
    public int ExtendNext = 1;
    public uint FrameRand;

    public bool PlayerIsHit;
    public bool PlayerInvincible;
    public int PlayerInvincibilityTime;
    public bool PlayerDeflecting;
    public bool PlayerSliding;

    public int StageId;
    /// <summary>Route picked after the stage 5 boss: 0 Makai, 1 Jigoku.</summary>
    public int RouteChosen;
    public int Route;
    public bool StageCleared;
    public ushort PointValue;
    /// <summary>card_flip_cycle (uint8, wraps): decides which flipped cards drop items.</summary>
    public byte CardFlipCycle;
    /// <summary>resident->pellet_speed in 1/40 px (original start -4 = -0.1).</summary>
    public int PelletSpeed;

    /// <summary>resident->bonus_per_stage: last bonus of each stage of the scene (used by the ending).</summary>
    public readonly uint[] BonusPerStage = new uint[StageSetup.StagesPerScene];

    public int CardComboCur;
    /// <summary>The high score the HUD starts from; it never changes during a session.</summary>
    public uint HiScoreBase;
    /// <summary>hiscore_update_and_render(): the displayed record follows the score as soon as it is beaten.</summary>
    public uint HudHiScore => Math.Max(HiScoreBase, Score);
    public int CardComboMax;

    /// <summary>bomb_frame: also reset to 0 by many player actions (original quirk, kept).</summary>
    public uint BombFrame;
    public int BombDoubletapFrame;


    public GameWorld(GameOptions o, ISoundSink sound)
    {
        Sound = sound;
        Rank = o.Rank;
        RemLives = o.Lives;
        RemBombs = o.Bombs;
        Score = o.Score;
        HiScoreBase = o.HiScore;
        FrameRand = o.FrameRand;
        ExtendNext = (int)(o.Score / GameSession.ScorePerExtend) + 1;
        Rng = new IRand(o.FrameRand);
        Orb = new Orb(this);
        Shots = new Shots(this);
        Player = new Player(this);
        Items = new Items(this);
        Pellets = new Pellets(this);
        Sparkles = new InvincibilitySparkles(this);
        Particles = new ParticleField(this);
        Missiles = new Missiles(this);
        Lasers = new ShootoutLasers(this);
        PelletSpeed = o.PelletSpeed;
        PointValue = o.PointValue;
        if (o.CarryOver is { } c)
        {
            Score = c.Score; RemLives = c.RemLives; RemBombs = c.RemBombs; PointValue = c.PointValue;
            PelletSpeed = c.PelletSpeed; ExtendNext = c.ExtendNext; CardFlipCycle = c.CardFlipCycle; FrameRand = c.FrameRand;
            Rng = new IRand(c.FrameRand);
            Orb.Y.VelocityYBits = c.OrbVelocityYBits;
            Player.Left = c.PlayerLeft;
            c.BonusPerStage?.CopyTo(BonusPerStage, 0);
        }
        else
        {
            // Game start: card_flip_cycle takes the first draw after irand_init(frame_rand).
            CardFlipCycle = (byte)(Rng.Next() % StageObjects.CardFlipCycleInitialMax);
        }
        if (o.Stage != null)
        {
            StageId = o.Stage.StageId;
            Route = o.Stage.Route;
            HasStage = true;
            if (o.Stage.IsBoss)
            {
                if (o.Stage.StageId == StageSetup.BossStage) Boss = new SinGyoku(this);
                else if (o.Stage.StageId == 2 * StageSetup.StagesPerScene - 1)
                    Boss = o.Stage.Route == 1 ? new Mima(this) : new YuugenMagan(this);
                else if (o.Stage.StageId == Kikuri.Id && o.Stage.Route == 1) Boss = new Kikuri(this);
                else if (o.Stage.StageId == Elis.Id && o.Stage.Route == 0) Boss = new Elis(this);
                else if (o.Stage.StageId == Konngara.Id && o.Stage.Route == 1) Boss = new Konngara(this);
            }
            else
            {
                Stage = new StageObjects(this, o.Stage);
                // a_random_unused_card_id: unused by the game, but the draw happens (and divides by 0 on empty stages).
                if (Stage.CardCount > 0) Rng.Next();
            }
        }
        SeedAfterInit = Rng.Seed;
    }

    /// <summary>The player's hitbox test against the Orb. Friction semantics: see <see cref="OrbRepel"/>.</summary>
    public void OrbPlayerHitTest(int repelFriction) => Player.OrbPlayerHitTest(repelFriction);
}

/// <summary>orb_repel_friction_t. ref: th01/main/player/orb.hpp</summary>
public static class OrbRepel
{
    public const int None = 0;
    public const int Max = 26; // -ORB_FORCE_REPEL * 2
    public const int ThreeXUnchanged = 100;
    public const int ThreeX4Left = 101;
    public const int ThreeX4Right = 102;
    public const int ThreeX8Right = 103;
    public const int ThreeX8Left = 104;
}
