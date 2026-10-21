namespace Th01.Core.Game;

public enum CardFlag : byte
{
    Alive = 0,
    Flipping = 1,
    Removed = 2,
}

/// <summary>
/// Cards, bumpers, bumper bars and portals of one stage. Turrets occupy obstacle slots (they matter for the
/// portal destination search) but do nothing until the pellet port. ref: th01/main/stage/stageobj.cpp, card.cpp
/// </summary>
public sealed class StageObjects
{
    // ref: th01/main/stage/stageobj.hpp
    public const int ObjW = 32;
    public const int ObjH = 32;
    public const int OrbDistanceX = ObjW * 3 / 8 + 25 / 2;   // 24
    public const int OrbDistanceY = ObjW * 3 / 8 + 25 / 2;   // 24
    public const int CardFramesPerCel = 6;
    public const int CardCels = 5;
    public const int CardCelFlipped = 4;
    public const int CardFlipDone = CardCelFlipped * CardFramesPerCel + 1;            // KEYFRAME_FLIP_DONE
    public const int CardScoreFirst = 2 * CardFramesPerCel + CardFramesPerCel / 2;     // KEYFRAME_SCORE_FIRST
    public const int CardScoreDone = CardFlipDone + CardFramesPerCel / 2;              // KEYFRAME_SCORE_DONE
    public const uint CardScoreCap = 25600;
    public const int CardFlipCycleInitialMax = 60;
    public const int CardFlipCycleMax = 140;
    const int BlockFrames = 8;
    const int PortalOvxCount = 5;

    readonly GameWorld _w;

    // Cards (structure of arrays, row-major order like the DAT scan)
    public readonly int CardCount;
    public readonly int[] CardLeft, CardTop, CardFlipFrame, CardHp;
    public readonly CardFlag[] CardFlagArr;
    /// <summary>Score shown by the popup; 0 once the popup is done. Display state, kept for the renderer.</summary>
    public readonly uint[] CardScore;

    // Obstacles
    public readonly int ObstacleCount;
    public readonly int[] ObsLeft, ObsTop, ObsType, ObsFrame;

    // Statics of obstacles_update_and_render / portal_enter_update_and_render_or_reset
    bool _verticalBarsBlocked;
    int _portalEnteredSlot;
    int _portalDstLeft, _portalDstTop;
    bool _portalsBlocked;
    // turret_flag[]: 0 ready, 1 warm-up, 2 fire, 3..9 cool-down (the flag itself counts up)
    readonly int[] _turretFlag;
    const int TurretCooldown = 3;
    const int TurretDone = TurretCooldown + 7;

    public StageObjects(GameWorld w, StageSetup s)
    {
        _w = w;
        s.Validate();
        int cards = 0, obs = 0;
        for (int r = 0; r < StageSetup.Rows; r++)
            for (int c = 0; c < StageSetup.Cols; c++)
            {
                if (s.Cards[r, c]) cards++;
                if (ObstacleType.IsActual(s.Obstacles[r, c])) obs++;
            }
        CardCount = cards;
        ObstacleCount = obs;
        CardLeft = new int[cards]; CardTop = new int[cards]; CardFlipFrame = new int[cards]; CardHp = new int[cards];
        CardFlagArr = new CardFlag[cards]; CardScore = new uint[cards];
        _turretFlag = new int[obs];
        ObsLeft = new int[obs]; ObsTop = new int[obs]; ObsType = new int[obs]; ObsFrame = new int[obs];

        int slot = 0;
        for (int r = 0; r < StageSetup.Rows; r++)
            for (int c = 0; c < StageSetup.Cols; c++)
                if (s.Cards[r, c])
                {
                    CardLeft[slot] = c * ObjW + Playfield.Left;
                    CardTop[slot] = r * ObjH + Playfield.Top;
                    slot++;
                }
        slot = 0;
        for (int r = 0; r < StageSetup.Rows; r++)
            for (int c = 0; c < StageSetup.Cols; c++)
            {
                int t = s.Obstacles[r, c];
                int left = c * ObjW + Playfield.Left, top = r * ObjH + Playfield.Top;
                if (t is >= ObstacleType.Card2Flip and <= ObstacleType.Card4Flip)
                {
                    // HP is stored minus 1 in the tile's obstacle byte; only applies if a card sits there.
                    for (int i = 0; i < cards; i++)
                        if (CardLeft[i] == left && CardTop[i] == top)
                        {
                            CardHp[i] = t - ObstacleType.Card2Flip + 1;
                            break;
                        }
                }
                else if (ObstacleType.IsActual(t))
                {
                    ObsLeft[slot] = left; ObsTop[slot] = top; ObsType[slot] = t;
                    slot++;
                }
            }
    }

    // ---- Cards ----

    /// <summary>ref: cards_hittest(stage_id)</summary>
    public void CardsHitTest()
    {
        var w = _w;
        for (int i = 0; i < CardCount; i++)
        {
            int dx = Math.Abs(CardLeft[i] - w.Orb.Left);
            int dy = Math.Abs(CardTop[i] - w.Orb.Top);
            bool alive = CardFlagArr[i] == CardFlag.Alive;
            // Evaluation order matters: the bomb branch draws irand() before looking at the card's state.
            bool hit = (dx < OrbDistanceX && dy < OrbDistanceY && alive)
                || (w.Bomb.Damaging && w.BombFrame % (uint)CardCount == (uint)i && w.Rng.Next() % 4 != 0 && alive);
            if (!hit) continue;

            CardFlagArr[i] = CardFlag.Flipping;
            // cardcombo_cur * cardcombo_cur is a 16-bit int multiplication in the original (wraps, sign-extends).
            uint sq = unchecked((uint)(int)(short)(w.CardComboCur * w.CardComboCur));
            uint score = (uint)((w.StageId / 5) * 100 + 100) + sq * (uint)((w.Rank == Rank.Lunatic ? 15 : 0) + 20);
            if (score > CardScoreCap) score = CardScoreCap;
            CardScore[i] = score;
            w.Score += score;
            w.CardComboCur++;
            if (w.CardComboMax < w.CardComboCur) w.CardComboMax = w.CardComboCur;

            if (w.CardFlipCycle++ % 10 == 0)
            {
                if (w.CardFlipCycle >= CardFlipCycleMax)
                {
                    w.CardFlipCycle = 1;
                    if (w.Bomb.Damaging) w.Items.AddPoint(CardLeft[i], CardTop[i]);
                    else w.Items.AddBomb(CardLeft[i], CardTop[i]);
                }
                else
                {
                    w.Items.AddPoint(CardLeft[i], CardTop[i]);
                }
            }
            w.Sound.PlaySe(2);
        }
    }

    /// <summary>ref: cards_update_and_render(). Sets StageCleared + PlayerIsHit when every card is gone.</summary>
    public void CardsUpdate()
    {
        var w = _w;
        // cards_score_render(): clears the popup value once its animation is over (display state only).
        for (int i = 0; i < CardCount; i++)
            if (CardScore[i] != 0 && CardFlipFrame[i] > CardScoreFirst - 1 && CardFlipFrame[i] >= CardScoreDone)
                CardScore[i] = 0;

        int removed = 0;
        for (int i = 0; i < CardCount; i++)
        {
            if (CardFlagArr[i] == CardFlag.Flipping)
            {
                CardFlipFrame[i]++;
                if (CardFlipFrame[i] >= CardCelFlipped * CardFramesPerCel)
                {
                    // Lunatic revenge: one aimed pellet when the flip animation is done.
                    if (w.Rank == Rank.Lunatic && CardFlipFrame[i] == CardFlipDone)
                    {
                        var group = w.StageId + 1 < StageSetup.StagesPerScene * 2
                            ? PelletGroup.OneAimed : PelletGroup.OneRandomNarrowAimed;
                        w.Pellets.AddGroup(CardLeft[i] + ObjW / 2 - Pellets.W / 2, CardTop[i] + ObjH / 2 - Pellets.H / 2,
                            group, Pellets.Sp3_5);
                    }
                    if (CardFlipFrame[i] >= CardCels * CardFramesPerCel)
                    {
                        if (CardHp[i] == 0)
                        {
                            CardFlagArr[i] = CardFlag.Removed;
                            CardFlipFrame[i] = 0;
                        }
                        else
                        {
                            CardFlagArr[i] = CardFlag.Alive;
                            CardFlipFrame[i] = 0;
                            CardHp[i]--;
                        }
                    }
                }
            }
            if (CardFlagArr[i] == CardFlag.Removed) removed++;
        }
        if (removed == CardCount)
        {
            w.StageCleared = true;
            w.PlayerIsHit = true;
        }
    }

    // ---- Obstacles ----

    /// <summary>ref: obstacles_update_and_render(reset). With reset the full pass still runs, then state is cleared.</summary>
    public void ObstaclesUpdate(bool reset)
    {
        var w = _w;
        var o = w.Orb;
        if (reset) _verticalBarsBlocked = false;

        for (int i = 0; i < ObstacleCount; i++)
        {
            int dax = Math.Abs(ObsLeft[i] - o.Left);
            int day = Math.Abs(ObsTop[i] - o.Top);
            int dx = o.Left - ObsLeft[i];
            int dy = o.Top - ObsTop[i];
            int t = ObsType[i];

            if (t == ObstacleType.Bumper)
            {
                if (dax < OrbDistanceX && day < OrbDistanceY && ObsFrame[i] == 0)
                {
                    ObsFrame[i]++;
                    // Immediate teleport above/below the bumper (original behaviour; allows infinite loops).
                    o.Top = dy <= 0 ? ObsTop[i] - OrbDistanceY : ObsTop[i] + OrbDistanceY;
                    // 8 px/tick velocities are left unchanged.
                    if (o.VelocityX == OrbVelocityX.Right4 && dx < 0) o.VelocityX = OrbVelocityX.Left4;
                    else if (o.VelocityX == OrbVelocityX.Left4 && dx > 0) o.VelocityX = OrbVelocityX.Right4;
                    o.ForceNew(0, OrbForce.BounceFromSurface); // immediate value 1.5 is ignored for this kind
                }
                if (ObsFrame[i] != 0)
                {
                    ObsFrame[i]++;
                    if (ObsFrame[i] >= BlockFrames) ObsFrame[i] = 0;
                }
                if (reset) ObsFrame[i] = 0;
                continue;
            }
            if (t is >= ObstacleType.TurretFirst and <= ObstacleType.TurretLast)
            {
                int interval = t >= ObstacleType.TurretQuickFirst ? 100 : 200;
                if (w.Rank != Rank.Easy) ObsFrame[i]++;
                if (ObsFrame[i] >= interval) TurretFire(i);
                if (reset)
                {
                    ObsFrame[i] = 0;
                    Array.Fill(_turretFlag, 0);
                }
                continue;
            }
            if (t == ObstacleType.Portal)
            {
                if ((dax < OrbDistanceX && day < OrbDistanceY) || ObsFrame[i] != 0)
                    PortalEnterUpdate(i);
                if (reset)
                {
                    ObsFrame[i] = 0;
                    PortalReset();
                }
                continue;
            }

            // Bumper bars: each collision handled independently per bar.
            switch (t)
            {
                case ObstacleType.BarBottom:
                    if (dax < ObjW && o.Top - ObsTop[i] <= ObjH && o.Top - ObsTop[i] > 0) HorizontalBarHit(i);
                    break;
                case ObstacleType.BarTop:
                    if (dax < ObjW && o.Top - ObsTop[i] < 0 && o.Top - ObsTop[i] >= -ObjH) HorizontalBarHit(i);
                    break;
                case ObstacleType.BarLeft:
                    if (day < ObjH && ObsLeft[i] - o.Left <= ObjW && ObsLeft[i] - o.Left >= 0) VerticalBarHit(i);
                    break;
                case ObstacleType.BarRight:
                    if (day < ObjH && ObsLeft[i] - o.Left <= 0 && ObsLeft[i] - o.Left >= -ObjW) VerticalBarHit(i);
                    break;
                default:
                    continue;
            }
            // Common to all bars
            if (ObsFrame[i] != 0)
            {
                ObsFrame[i]++;
                if (ObsFrame[i] >= BlockFrames)
                {
                    ObsFrame[i] = 0;
                    _verticalBarsBlocked = false;
                }
            }
            if (reset) ObsFrame[i] = 0;
        }
    }

    public int TurretFlag(int slot) => _turretFlag[slot];

    /// <summary>turret_fire_update_and_render_or_reset() without the drawing.</summary>
    void TurretFire(int slot)
    {
        int f = _turretFlag[slot];
        if (f == 0)
        {
            _turretFlag[slot] = 1;
        }
        else if (f == 1)
        {
            if (ObsFrame[slot] % 10 == 9) _turretFlag[slot] = 2;
        }
        else if (f == 2)
        {
            // Types 2..7 (slow) and 8..13 (quick) share the group order.
            int k = (ObsType[slot] - ObstacleType.TurretFirst) % 6;
            var group = k switch
            {
                0 => PelletGroup.OneAimed,
                1 => PelletGroup.OneRandomNarrowAimed,
                2 => PelletGroup.TwoSpreadWideAimed,
                3 => PelletGroup.ThreeSpreadWideAimed,
                4 => PelletGroup.FourSpreadWideAimed,
                _ => PelletGroup.FiveSpreadWideAimed,
            };
            _w.Pellets.AddGroup(ObsLeft[slot] + ObjW / 2 - Pellets.W / 2, ObsTop[slot] + ObjH / 2 - Pellets.H / 2,
                group, Pellets.Sp3_5);
            _turretFlag[slot] = TurretCooldown;
        }
        else
        {
            _turretFlag[slot]++;
            if (_turretFlag[slot] >= TurretDone)
            {
                _turretFlag[slot] = 0;
                ObsFrame[slot] = 0;
            }
        }
    }

    void HorizontalBarHit(int i)
    {
        if (ObsFrame[i] == 0)
        {
            ObsFrame[i]++;
            // Naive: assumes a perpendicular approach (original). Idempotent within a frame.
            _w.Orb.ReflectY();
        }
    }

    void VerticalBarHit(int i)
    {
        if (ObsFrame[i] == 0 && !_verticalBarsBlocked)
        {
            ObsFrame[i]++;
            _verticalBarsBlocked = true;
            // Only OVX_0 is treated as "parallel" (original).
            if (_w.Orb.VelocityX == OrbVelocityX.Zero) _w.Orb.ReflectY();
            else _w.Orb.ReflectX();
        }
    }

    void PortalReset()
    {
        _portalEnteredSlot = 0;
        _portalsBlocked = false;
        if (_w.Orb.InPortal) _w.Orb.InPortal = false;
    }

    void PortalEnterUpdate(int slot)
    {
        var w = _w;
        var o = w.Orb;
        if (_portalsBlocked && _portalEnteredSlot != slot) return;
        if (ObsFrame[slot] != 0) ObsFrame[slot]++;
        int f = ObsFrame[slot];
        if (f == 0)
        {
            o.InPortal = true;
            ObsFrame[slot] = 1;
            _portalEnteredSlot = slot;
            _portalsBlocked = true;
        }
        else if (f == 20)
        {
            // Destination: repeated random pick over all obstacles, giving up after 16 passes.
            int dst = 0, loops = 0;
            while (true)
            {
                if (ObsType[dst] == ObstacleType.Portal && w.Rng.Next() % 4 == 0 && dst != slot) break;
                dst++;
                if (loops > 15) { dst = slot; break; }
                if (dst >= ObstacleCount) { dst = 0; loops++; }
            }
            _portalDstLeft = ObsLeft[dst];
            _portalDstTop = ObsTop[dst];
        }
        else if (f == 40)
        {
            o.VelocityX = (OrbVelocityX)(w.Rng.Next() % PortalOvxCount);
            o.ForceNew(w.Rng.Next() % 19 - 9, OrbForce.Immediate);
            o.Left = _portalDstLeft;
            o.Top = _portalDstTop;
            o.InPortal = false;
        }
        else if (f == 60)
        {
            ObsFrame[slot] = 0;
            _portalsBlocked = false;
        }
    }

    public void Hash(ref StateHasher h)
    {
        for (int i = 0; i < CardCount; i++)
        {
            h.Add((int)CardFlagArr[i]); h.Add(CardFlipFrame[i]); h.Add(CardHp[i]); h.Add(CardScore[i]);
        }
        for (int i = 0; i < ObstacleCount; i++) h.Add(ObsFrame[i]);
        for (int i = 0; i < ObstacleCount; i++) h.Add(_turretFlag[i]);
        h.Add(_verticalBarsBlocked); h.Add(_portalEnteredSlot);
        h.Add(_portalDstLeft); h.Add(_portalDstTop); h.Add(_portalsBlocked);
    }
}
