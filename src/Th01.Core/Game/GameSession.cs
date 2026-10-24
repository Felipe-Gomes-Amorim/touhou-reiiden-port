using Th01.Core.Audio;
using Th01.Core.Diagnostics;
using Th01.Core.Graphics;
using Th01.Core.Input;
using Th01.Core.Rng;
using Th01.Core.Sim;

namespace Th01.Core.Game;

public enum SessionPhase : byte
{
    /// <summary>Stage start: Reimu can move, nothing else runs until the first shot press.</summary>
    WaitForShot,
    Playing,
    /// <summary>Blocking miss animation of the original, unrolled into ticks.</summary>
    MissAnimation,
    /// <summary>Hit with no lives left. Game over / continue / ranking are backlog item 7.</summary>
    GameOver,
    /// <summary>Stage banner shown before play (blocking in the original). Only with <see cref="GameOptions.Entrance"/>.</summary>
    Entrance,
    /// <summary>All cards removed: the bonus box is up until Shot or OK is pressed and released.</summary>
    StageBonus,
    /// <summary>Boss stage: the blocking entrance animation.</summary>
    BossEntrance,
    /// <summary>Boss beaten: white-out and fade (blocking in the original).</summary>
    BossDefeat,
    /// <summary>Makai / Jigoku choice after the stage 5 boss.</summary>
    RouteSelect,
    /// <summary>Bonus dismissed. Terminal.</summary>
    Cleared,
    /// <summary>Blocking HARRY UP banner (two frame_delay(27)); the sim is frozen meanwhile.</summary>
    HarryUp,
}

/// <summary>
/// Gameplay session for the card stage engine. This increment covers backlog items 1-2 (player, Orb, shots,
/// bomb timeline, lives/extends); cards, obstacles, pellets, items and bosses plug into <see cref="Step"/> where
/// the matching original calls are marked.
/// ref: th01/main_01.cpp (stage and life loops)
/// </summary>
public sealed class GameSession : IGameSession, ITraceSource
{
    // ref: th01/main/extend.hpp
    public const long ScorePerExtend = 400000;

    // Miss animation. ref: th01/main/player/player.cpp player_miss_animate_and_update()
    // The shake phase is 16 iterations of z_vsync_wait + frame_delay(2); modelled as 2 ticks each.
    // The real duration depends on frame_delay's VSync accounting (unverified).
    // TODO(T5): calibrate against the original; then adjust the constants below (pinned by a test).
    const int MissShakeIterations = 16;
    const int MissShakeTicksPerIteration = 2;
    const int MissMoveoutFrames = 10;
    const int MissMoveinFrames = 20;
    const int MissFrames = MissMoveoutFrames + MissMoveinFrames;
    const int MissMoveoutDistanceX = Playfield.Width / 4;
    const int MissMoveoutPerFrameX = MissMoveoutDistanceX / MissMoveoutFrames;
    const int BombsExtraPerLifeLost = 1;

    public const int MissShakeTicks = MissShakeIterations * MissShakeTicksPerIteration;
    /// <summary>Sum over the 30 effect frames of their frame_delay (1 for f&lt;6, else (30-f)/10+1).</summary>
    public const int MissEffectTicks = 50;
    public const int MissTotalTicks = MissShakeTicks + MissEffectTicks;

    /// <summary>The original sets bomb_frame to 200 right after the main loop ends (player hit or stage cleared). ref: th01/main_01.cpp</summary>
    const uint BombFrameAfterLoop = 200;

    readonly GameWorld _w;

    public SessionPhase Phase { get; private set; } = SessionPhase.WaitForShot;
    public long Tick { get; private set; }

    /// <summary>ASSUMPTION (not measured): HARRY UP banner = 2 x frame_delay(27). TODO(T5): calibrate.</summary>
    public const int HarryUpBannerTicks = 54;

    /// <summary>ASSUMPTION (not measured): stage_num_animate = 2 x frame_delay(35). TODO(T5): calibrate. The
    /// scene title animation and the card flip-in of later stages are not modelled.</summary>
    public const int EntranceTicks = 70;

    /// <summary>ASSUMPTION (not measured): fade after the white-out lines, 15 palette levels x 15 frames, and the
    /// 50-frame pause before the route menu accepts input. TODO(T5).</summary>
    public const int BossDefeatFadeTicks = 15 * 15;
    public const int RouteSelectDelayTicks = 50;

    int _defeatWait;
    /// <summary>Drawing only: total ticks of the defeat animation and the row its white lines start from.</summary>
    public int DefeatTotal { get; private set; }
    public int DefeatStartY { get; private set; }
    public int DefeatElapsed => DefeatTotal - Math.Max(_defeatWait, 0);
    public bool RouteMenuActive => Phase == SessionPhase.RouteSelect;
    public int RouteMenuDelayLeft => _routeDelay;
    int _routeDelay;
    bool _routePrevUp, _routePrevDown, _routePrevOk;

    int _entranceWait;
    bool _bonusPrevShot, _bonusPrevOk;
    /// <summary>The numbers shown by the bonus box; null until the stage is cleared.</summary>
    public StageBonus? Bonus { get; private set; }

    /// <summary>Data for the name entry / ranking / continue screens; null until <see cref="Outcome"/> is GameOver.</summary>
    public GameOverResult? GameOverResult { get; private set; }

    int _harryUpWait;
    int _bonusSteps;
    int _missStage;
    int _missTick;
    int _missFrame;
    int _missWait;
    int _effectLeft;
    int _moveoutTargetRight;
    int _moveoutTargetLeft;

    readonly GameRenderer _renderer;

    internal int MissStage => _missStage;
    internal int MissFrame => _missFrame;
    internal int EffectLeft => _effectLeft;

    /// <param name="art">Sprites and background; null or missing frames fall back to flat boxes.</param>
    /// <param name="font">Text for the numeric HUD and the bonus box; without it only the icons are drawn.</param>
    public GameSession(GameOptions options, ISoundSink? sound = null, IGameArt? art = null, IFontSource? font = null)
    {
        _renderer = new GameRenderer(this, art, font);
        _w = new GameWorld(options, sound ?? NullSoundSink.Instance);
        // stage_resets_game_state(): first stage of a scene and boss stages put the player back at the start.
        bool resets = options.CarryOver == null || options.Stage == null || options.Stage.StageId % StageSetup.StagesPerScene == 0
            || options.Stage.IsBoss;
        BeginStage(resetPlayerLeft: resets);
        if (_w.Boss is { HasEntrance: false })
        {
            StartPlaying();                      // the fight begins at once
        }
        else if (_w.Boss != null)
        {
            Phase = SessionPhase.BossEntrance;
        }
        else if (options.Entrance)
        {
            Phase = SessionPhase.Entrance;
            _entranceWait = EntranceTicks;
        }
    }

    public GameWorld World => _w;

    // Stage start, then life-loop start. ref: th01/main_01.cpp ~l.480-560
    void BeginStage(bool resetPlayerLeft)
    {
        var w = _w;
        w.Shots.Reset();
        w.PlayerInvincible = false;
        w.PlayerInvincibilityTime = 0;
        w.CardComboMax = 0;
        w.BombFrame = 0;
        w.Orb.StageStart();
        if (resetPlayerLeft) w.Player.Left = Playfield.PlayerLeftStart;
        w.PlayerDeflecting = false;
        w.Bomb.Damaging = false;
        w.CardComboCur = 0;
        w.Items.Reset();
        if (w.HasStage) w.Timer.InitFor(w.StageId, w.Route);
        BeginLife();
        Phase = SessionPhase.WaitForShot;
    }

    // Life-loop top. ref: th01/main_01.cpp ~l.518-548
    void BeginLife()
    {
        var w = _w;
        w.Player.Reset();
        w.Player.PutDefault();
        w.Stage?.ObstaclesUpdate(true);
        // The original runs the boss once more at every life start (outside the main loop), except the very first
        // time, when that call is the entrance animation.
        if (w.Boss is { Phase: not 0, RunsAtLifeStart: true }) w.Boss.Update();
        w.Input.Reset();
        w.BombDoubletapFrame = InputLatch.BombDoubletapWindow * 3;
        if (w.PlayerInvincibilityTime > 1) w.PlayerInvincible = true;
    }

    void StartPlaying()
    {
        _w.Input.Shot = false;
        _w.Rng = new IRand(_w.FrameRand);
        _w.BombDoubletapFrame = InputLatch.BombDoubletapWindow;
        Phase = SessionPhase.Playing;
    }

    public void Step(GameButtons input)
    {
        var w = _w;
        if (Phase is SessionPhase.GameOver or SessionPhase.Cleared) return;
        Tick++;
        switch (Phase)
        {
            case SessionPhase.BossEntrance:
                if (w.Boss!.EntranceFrame()) StartPlaying();
                return;
            case SessionPhase.BossDefeat:
                if (--_defeatWait <= 0 && !w.Boss!.HasRouteSelect)
                {
                    w.StageCleared = true;
                    BeginBonus();
                }
                else if (_defeatWait <= 0)
                {
                    Phase = SessionPhase.RouteSelect;
                    _routeDelay = RouteSelectDelayTicks;
                    _routePrevUp = _routePrevDown = _routePrevOk = false;
                }
                return;
            case SessionPhase.RouteSelect:
                StepRouteSelect(input);
                return;
            case SessionPhase.Entrance:
                // The first stage of a scene and the boss stages type the title in one character at a time, a click each
                // (entrance.hpp: 6 characters, 8 frames apart). ref: th01/main_01.cpp stage_entrance
                if ((w.StageId % StageSetup.StagesPerScene is 0 or StageSetup.BossStage) && EntranceTicks - _entranceWait is var e and < 48 && e % 8 == 0)
                    w.Sound.PlaySe(14);
                if (--_entranceWait <= 0) Phase = SessionPhase.WaitForShot;
                return;
            case SessionPhase.StageBonus:
                StepBonus(input);
                return;
            case SessionPhase.HarryUp:
                if (--_harryUpWait <= 0) Phase = SessionPhase.Playing;
                return;
            case SessionPhase.MissAnimation:
                StepMiss();
                return;
            case SessionPhase.WaitForShot:
                if (!w.Input.Shot)
                {
                    w.Input.Sense(input, ref w.BombDoubletapFrame);
                    w.Player.Update();
                    w.BombFrame++;
                    return;
                }
                StartPlaying();
                break;
        }

        // Pause: the original blocks inside pause_menu(); only the Pause key is read while it is up.
        if (w.Input.Paused)
        {
            w.Input.SensePauseOnly(input);
            return;
        }

        StepPlaying(input);
    }

    // The main gameplay loop body. ref: th01/main_01.cpp ~l.590-690
    void StepPlaying(GameButtons input)
    {
        var w = _w;
        w.FrameRand++;
        int raiseCycle = 1800 - w.RemLives * 200 - w.RemBombs * 50;
        if (w.FrameRand % (uint)raiseCycle == 0) w.Pellets.SpeedRaise(1);   // pellet_speed_raise(0.025f)
        w.Input.Sense(input, ref w.BombDoubletapFrame);

        if (w.PlayerInvincibilityTime > 1)
        {
            w.PlayerInvincibilityTime--;
            w.PlayerInvincible = true;
        }
        else if (w.PlayerInvincibilityTime == 1)
        {
            w.PlayerInvincible = false;
            w.PlayerInvincibilityTime = 0;
        }

        w.Player.Update();
        w.Items.Update();
        w.Orb.ForceFrame++;
        w.BombFrame++;
        w.BombDoubletapFrame++;
        bool harryUp = w.HasStage && (w.FrameRand & 3) == 0 && w.Timer.TickDown(w);
        w.Sparkles.Update(w.PlayerInvincible);
        if (w.Boss != null)
        {
            if (w.Boss.Defeated)
            {
                BeginBossDefeat();
                return;
            }
            w.Boss.Update();
        }
        w.Lasers.UpdateAll();
        // boss_main(), shootout lasers -- boss port

        // orb_and_pellets_and_stage_unput_update_render__vsync_wait()
        w.Orb.Move();
        // Pellets.unput_update_render() runs here and ends with:
        w.Pellets.Update();
        w.Stage?.CardsHitTest();
        w.Stage?.ObstaclesUpdate(false);
        if (!w.Orb.InPortal) w.Player.OrbPlayerHitTest(OrbRepel.None);
        w.Stage?.CardsUpdate();
        w.Orb.EndOfFrame();

        ScoreExtendUpdate();
        if (w.Pellets.DestroyScoreDelta != 0)
        {
            w.Score += (uint)w.Pellets.DestroyScoreDelta;
            w.Pellets.DestroyScoreDelta = 0;
        }

        // Clearing wins over a simultaneous hit or game over (the original checks stage_cleared first).
        if (w.StageCleared)
        {
            BeginBonus();
            return;
        }
        if (w.PlayerIsHit)
        {
            BeginMiss();
            return;
        }
        if (harryUp)
        {
            Phase = SessionPhase.HarryUp;
            _harryUpWait = HarryUpBannerTicks;
        }
    }

    /// <summary>State for the next stage; null until <see cref="Outcome"/> is Cleared.</summary>
    public SessionCarryOver? CarryOver => Phase != SessionPhase.Cleared ? null : new SessionCarryOver
    {
        Score = _w.Score, RemLives = _w.RemLives, RemBombs = _w.RemBombs, PointValue = _w.PointValue,
        PelletSpeed = Math.Max(0, _w.PelletSpeed), ExtendNext = _w.ExtendNext, CardFlipCycle = _w.CardFlipCycle, OrbVelocityYBits = _w.Orb.Y.VelocityYBits,
        FrameRand = _w.FrameRand, PlayerLeft = _w.Player.Left, RouteChosen = _w.RouteChosen, BonusPerStage = (uint[])_w.BonusPerStage.Clone(),
    };

    public SessionOutcome Outcome => Phase switch
    {
        SessionPhase.GameOver => SessionOutcome.GameOver,
        SessionPhase.Cleared => SessionOutcome.Cleared,
        _ => SessionOutcome.Running,
    };

    // singyoku_defeat_animate_and_select_route(): lines expand from the boss centre, then the palette fades.
    void BeginBossDefeat()
    {
        var w = _w;
        w.Pellets.ResetNonClouds();
        _defeatWait = DefeatTotal = w.Boss!.DefeatTicks();
        DefeatStartY = w.Boss.DefeatFromBossCenter ? w.Boss.CenterY : w.Orb.Top + Playfield.OrbH / 2;
        Phase = SessionPhase.BossDefeat;
    }

    /// <summary>The original polls every 5th frame and waits for a press edge; here every tick, same edges.</summary>
    void StepRouteSelect(GameButtons input)
    {
        if (_routeDelay > 0) { _routeDelay--; return; }
        bool up = (input & GameButtons.Up) != 0, down = (input & GameButtons.Down) != 0;
        bool ok = (input & (GameButtons.Shot | GameButtons.Ok)) != 0;
        if ((up && !_routePrevUp) || (down && !_routePrevDown)) _w.RouteChosen = _w.RouteChosen == 0 ? 1 : 0;
        bool confirm = ok && !_routePrevOk;
        _routePrevUp = up; _routePrevDown = down; _routePrevOk = ok;
        if (!confirm) return;
        _w.Sound.PlaySe(4);
        _w.StageCleared = true;
        BeginBonus();
    }

    // stagebonus_animate(): grants the points, may extend, then waits for Shot/OK.
    void BeginBonus()
    {
        var w = _w;
        w.BombFrame = BombFrameAfterLoop;
        bool boss = w.Boss != null;
        var b = boss
            ? StageBonus.ComputeTotle(w.StageId + 1, w.Timer.Value, w.CardComboMax, w.RemLives, w.RemBombs)
            : StageBonus.Compute(w.StageId + 1, w.Timer.Value, w.CardComboMax, w.RemLives, w.RemBombs);
        Bonus = b;
        w.Score += b.Total;
        if (!boss) w.BonusPerStage[w.StageId % StageSetup.StagesPerScene] = b.Total;
        ScoreExtendUpdate();
        _bonusPrevShot = false;
        _bonusPrevOk = false;
        // The boss bonus (TOTLE) scrambles its digits with a sound per step. ASSUMPTION: one step per tick (the original
        // waits delay(4) per step) and the input is not blocked meanwhile. TODO(T5)
        _bonusSteps = boss ? b.TotleSoundSteps() : 0;
        Phase = SessionPhase.StageBonus;
    }

    // The original resets the input state, sets shot and ok, and loops until either latch drops: that is a
    // release edge of Shot or OK (a key already held on entry counts as pressed).
    void StepBonus(GameButtons input)
    {
        if (_bonusSteps > 0)
        {
            _bonusSteps--;
            _w.Sound.PlaySe(2);
        }
        bool shot = (input & GameButtons.Shot) != 0, ok = (input & GameButtons.Ok) != 0;
        bool done = (_bonusPrevShot && !shot) || (_bonusPrevOk && !ok);
        _bonusPrevShot = shot;
        _bonusPrevOk = ok;
        if (done) Phase = SessionPhase.Cleared;
    }

    // ref: th01/main/extend.cpp
    void ScoreExtendUpdate()
    {
        var w = _w;
        if (w.Score >= (long)w.ExtendNext * ScorePerExtend)
        {
            if (w.RemLives < Playfield.LivesMax)
            {
                w.RemLives++;
                w.Sound.PlaySe(15);
                w.Pellets.SpeedRaise(40);   // pellet_speed_raise(1), clamped to the maximum
            }
            w.ExtendNext++;
        }
    }

    // After the main loop exits through player_is_hit. ref: th01/main_01.cpp ~l.700-720
    void BeginMiss()
    {
        var w = _w;
        w.BombFrame = BombFrameAfterLoop;
        if (w.RemLives <= 0)
        {
            Phase = SessionPhase.GameOver;
            GameOverResult = new GameOverResult(
                w.Score, w.StageId + 1,
                w.StageId < StageSetup.StagesPerScene ? ScoreRoute.Shrine : w.Route == 0 ? ScoreRoute.Makai : ScoreRoute.Jigoku,
                w.Rank, w.PelletSpeed, w.FrameRand);
            return;
        }
        w.Sound.PlaySe(5);
        w.RemLives--;
        w.Player.PutMiss();
        Phase = SessionPhase.MissAnimation;
        _missStage = 0;
        _missTick = 0;
    }

    void StepMiss()
    {
        var w = _w;
        if (_missStage == 0)
        {
            if (++_missTick < MissShakeIterations * MissShakeTicksPerIteration) return;
            w.Shots.Reset();
            w.Pellets.Decay();
            w.Player.Reset();
            _moveoutTargetRight = Math.Min(w.Player.Left + MissMoveoutDistanceX, Playfield.PlayerLeftMax);
            _moveoutTargetLeft = Math.Max(w.Player.Left - MissMoveoutDistanceX, Playfield.PlayerLeftMin);
            _effectLeft = w.Player.Left;
            _missStage = 1;
            _missFrame = 0;
            _missWait = 0;
            return;
        }

        if (_missWait == 0)
        {
            int f = _missFrame;
            w.Player.Left += f < MissMoveoutFrames
                ? +MissMoveoutPerFrameX
                : (Playfield.PlayerLeftStart - _moveoutTargetRight) / MissMoveinFrames;
            _effectLeft += f < MissMoveoutFrames
                ? -MissMoveoutPerFrameX
                : (Playfield.PlayerLeftStart - _moveoutTargetLeft) / MissMoveinFrames;
            if (w.Player.Left >= Playfield.PlayerLeftMax) w.Player.Left = Playfield.PlayerLeftMax;
            if (_effectLeft < Playfield.PlayerLeftMin) _effectLeft = Playfield.PlayerLeftMin;
            _missWait = f < 6 ? 1 : (MissFrames - f) / 10 + 1;
            _missFrame++;
        }
        _missWait--;
        if (_missFrame == MissFrames && _missWait == 0) FinishMiss();
    }

    void FinishMiss()
    {
        var w = _w;
        // hud_lives_put(); bombs refill
        w.RemBombs += BombsExtraPerLifeLost;
        if (w.RemBombs > Playfield.BombsMax) w.RemBombs = Playfield.BombsMax;
        w.Timer.Extend();
        w.Pellets.SpeedLower(0, -2);   // pellet_speed_lower(0.0f, -0.05f)
        w.PlayerIsHit = false;
        w.PlayerInvincibilityTime = Playfield.PlayerMissInvincibilityFrames;
        BeginLife();
        StartPlaying();
    }

    /// <summary>Draws the current state; never changes it (the state hash is unaffected, see tests).</summary>
    public void Render(Framebuffer target) => _renderer.Render(target);

    /// <summary>Oracle columns; see docs/ORACLE-VARS.md. Fixed-point columns are value*256.</summary>
    public IReadOnlyDictionary<string, long> TraceValues()
    {
        var w = _w;
        // The original logger samples each frame right after the main loop has done "frame_rand++" (and, on the
        // first frame, irand_init(frame_rand)) but before anything else of that iteration. A session whose next Step
        // is a main-loop iteration therefore reports those two values as the iteration will leave them. Everything
        // else is the state after the previous iteration, which is what the original shows at that point too.
        bool startsMainIteration = Phase == SessionPhase.Playing || (Phase == SessionPhase.WaitForShot && w.Input.Shot);
        // Before the first main iteration the original's logger shows the seed it pinned (frame_rand), not the state
        // after the game-start draws; a draw made while waiting would still show.
        bool pinnedSeedVisible = (Phase is SessionPhase.WaitForShot or SessionPhase.Entrance or SessionPhase.BossEntrance)
            && (w.Rng.Seed == w.SeedAfterInit || (Phase == SessionPhase.WaitForShot && w.Input.Shot));
        var d = new Dictionary<string, long>
        {
            ["player_x"] = w.Player.Left,
            ["player_y"] = Player.Top,
            ["orb_x"] = w.Orb.Left,
            ["orb_y"] = w.Orb.Top,
            ["orb_vx"] = OrbVxPixels(w.Orb.VelocityX),
            ["orb_vy"] = w.Orb.Y.VelocityYQ8,
            ["score"] = w.Score,
            ["rng_seed"] = pinnedSeedVisible ? w.FrameRand : w.Rng.Seed,
            ["orb_force"] = w.Orb.Y.ForceQ8,
            ["orb_vy_bits"] = w.Orb.Y.VelocityYBits,
            ["orb_force_bits"] = w.Orb.Y.ForceBits,
            ["orb_force_frame"] = w.Orb.ForceFrame,
            ["rem_lives"] = w.RemLives,
            ["rem_bombs"] = w.RemBombs,
            ["frame_rand"] = w.FrameRand + (startsMainIteration ? 1 : 0),
            ["player_invincible"] = w.PlayerInvincible ? 1 : 0,
            ["invincibility_time"] = w.PlayerInvincibilityTime,
            ["bomb_frame"] = w.BombFrame,
            ["cardcombo_cur"] = w.CardComboCur,
            ["cardcombo_max"] = w.CardComboMax,
        };
        if (w.Boss is { } b)
        {
            d["boss_hp"] = b.Hp;
            d["boss_phase"] = b.Phase;
            d["boss_phase_frame"] = b.PhaseFrame;
            d["boss_x"] = b.Left;
            d["boss_y"] = b.Top;
            d["boss_invincibility_frame"] = b.InvincibilityFrame;
            d["boss_pattern"] = b.PatternCur;
        }
        return d;
    }

    static long OrbVxPixels(OrbVelocityX v) => v switch
    {
        OrbVelocityX.Left4 => -4,
        OrbVelocityX.Right4 => 4,
        OrbVelocityX.Left8 => -8,
        OrbVelocityX.Right8 => 8,
        _ => 0,
    };

    public ulong StateHash()
    {
        var h = new StateHasher();
        var w = _w;
        h.Add((int)Phase); h.Add(Tick);
        h.Add(_missStage); h.Add(_missTick); h.Add(_missFrame); h.Add(_missWait);
        h.Add(_effectLeft); h.Add(_moveoutTargetRight); h.Add(_moveoutTargetLeft);
        h.Add(w.Rng.Seed); h.Add(w.FrameRand); h.Add((int)w.Rank);
        h.Add(w.RemLives); h.Add(w.RemBombs); h.Add(w.Score); h.Add(w.ExtendNext);
        h.Add(w.PlayerIsHit); h.Add(w.PlayerInvincible); h.Add(w.PlayerInvincibilityTime);
        h.Add(w.PlayerDeflecting); h.Add(w.PlayerSliding);
        h.Add(w.CardComboCur); h.Add(w.CardComboMax);
        h.Add(w.BombFrame); h.Add(w.BombDoubletapFrame);
        h.Add(w.PelletSpeed);
        h.Add(_harryUpWait); h.Add(_defeatWait); h.Add(_routeDelay); h.Add(_routePrevUp); h.Add(_routePrevDown); h.Add(_routePrevOk); h.Add(w.RouteChosen);
        w.Boss?.Hash(ref h); if (GameOverResult != null) h.Add(GameOverResult.Score); h.Add(_entranceWait); h.Add(_bonusPrevShot); h.Add(_bonusPrevOk); h.Add(_bonusSteps);
        if (Bonus is { } bn) { h.Add(bn.Time); h.Add(bn.CardCombo); h.Add(bn.Resources); h.Add(bn.StageNumber); h.Add(bn.Total); }
        foreach (var v in w.BonusPerStage) h.Add(v); h.Add(w.StageCleared); h.Add(w.CardFlipCycle); h.Add(w.PointValue);
        w.Items.Hash(ref h); w.Sparkles.Hash(ref h); w.Particles.Hash(ref h); w.Missiles.Hash(ref h); w.Lasers.Hash(ref h); w.Pellets.Hash(ref h); w.Timer.Hash(ref h); w.Stage?.Hash(ref h);
        w.Input.Hash(ref h);
        w.Player.Hash(ref h);
        w.Orb.Hash(ref h);
        w.Shots.Hash(ref h);
        w.Bomb.Hash(ref h);
        return h.Value;
    }
}
