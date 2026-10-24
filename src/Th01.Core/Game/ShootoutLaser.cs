namespace Th01.Core.Game;

/// <summary>
/// One of the 10 "shoot-out" lasers: a thin ray that leaves a point, grows towards a target direction, then detaches
/// from its origin and slides away until it leaves the field. Positions are Q24.8 fixed point; the direction is the unit
/// vector of a 256-step angle (Sin8/Cos8, 256 = 1.0 per ray pixel).
///
/// The ray is walked pixel by pixel. The original tests the player each time the walk enters a new 8-pixel column or a
/// new row, using the position of the cell it has just LEFT; the last cell is never tested. Both are kept.
/// ref: th01/main/bullet/laser_s.cpp
/// </summary>
public sealed class ShootoutLaser
{
    readonly GameWorld _w;

    public int OriginLeft, OriginY;           // Q24.8
    public int RayStartLeft, RayStartY;       // Q24.8
    int _rayLeft, _rayY;                      // walker position, Q24.8
    public int RayLength, RayMoveoutSpeed, RayExtendSpeed;   // pixels (per frame for the speeds)
    public int StepX, StepY;                  // per ray pixel, Q24.8 in [-256, 256]
    public int Age, MoveoutAtAge;
    public int Color, WidthCel;
    public bool Alive, Damaging;

    public ShootoutLaser(GameWorld w) => _w = w;

    /// <summary>Does nothing if the laser is already alive (patterns rely on that). Speed is passed multiplied by 8.</summary>
    public void Spawn(int originLeft, int originY, int targetLeft, int targetY, int speedTimes8, int color, int moveoutAtAge, int width)
    {
        if (Alive) return;
        OriginLeft = originLeft << 8;
        OriginY = originY << 8;
        RayStartLeft = OriginLeft;
        RayStartY = OriginY;
        int angle = AngleMath.Iatan2(targetY - originY, targetLeft - originLeft);
        StepX = AngleMath.Cos8(angle);
        StepY = AngleMath.Sin8(angle);
        RayExtendSpeed = speedTimes8 / 8;
        WidthCel = width - 1;
        Alive = true;
        Damaging = true;
        MoveoutAtAge = moveoutAtAge;
        Age = 0;
        Color = color;
        RayMoveoutSpeed = RayExtendSpeed;
        RayLength = 0;
    }

    /// <summary>The original's broken unblit does nothing useful; this only frees the slot.</summary>
    public void Reset() => Alive = false;

    public void Update()
    {
        if (!Alive) return;
        if (Age >= MoveoutAtAge)
        {
            _rayLeft = RayStartLeft;
            _rayY = RayStartY;
            Walk(put: false);
            // the walk advanced the walker by the move-out speed: that is the new start
            RayStartLeft = _rayLeft;
            RayStartY = _rayY;
            if (RayStartLeft >= Playfield.Right << 8 || RayStartLeft < Playfield.Left << 8 ||
                RayStartY < Playfield.Top << 8 || RayStartY >= Playfield.Bottom << 8)
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
        _rayLeft = RayStartLeft;
        _rayY = RayStartY;
        Walk(put: true);
    }

    void Walk(bool put)
    {
        var w = _w;
        int left = _rayLeft >> 8, y = _rayY >> 8;
        int count = put ? RayLength : RayMoveoutSpeed;
        for (int i = 0; i < count; i++)
        {
            if ((left >> 3) != (_rayLeft >> 11) || y != _rayY >> 8)
            {
                if (put && !w.PlayerInvincible && Damaging)
                {
                    if (y > Player.Top + 8 &&
                        w.Player.Left + Playfield.PlayerW / 8 <= left &&
                        w.Player.Left + Playfield.PlayerW / 8 * 6 > left)
                        w.PlayerIsHit = true;
                }
                left = _rayLeft >> 8;
                y = _rayY >> 8;
                if (left > Playfield.Right - 2 || left < Playfield.Left || y < Playfield.Top || y > Playfield.Bottom - 1) break;
            }
            _rayLeft += StepX;
            _rayY += StepY;
        }
    }

    public void Hash(ref StateHasher h)
    {
        h.Add(OriginLeft); h.Add(OriginY); h.Add(RayStartLeft); h.Add(RayStartY); h.Add(_rayLeft); h.Add(_rayY);
        h.Add(RayLength); h.Add(RayMoveoutSpeed); h.Add(RayExtendSpeed); h.Add(StepX); h.Add(StepY);
        h.Add(Age); h.Add(MoveoutAtAge); h.Add(Color); h.Add(WidthCel); h.Add(Alive); h.Add(Damaging);
    }
}

/// <summary>The fixed pool of 10 lasers; bosses index it modulo 10.</summary>
public sealed class ShootoutLasers
{
    public const int Count = 10;
    public readonly ShootoutLaser[] Slots = new ShootoutLaser[Count];

    public ShootoutLasers(GameWorld w)
    {
        for (int i = 0; i < Count; i++) Slots[i] = new ShootoutLaser(w);
    }

    public ShootoutLaser this[int i] => Slots[i % Count];

    public void UpdateAll()
    {
        foreach (var l in Slots) l.Update();
    }

    public void ResetAll()
    {
        foreach (var l in Slots) l.Reset();
    }

    public void Hash(ref StateHasher h)
    {
        foreach (var l in Slots) l.Hash(ref h);
    }
}
