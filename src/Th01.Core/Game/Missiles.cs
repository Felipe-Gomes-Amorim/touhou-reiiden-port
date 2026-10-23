namespace Th01.Core.Game;

/// <summary>
/// The missile pool (50 slots) Mima and Elis use. Positions and velocities are subpixels (1/16 px); velocities are
/// handed in as whole pixels, as the original does. State only: the sprite choice is a pure function of the velocity.
/// ref: th01/main/bullet/missile.cpp
/// </summary>
public sealed class Missiles
{
    public const int Count = 50;
    public const int W = 16, H = 16;
    const int LeftMin = Playfield.Left, TopMin = Playfield.Top;
    const int LeftMax = Playfield.Right - W, TopMax = Playfield.Bottom - 1;
    const int HitCels = 2;
    const int SoundExplode = 7;

    public const int FlagFree = 0, FlagMoving = 1, FlagHit = 2, FlagHitLast = FlagHit + HitCels - 1;

    readonly GameWorld _w;

    public readonly int[] Flag = new int[Count];
    public readonly int[] CurLeft = new int[Count], CurTop = new int[Count];
    public readonly int[] VelX = new int[Count], VelY = new int[Count];
    /// <summary>Where the explosion is drawn.</summary>
    public readonly int[] HitLeft = new int[Count], HitTop = new int[Count];

    public Missiles(GameWorld w) => _w = w;

    public int Alive
    {
        get
        {
            int n = 0;
            for (int i = 0; i < Count; i++) if (Flag[i] != FlagFree) n++;
            return n;
        }
    }

    /// <summary>Spawns a missile in the first free slot, if any. The velocity is in whole pixels per tick.</summary>
    public void Add(int left, int top, int velocityX, int velocityY)
    {
        for (int i = 0; i < Count; i++)
        {
            if (Flag[i] != FlagFree) continue;
            CurLeft[i] = left << 4;
            CurTop[i] = top << 4;
            VelX[i] = velocityX << 4;
            VelY[i] = velocityY << 4;
            Flag[i] = FlagMoving;
            return;
        }
    }

    public void Reset() => Array.Clear(Flag);

    /// <summary>unput_update_render(): move, explode on the field edge, then test the player.</summary>
    public void Update()
    {
        var w = _w;
        int field = (int)(w.FrameRand & 1);
        for (int i = 0; i < Count; i++)
        {
            if (Flag[i] == FlagFree) continue;
            if (Flag[i] == FlagMoving)
            {
                CurLeft[i] += VelX[i];
                CurTop[i] += VelY[i];
                int l = CurLeft[i] >> 4, t = CurTop[i] >> 4;
                if (!Overlap.XyXywhLeGe(l, t, LeftMin, TopMin, LeftMax - LeftMin, TopMax - TopMin))
                {
                    Flag[i] = FlagHit;
                    HitLeft[i] = l < LeftMin ? LeftMin : l > LeftMax ? LeftMax : l;
                    HitTop[i] = t < TopMin ? TopMin : t > TopMax ? Playfield.Bottom - H / 2 : t;
                }
            }
            else
            {
                if (i % 2 == field) Flag[i]++;
                if (Flag[i] > FlagHitLast)
                {
                    Flag[i] = FlagFree;
                    w.Sound.PlaySe(SoundExplode);
                }
            }
        }

        if (w.PlayerInvincible) return;
        for (int i = 0; i < Count; i++)
        {
            if (Flag[i] == FlagFree) continue;
            // 46x46 pixels around the player's centre point
            int l = CurLeft[i] >> 4, t = CurTop[i] >> 4;
            if (l > w.Player.Left - W / 2 && l < w.Player.Left + Playfield.PlayerW / 2 + W / 2 &&
                t < Player.Top + Playfield.PlayerH && t > Player.Top - H / 2)
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
            h.Add(Flag[i]);
            if (Flag[i] == FlagFree) continue;
            h.Add(CurLeft[i]); h.Add(CurTop[i]); h.Add(VelX[i]); h.Add(VelY[i]); h.Add(HitLeft[i]); h.Add(HitTop[i]);
        }
    }
}
