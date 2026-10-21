namespace Th01.Core.Game;

/// <summary>The player's 8 shot slots. ref: th01/main/player/shot.cpp, shot.hpp</summary>
public sealed class Shots
{
    public const int Count = 8;
    public const int W = 16;
    public const int H = 16;
    const int SpriteMargin = 2;
    const int DecayFrames = 7;
    public const int PelletW = 8; // ref: th01/sprites/pellet.h
    public const int PelletH = 8;

    readonly GameWorld _w;

    public readonly int[] Left = new int[Count];
    public readonly int[] Top = new int[Count];
    public readonly bool[] Moving = new bool[Count];
    public readonly byte[] DecayFrame = new byte[Count];

    public Shots(GameWorld w) => _w = w;

    public void Add(int newLeft, int newTop)
    {
        if (newLeft < Playfield.Left || newLeft > Playfield.Right - 1) return;
        for (int i = 0; i < Count; i++)
        {
            if (Moving[i] || DecayFrame[i] != 0) continue;
            Left[i] = newLeft;
            Top[i] = newTop;
            Moving[i] = true;
            _w.Sound.PlaySe(1);
            DecayFrame[i] = 0;
            return;
        }
    }

    /// <summary>unput_and_reset(): note the original resets decay_frame to 0 only for decaying slots.</summary>
    public void Reset()
    {
        for (int i = 0; i < Count; i++)
        {
            if (Moving[i]) Moving[i] = false;
            else if (DecayFrame[i] != 0) DecayFrame[i] = 0;
        }
    }

    static int DecayQuarter(int frame) => frame / 4 + 1;

    public void Update()
    {
        for (int i = 0; i < Count; i++)
        {
            if (Moving[i])
            {
                Top[i] -= 12;
                if (!_w.Orb.InPortal && HitTestOrb(i, _w.Orb.Left, _w.Orb.Top)) continue;
                if (Top[i] < Playfield.Top) Moving[i] = false;
            }
            else if (DecayFrame[i] != 0)
            {
                DecayFrame[i]++;
                if (DecayFrame[i] > DecayFrames)
                {
                    DecayFrame[i] = 0;
                    Moving[i] = false;
                }
            }
        }
    }

    /// <summary>Shot sprite quarter to draw while decaying (render helper).</summary>
    public static int DecayQuarterFor(byte decayFrame) => DecayQuarter(decayFrame);

    public bool HitTestOrb(int i, int orbLeft, int orbTop)
    {
        if (!Moving[i]) return false;
        if (!Overlap.XywhXywhLtGt(Left[i], Top[i], W, H, orbLeft, orbTop, Playfield.OrbW, Playfield.OrbH)) return false;

        var o = _w.Orb;
        int dx = Left[i] - orbLeft;
        if (dx > W / 2) o.VelocityX = OrbVelocityX.Left4;
        else if (dx == W / 2) o.VelocityX = OrbVelocityX.Zero;
        else if (dx > -W) o.VelocityX = OrbVelocityX.Right4;
        o.ForceNew(0, OrbForce.Shot);
        Moving[i] = false;

        // Original quirk: an extra, full orb movement step on the frame of the hit.
        o.MoveX(o.VelocityX);
        o.Top += o.VelocityYUpdate();
        return true;
    }

    /// <summary>Any shot vs. a pellet at the given top-left; the first hit starts the decay. Used by pellets (item 5).</summary>
    public bool HitTestPellet(int pelletLeft, int pelletTop)
    {
        for (int i = 0; i < Count; i++)
        {
            if (!Moving[i]) continue;
            if (DecayFrame[i] == 1) continue;
            if (Overlap.XywhXywhLeGe(Left[i], Top[i], W - SpriteMargin, H - SpriteMargin,
                    pelletLeft, pelletTop, PelletW - SpriteMargin, PelletH - SpriteMargin))
            {
                OnHit(i);
                return true;
            }
        }
        return false;
    }

    /// <summary>Any shot vs. a boss hitbox given as in shot_hitbox_t (w/h already minus the shot size).</summary>
    public bool HitTestBoss(int hitboxLeft, int hitboxTop, int wMinusShotW, int hMinusShotH)
    {
        for (int i = 0; i < Count; i++)
        {
            if (!Moving[i]) continue;
            if (Overlap.XyXywhLeGe(Left[i], Top[i], hitboxLeft, hitboxTop, wMinusShotW, hMinusShotH))
            {
                OnHit(i);
                return true;
            }
        }
        return false;
    }

    void OnHit(int i)
    {
        Moving[i] = false;
        DecayFrame[i] = 1;
        _w.Sound.PlaySe(16);
    }

    public void Hash(ref StateHasher h)
    {
        for (int i = 0; i < Count; i++)
        {
            h.Add(Left[i]); h.Add(Top[i]); h.Add(Moving[i]); h.Add(DecayFrame[i]);
        }
    }
}
