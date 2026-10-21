namespace Th01.Core.Game;

/// <summary>pellet_group_t. ref: th01/main/bullet/pellet.hpp</summary>
public enum PelletGroup : byte
{
    None = 0,
    One = 1,
    OneAimed = 12,
    TwoSpreadWide, TwoSpreadNarrow,
    ThreeSpreadWide, ThreeSpreadNarrow,
    FourSpreadWide, FourSpreadNarrow,
    FiveSpreadWide, FiveSpreadNarrow,
    TwoSpreadWideAimed, TwoSpreadNarrowAimed,           // AimedSpreads = 21
    ThreeSpreadWideAimed, ThreeSpreadNarrowAimed,
    FourSpreadWideAimed, FourSpreadNarrowAimed,
    FiveSpreadWideAimed, FiveSpreadNarrowAimed,
    OneRandomNarrowAimed = 29,
    OneRandomWide = 30,
}

/// <summary>pellet_motion_t (only the types the ported bosses need are implemented).</summary>
public enum PelletMotion : byte
{
    Regular = 0,
    Gravity = 1,
    SlingAimed = 2,
    BounceFromTopThenGravity = 3,
    FallStraightFromTopThenRegular = 4,
    Spin = 5,
    Chase = 6,
}

/// <summary>
/// The pellet pool (100 slots), their groups, hit tests against shots, the Orb, the deflecting player and the
/// player. Coordinates are subpixels (1/16 px). Group-fired pellets plus add_single with the regular, gravity,
/// sling and chase motions; the others arrive with the bosses that use them.
/// ref: th01/main/bullet/pellet.cpp, pellet_s.cpp
/// </summary>
public sealed class Pellets
{
    public const int Count = 100;
    public const int W = 8;
    public const int H = 8;
    const int VelocityMax = 8 * 16;
    const int DestroyScore = 10;
    const int DecayFrames = 20;
    const int SpeedMultiplier = 40;
    public const int SpeedLowerMin = -15;   // to_pellet_speed(-0.375)
    public const int SpeedRaiseMax = 20;    // to_pellet_speed(0.5)

    // to_sp() of the constants the callers pass
    public const int Sp3_0 = 48;
    public const int Sp3_5 = 56;

    readonly GameWorld _w;

    public readonly bool[] Moving = new bool[Count];
    public readonly int[] CurLeft = new int[Count], CurTop = new int[Count];
    public readonly int[] VelX = new int[Count], VelY = new int[Count];
    public readonly PelletGroup[] FromGroup = new PelletGroup[Count];
    public readonly int[] DecayFrame = new int[Count];
    public readonly int[] CloudFrame = new int[Count];
    public readonly int[] CloudLeft = new int[Count], CloudTop = new int[Count];
    public readonly PelletMotion[] Motion = new PelletMotion[Count];
    /// <summary>"speed" field: radius for the sling, chase speed, gravity step (subpixels).</summary>
    public readonly int[] Speed = new int[Count];
    /// <summary>Not reset when a pellet dies: a new pellet in the slot inherits the old angle (original quirk).</summary>
    public readonly int[] Angle = new int[Count];
    /// <summary>0 none, 1 clockwise, 2 counter-clockwise; never reset (original quirk).</summary>
    public readonly int[] SlingDirection = new int[Count];

    int _alive;
    public bool Interlace;
    public bool InterlaceField;
    public bool SpawnWithCloud;
    /// <summary>pellet_destroy_score_delta: points for pellets destroyed this tick, added after the extend check.</summary>
    public int DestroyScoreDelta;

    public Pellets(GameWorld w) => _w = w;

    public int Alive => _alive;

    static int ToPixel(int sp) => sp >> 4;

    int BaseSpeedForRank() => _w.Rank switch
    {
        Rank.Easy => 0,
        Rank.Normal => 6,    // to_sp(0.375)
        Rank.Hard => 12,     // to_sp(0.75)
        _ => 18,             // to_sp(1.125)
    };

    int SpeedSet(int speed)
    {
        speed += BaseSpeedForRank();
        speed += _w.PelletSpeed * speed / SpeedMultiplier;
        if (speed < 16) speed = 16;
        return speed;
    }

    // ---- resident pellet speed ----

    /// <summary>pellet_speed_raise(delta): delta is in 1/40 px.</summary>
    public void SpeedRaise(int delta)
    {
        _w.PelletSpeed += delta;
        if (_w.PelletSpeed > SpeedRaiseMax) _w.PelletSpeed = SpeedRaiseMax;
    }

    /// <summary>pellet_speed_lower(max, negativeDelta).</summary>
    public void SpeedLower(int max, int negativeDelta)
    {
        if (_w.PelletSpeed > max) _w.PelletSpeed = max;
        else _w.PelletSpeed += negativeDelta;
        if (_w.PelletSpeed < SpeedLowerMin) _w.PelletSpeed = SpeedLowerMin;
    }

    // ---- spawning ----

    int PlayerCenterX => _w.Player.Left + Playfield.PlayerW / 2;
    static int PlayerCenterY => Player.Top + Playfield.PlayerH / 2;

    static void Vector2(out int x, out int y, int length, int angle)
    {
        x = AngleMath.PolarX(0, length, angle);
        y = AngleMath.PolarY(0, length, angle);
    }

    void Vector2Between(int x1, int y1, int x2, int y2, out int rx, out int ry, int length)
    {
        int a = AngleMath.Iatan2(y2 - y1, x2 - x1);
        Vector2(out rx, out ry, length, a);
    }

    void ToPlayerFrom(int x, int y, out int rx, out int ry, int length, int plusAngle)
    {
        int a = (AngleMath.Iatan2(PlayerCenterY - y, PlayerCenterX - W / 2 - x) + plusAngle) & 0xFF;
        Vector2(out rx, out ry, length, a);
    }

    /// <summary>Velocity of pellet #i of a group; returns true when it was the last one of the group.</summary>
    bool GroupVelocitySet(out int vx, out int vy, PelletGroup group, int speed, ref int i, int left, int top)
    {
        bool done = false;
        int angle = 0;
        int spread = 0;
        bool aim;
        vx = vy = 0;
        int g = (int)group;
        bool aimedSpread = g >= (int)PelletGroup.TwoSpreadWideAimed;

        switch (group)
        {
            case PelletGroup.One:
                vy = speed; vx = 0; done = true;
                i++;
                return done;
            case PelletGroup.OneAimed:
                Vector2Between(left, top, PlayerCenterX - W, PlayerCenterY, out vx, out vy, speed);
                done = true;
                i++;
                return done;
            case PelletGroup.TwoSpreadWide:
            case PelletGroup.TwoSpreadWideAimed:
            case PelletGroup.TwoSpreadNarrow:
            case PelletGroup.TwoSpreadNarrowAimed:
                if (group is PelletGroup.TwoSpreadWide or PelletGroup.TwoSpreadWideAimed) spread = 0x08;
                if (i == 0) angle = 0x04 + spread;
                else if (i == 1) { angle = -0x04 - spread; done = true; }
                aim = aimedSpread;
                break;
            case PelletGroup.ThreeSpreadWide:
            case PelletGroup.ThreeSpreadWideAimed:
            case PelletGroup.ThreeSpreadNarrow:
            case PelletGroup.ThreeSpreadNarrowAimed:
                if (group is PelletGroup.ThreeSpreadWide or PelletGroup.ThreeSpreadWideAimed) spread = 0x05;
                if (i == 0) angle = 0x00;
                else if (i == 1) angle = 0x04 + spread;
                else if (i == 2) { angle = -0x04 - spread; done = true; }
                aim = aimedSpread;
                break;
            case PelletGroup.FourSpreadWide:
            case PelletGroup.FourSpreadWideAimed:
            case PelletGroup.FourSpreadNarrow:
            case PelletGroup.FourSpreadNarrowAimed:
                if (group is PelletGroup.FourSpreadWide or PelletGroup.FourSpreadWideAimed) spread = 0x04;
                if (i == 0) angle = 0x04 + spread;
                else if (i == 1) angle = -0x04 - spread;
                else if (i == 2) angle = 0x0C + spread * 3;
                else if (i == 3) { angle = -0x0C - spread * 3; done = true; }
                aim = aimedSpread;
                break;
            case PelletGroup.FiveSpreadWide:
            case PelletGroup.FiveSpreadWideAimed:
            case PelletGroup.FiveSpreadNarrow:
            case PelletGroup.FiveSpreadNarrowAimed:
                if (group is PelletGroup.FiveSpreadWide or PelletGroup.FiveSpreadWideAimed) spread = 0x04;
                if (i == 0) angle = 0x00;
                else if (i == 1) angle = 0x04 + spread;
                else if (i == 2) angle = -0x04 - spread;
                else if (i == 3) angle = 0x08 + spread * 2;
                else if (i == 4) { angle = -0x08 - spread * 2; done = true; }
                aim = aimedSpread;
                break;
            case PelletGroup.OneRandomNarrowAimed:
                angle = (_w.Rng.Next() & 0x0F) - 0x07;
                done = true;
                aim = true;
                break;
            case PelletGroup.OneRandomWide:
                angle = (_w.Rng.Next() & 0x3F) + 0x20;
                done = true;
                aim = false;
                break;
            default:
                // Invalid group: the original leaves the velocity uninitialized and keeps filling the pool.
                i++;
                return false;
        }

        angle &= 0xFF;
        if (aim)
        {
            ToPlayerFrom(left, top, out vx, out vy, speed, angle);
        }
        else
        {
            // Static groups: add 90 degrees so that angle 0 points downwards (random-wide already includes it).
            if (group != PelletGroup.OneRandomWide) angle = (angle + 0x40) & 0xFF;
            Vector2(out vx, out vy, speed, angle);
        }
        i++;
        return done;
    }

    /// <summary>add_group(): spawns a group at (left, top); speedBase is in subpixels (to_sp).</summary>
    public void AddGroup(int left, int top, PelletGroup group, int speedBase)
    {
        if (_alive >= Count) return;
        if (left >= Playfield.Right || top < Playfield.Top - H || left < Playfield.Left - W || top > Playfield.Bottom) return;
        int speed = SpeedSet(speedBase);
        int groupI = 0;
        for (int i = 0; i < Count; i++)
        {
            if (Moving[i] || CloudFrame[i] != 0) continue;
            DecayFrame[i] = 0;
            CurLeft[i] = left << 4;
            CurTop[i] = top << 4;
            CloudLeft[i] = left;
            CloudTop[i] = top;
            if (SpawnWithCloud) CloudFrame[i] = 1;
            else Moving[i] = true;
            FromGroup[i] = group;
            _alive++;
            bool done = GroupVelocitySet(out int vx, out int vy, group, speed, ref groupI, left, top);
            VelX[i] = vx;
            VelY[i] = vy;
            if (done) return;
        }
    }

    /// <summary>
    /// add_single(): one pellet with a motion type. <paramref name="speedBase"/> is tuned like a group's;
    /// <paramref name="speedForMotion"/> never is. Spin motions (bosses that use them) are not ported.
    /// </summary>
    public void AddSingle(int left, int top, int angle, int speedBase, PelletMotion motion = PelletMotion.Regular, int speedForMotion = 0)
    {
        if (_alive >= Count) return;
        speedBase = SpeedSet(speedBase);
        for (int i = 0; i < Count; i++)
        {
            if (Moving[i] || CloudFrame[i] != 0) continue;
            DecayFrame[i] = 0;
            CurLeft[i] = left << 4;
            CurTop[i] = top << 4;
            CloudLeft[i] = left;
            CloudTop[i] = top;
            if (SpawnWithCloud) CloudFrame[i] = 1;
            else Moving[i] = true;
            FromGroup[i] = PelletGroup.None;
            Motion[i] = motion;
            _alive++;
            Vector2(out VelX[i], out VelY[i], speedBase, angle & 0xFF);
            Speed[i] = speedForMotion;
            return;
        }
    }

    const int SlingDeltaAngle = 0x08;
    const int ChaseTopMax = (Playfield.Bottom - 80) * 16;

    /// <summary>motion_type_apply_for_cur() for the implemented motions.</summary>
    void ApplyMotion(int i)
    {
        switch (Motion[i])
        {
            case PelletMotion.Gravity:
                VelY[i] += Speed[i];
                break;
            case PelletMotion.SlingAimed:
                if (SlingDirection[i] == 0) SlingDirection[i] = (_w.Rng.Next() & 1) + 1;
                if (SlingDirection[i] == 1) Vector2(out VelX[i], out VelY[i], Speed[i], Angle[i] & 0xFF);
                else Vector2(out VelY[i], out VelX[i], Speed[i], Angle[i] & 0xFF);
                Angle[i] += SlingDeltaAngle;
                if (Angle[i] > 0x100)
                {
                    Vector2Between(ToPixel(CurLeft[i]), ToPixel(CurTop[i]), PlayerCenterX - W, PlayerCenterY,
                        out VelX[i], out VelY[i], Speed[i]);
                    FromGroup[i] = PelletGroup.OneAimed;
                    Angle[i] = 0;
                }
                break;
            case PelletMotion.Chase:
                Vector2Between(ToPixel(CurLeft[i]), ToPixel(CurTop[i]), PlayerCenterX - W, PlayerCenterY,
                    out int tx, out int ty, Speed[i]);
                if (CurTop[i] < ChaseTopMax)
                {
                    VelX[i] += VelX[i] > tx ? -1 : VelX[i] < tx ? 1 : 0;
                    VelY[i] += VelY[i] > ty ? -1 : VelY[i] < ty ? 1 : 0;
                }
                break;
        }
    }

    // ---- per-tick ----

    /// <summary>Pellets.unput_update_render(): the state half. Ends with the player shots' update, like the original.</summary>
    public void Update()
    {
        var w = _w;
        InterlaceField = !InterlaceField;
        CloudsUpdate();

        for (int i = 0; i < Count; i++)
        {
            if (!Moving[i]) continue;
            if (FromGroup[i] == PelletGroup.None && Motion[i] != PelletMotion.Regular) ApplyMotion(i);
            if (VelY[i] > VelocityMax) VelY[i] = VelocityMax;
            if (VelX[i] > VelocityMax) VelX[i] = VelocityMax;
            CurTop[i] += VelY[i];
            CurLeft[i] += VelX[i];

            if (DecayFrame[i] == 0 && w.Shots.HitTestPellet(ToPixel(CurLeft[i]), ToPixel(CurTop[i])))
            {
                DecayFrame[i] = 0;
                Moving[i] = false;
                _alive--;
            }

            // Clipping; also runs for a pellet a shot just removed (the counter is decremented twice then).
            if (CurTop[i] >= Playfield.Bottom * 16 || CurTop[i] < Playfield.Top * 16 ||
                CurLeft[i] >= (Playfield.Right - W) * 16 || CurLeft[i] <= Playfield.Left * 16)
            {
                Moving[i] = false;
                _alive--;
                DecayFrame[i] = 0;
            }
        }

        w.Shots.Update();

        for (int i = 0; i < Count; i++)
        {
            if (!Moving[i]) continue;
            if (!Interlace || (InterlaceField ? 1 : 0) == i % 2)
                HitTestsForPellet(i, ToPixel(CurLeft[i]), ToPixel(CurTop[i]));
            if (DecayFrame[i] != 0)
            {
                DecayFrame[i]++;
                if (DecayFrame[i] > DecayFrames + 1)
                {
                    DecayFrame[i] = 0;
                    Moving[i] = false;
                    _alive--;
                }
            }
            else if (HitTestPlayer(i))
            {
                Moving[i] = false;
                _alive--;
                DecayFrame[i] = 0;
            }
        }
    }

    /// <summary>visible_after_hittests_for_cur(): the return value only drives drawing; the side effects matter.</summary>
    void HitTestsForPellet(int i, int left, int top)
    {
        var w = _w;
        var s = w.Shots;
        for (int k = 0; k < 3; k++)
        {
            // Quirk: the second test also looks at stale positions of shots that are not moving.
            if (s.Moving[k] && Overlap.XywhXywhLtGt(left, top, W, H, s.Left[k], s.Top[k], Shots.W, Shots.H)) return;
            if (Overlap.XywhXywhLtGt(left + 8, top, W, H, s.Left[k], s.Top[k], Shots.W, Shots.H)) return;
        }
        if (DecayFrame[i] != 0) return;
        if (Overlap.XywhXywhLtGt(left, top, W + 8, H, w.Orb.Left, w.Orb.Top, Playfield.OrbW, Playfield.OrbH))
        {
            // The Orb destroys pellets it overlaps, even ones it does not really touch.
            VelY[i] >>= 1;
            VelX[i] >>= 1;
            DecayFrame[i] = 1;
            DestroyScoreDelta += DestroyScore;
            return;
        }
        if (w.PlayerDeflecting && Overlap.XywhXywhLtGt(left, top, W, H,
                w.Player.Left - W, Player.Top - H * 2, Playfield.PlayerW + W * 2, Playfield.PlayerH + H * 2))
        {
            int deflectAngle = ToPixel(CurLeft[i]) <= w.Player.Left + 12 ? 0x80 : 0x00;
            Vector2(out VelX[i], out VelY[i], 8 * 16, deflectAngle);
            if (FromGroup[i] == PelletGroup.None) Motion[i] = PelletMotion.Regular;
        }
    }

    bool HitTestPlayer(int i)
    {
        var w = _w;
        if (w.PlayerInvincible || DecayFrame[i] != 0) return false;
        int l = ToPixel(CurLeft[i]), t = ToPixel(CurTop[i]);
        if (l >= w.Player.Left + 4 && l <= w.Player.Left + 20 &&
            t >= Player.Top + (w.PlayerSliding ? 8 : 0) &&
            t < Player.Top + Playfield.PlayerH - H)
        {
            w.PlayerIsHit = true;
            return true;
        }
        return false;
    }

    void CloudsUpdate()
    {
        for (int i = 0; i < Count; i++)
        {
            if (CloudFrame[i] == 0) continue;
            CloudFrame[i]++;
            if (CloudFrame[i] == 9)
            {
                CloudFrame[i] = 0;
                Moving[i] = true;
            }
        }
    }

    /// <summary>Pellets.decay(): every live pellet starts its destroy animation (bomb, miss).</summary>
    public void Decay()
    {
        for (int i = 0; i < Count; i++)
        {
            if (!Moving[i] || DecayFrame[i] != 0) continue;
            VelY[i] = VelY[i] * 2 / 3;   // original: int /= 1.5f, truncating toward zero
            VelX[i] = VelX[i] * 2 / 3;
            DecayFrame[i] = 1;
            DestroyScoreDelta += DestroyScore;
        }
    }

    /// <summary>Pellets.reset_nonclouds(): pellets still in their spawn cloud survive (original quirk).</summary>
    public void ResetNonClouds()
    {
        for (int i = 0; i < Count; i++)
        {
            if (!Moving[i]) continue;
            Moving[i] = false;
            DecayFrame[i] = 0;
            CloudFrame[i] = 0;
            _alive--;
        }
    }

    public void Hash(ref StateHasher h)
    {
        for (int i = 0; i < Count; i++)
        {
            h.Add(Moving[i]); h.Add(CurLeft[i]); h.Add(CurTop[i]); h.Add(VelX[i]); h.Add(VelY[i]);
            h.Add((int)FromGroup[i]); h.Add(DecayFrame[i]); h.Add(CloudFrame[i]);
            h.Add((int)Motion[i]); h.Add(Speed[i]); h.Add(Angle[i]); h.Add(SlingDirection[i]);
        }
        h.Add(_alive); h.Add(Interlace); h.Add(InterlaceField); h.Add(SpawnWithCloud); h.Add(DestroyScoreDelta);
    }
}
