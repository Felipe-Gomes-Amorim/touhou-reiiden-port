namespace Th01.Core.Game;

public enum MissileState : byte
{
    Free = 0,
    Moving = 1,
    /// <summary>Exploding: 2 and 3 are the two animation cels; anything above frees the slot.</summary>
    Hit = 2,
}

/// <summary>
/// Guided-by-nothing homing-look missiles of the Mima fights (50 slots, 16x16, straight lines). They explode on
/// touching the field edge and hurt the player on contact, including while they explode. The explosion advances only on
/// every other frame, chosen by the low bit of frame_rand, so frame_rand is gameplay state here.
/// ref: th01/main/bullet/missile.cpp
/// </summary>
public sealed class Missiles
{
    public const int Count = 50;
    public const int W = 16, H = 16;
    const int LeftMax = Playfield.Right - W;
    const int TopMax = Playfield.Bottom - 1;
    const int HitLast = (int)MissileState.Hit + 1;
    const int New = -1;

    readonly GameWorld _w;

    public readonly int[] CurLeft = new int[Count], CurTop = new int[Count];
    /// <summary>Last drawn position; for exploding missiles the clamped edge position they blow up at.</summary>
    public readonly int[] PrevLeft = new int[Count], PrevTop = new int[Count];
    public readonly int[] VelX = new int[Count], VelY = new int[Count];
    public readonly int[] Flag = new int[Count];

    public Missiles(GameWorld w) => _w = w;

    /// <summary>Spawns a missile at pixel (left, top) in the first free slot; velocities are in subpixels (1/16 px).</summary>
    public void Add(int left, int top, int velocityX, int velocityY)
    {
        for (int i = 0; i < Count; i++)
        {
            if (Flag[i] != (int)MissileState.Free) continue;
            CurLeft[i] = left << 4;
            CurTop[i] = top << 4;
            PrevLeft[i] = New;
            VelX[i] = velocityX;
            VelY[i] = velocityY;
            Flag[i] = (int)MissileState.Moving;
            return;
        }
    }

    public void Reset() => Array.Fill(Flag, (int)MissileState.Free);

    public int Alive => Flag.Count(f => f != (int)MissileState.Free);

    /// <summary>Direction sprite for the renderer: 0..15, sixteen steps over the full turn (angle / 16).</summary>
    public int DirectionCel(int i) => AngleMath.Iatan2(VelY[i], VelX[i]) / 16;

    /// <summary>One frame: move or explode every missile, then test the player.</summary>
    public void Update()
    {
        var w = _w;
        int field = (int)(w.FrameRand & 1);
        for (int i = 0; i < Count; i++)
        {
            if (Flag[i] == (int)MissileState.Free) continue;
            if (Flag[i] == (int)MissileState.Moving)
            {
                CurLeft[i] += VelX[i];
                CurTop[i] += VelY[i];
                int px = CurLeft[i] >> 4, py = CurTop[i] >> 4;
                if (!(px >= Playfield.Left && px <= LeftMax && py >= Playfield.Top && py <= TopMax))
                {
                    Flag[i] = (int)MissileState.Hit;
                    if (px < Playfield.Left) PrevLeft[i] = Playfield.Left << 4;
                    else if (px > LeftMax) PrevLeft[i] = LeftMax << 4;
                    if (py < Playfield.Top) PrevTop[i] = Playfield.Top << 4;
                    else if (py > TopMax) PrevTop[i] = (Playfield.Bottom - H / 2) << 4;
                }
                else if (i % 2 == field)
                {
                    PrevLeft[i] = CurLeft[i];   // "drawn" this frame
                    PrevTop[i] = CurTop[i];
                }
            }
            else
            {
                if (i % 2 == field) Flag[i]++;
                if (Flag[i] > HitLast)
                {
                    Flag[i] = (int)MissileState.Free;
                    w.Sound.PlaySe(7);
                }
            }
        }

        if (w.PlayerInvincible) return;
        for (int i = 0; i < Count; i++)
        {
            if (Flag[i] == (int)MissileState.Free) continue;
            int px = CurLeft[i] >> 4, py = CurTop[i] >> 4;
            // 46x46 pixels around the player's centre
            if (px > w.Player.Left - W / 2 && px < w.Player.Left + Playfield.PlayerW / 2 + W / 2 &&
                py < Player.Top + Playfield.PlayerH && py > Player.Top - H / 2)
            {
                w.PlayerIsHit = true;
                return;
            }
        }
    }

    public void Hash(ref StateHasher h)
    {
        for (int i = 0; i < Count; i++)
        {
            h.Add(Flag[i]); h.Add(CurLeft[i]); h.Add(CurTop[i]); h.Add(PrevLeft[i]); h.Add(PrevTop[i]); h.Add(VelX[i]); h.Add(VelY[i]);
        }
    }
}
