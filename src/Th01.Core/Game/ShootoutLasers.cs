namespace Th01.Core.Game;

/// <summary>
/// One shoot-out laser: a ray of pixels that grows from its origin along a fixed direction, then has its start
/// moved along the same direction so that it travels off the field. Positions are Q24.8 ("laser pixels").
/// The original draws and hit-tests in the same pass; this keeps the walk (and its early exits) and drops the drawing.
/// ref: th01/main/bullet/laser_s.cpp
/// </summary>
public sealed class ShootoutLaser
{
    public const int LaserW = 4;

    readonly GameWorld _w;

    public bool Alive;
    public int Age;
    public int MoveoutAtAge;
    public int RayLength, RayMoveoutSpeed, RayExtendSpeed;
    public int RayStartLeft, RayStartY;     // Q24.8
    public int StepX, StepY;                // Q24.8 per ray pixel, [-1, +1]
    public int WidthCel;
    public bool Damaging;
    public byte Color;

    int _rayILeft, _rayIY;

    public ShootoutLaser(GameWorld w) => _w = w;

    static int ToLaserPixel(int pixel) => pixel << 8;
    public int StartLeftPixel => RayStartLeft >> 8;
    public int StartYPixel => RayStartY >> 8;

    /// <summary>Does nothing if the laser is alive already; patterns rely on that.</summary>
    public void Spawn(int originLeft, int originY, int targetLeft, int targetY, int speedMultipliedBy8, byte color,
        int moveoutAtAge, int w)
    {
        if (Alive) return;
        RayStartLeft = ToLaserPixel(originLeft);
        RayStartY = ToLaserPixel(originY);
        int angle = AngleMath.Iatan2(targetY - originY, targetLeft - originLeft);
        StepX = AngleMath.Cos8(angle);
        StepY = AngleMath.Sin8(angle);
        RayExtendSpeed = speedMultipliedBy8 / 8;
        RayMoveoutSpeed = RayExtendSpeed;
        WidthCel = w - 1;
        Alive = true;
        Damaging = true;
        MoveoutAtAge = moveoutAtAge;
        Age = 0;
        Color = color;
        RayLength = 0;
    }

    /// <summary>hittest_and_render() without the drawing: walks <paramref name="count"/> pixels from the ray's start.</summary>
    void Walk(bool put, int count)
    {
        var w = _w;
        int left = _rayILeft >> 8, y = _rayIY >> 8;
        for (int i = 0; i < count; i++)
        {
            if ((left >> 3) != (_rayILeft >> 11) || y != (_rayIY >> 8))
            {
                if (put && !w.PlayerInvincible && Damaging)
                {
                    if (y > Player.Top + 8 &&
                        w.Player.Left + Playfield.PlayerW / 8 * 1 <= left &&
                        w.Player.Left + Playfield.PlayerW / 8 * 6 > left)
                        w.PlayerIsHit = true;
                }
                left = _rayILeft >> 8;
                y = _rayIY >> 8;
                if (left > Playfield.Right - 2 || left < Playfield.Left || y < Playfield.Top || y > Playfield.Bottom - 1)
                    break;
            }
            _rayILeft += StepX;
            _rayIY += StepY;
        }
    }

    public void Update()
    {
        if (!Alive) return;
        if (Age >= MoveoutAtAge)
        {
            _rayILeft = RayStartLeft;
            _rayIY = RayStartY;
            Walk(false, RayMoveoutSpeed);
            // The walk has advanced the cursor by the move-out speed.
            RayStartLeft = _rayILeft;
            RayStartY = _rayIY;
            if (RayStartLeft >= ToLaserPixel(Playfield.Right) || RayStartLeft < ToLaserPixel(Playfield.Left) ||
                RayStartY < ToLaserPixel(Playfield.Top) || RayStartY >= ToLaserPixel(Playfield.Bottom))
            {
                Alive = false;
                return;
            }
        }
        else
        {
            RayLength += RayExtendSpeed;
        }
        Age++;
        _rayILeft = RayStartLeft;
        _rayIY = RayStartY;
        Walk(true, RayLength);
    }

    public void Hash(ref StateHasher h)
    {
        h.Add(Alive);
        if (!Alive) return;
        h.Add(Age); h.Add(RayLength); h.Add(RayStartLeft); h.Add(RayStartY); h.Add(StepX); h.Add(StepY);
        h.Add(RayExtendSpeed); h.Add(MoveoutAtAge);
    }
}

/// <summary>The 10 shoot-out lasers. ref: th01/main/bullet/laser_s.hpp</summary>
public sealed class ShootoutLasers
{
    public const int Count = 10;
    public readonly ShootoutLaser[] Lasers = new ShootoutLaser[Count];

    public ShootoutLasers(GameWorld w)
    {
        for (int i = 0; i < Count; i++) Lasers[i] = new ShootoutLaser(w);
    }

    /// <summary>shootout_laser_safe(i)</summary>
    public ShootoutLaser Safe(int i) => Lasers[i % Count];

    public void Update()
    {
        foreach (var l in Lasers) l.Update();
    }

    /// <summary>shootout_lasers_unput_and_reset_broken(i, count): the bosses pass 5, so lasers 5..9 survive.</summary>
    public void ResetFirst(int count)
    {
        for (int i = 0; i < count && i < Count; i++) Lasers[i].Alive = false;
    }

    public void Hash(ref StateHasher h)
    {
        foreach (var l in Lasers) l.Hash(ref h);
    }
}
