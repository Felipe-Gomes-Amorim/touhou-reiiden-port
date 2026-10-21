namespace Th01.Core.Game;

public enum OrbVelocityX
{
    Zero = 0,
    Left4 = 1,
    Right4 = 2,
    Left8 = 3,
    Right8 = 4,
}

public enum OrbForce
{
    BounceFromSurface = 0, // floor of the playfield, or a bumper
    BounceFromTop = 1,     // ignores the immediate value
    Shot = 2,
    Immediate = 3,
}

/// <summary>
/// The Orb ("yin-yang"). Floating point is confined to <see cref="OrbYAxis"/>; everything here is integer.
/// See docs/GAMEPLAY-NOTES.md ("Orb").
/// </summary>
public sealed class Orb
{
    // ref: th01/main/player/orb.cpp
    public const int ForceRepel = -13;
    public const int FramesPerCel = 3;
    public const int Cels = 4;

    readonly GameWorld _w;

    public int Left = Playfield.OrbLeftStart;
    public int Top = Playfield.OrbTopStart;
    public int PrevLeft = Playfield.OrbLeftStart;
    public int PrevTop = Playfield.OrbTopStart;
    public bool InPortal;
    public int RotationFrame;
    public readonly OrbYAxis Y = new();
    /// <summary>Number of frames the current force has been acting.</summary>
    public int ForceFrame;
    public OrbVelocityX VelocityX = OrbVelocityX.Zero;

    public Orb(GameWorld w) => _w = w;

    public bool IsMovingLeft => VelocityX is OrbVelocityX.Left4 or OrbVelocityX.Left8;
    public bool IsMovingRight => VelocityX is OrbVelocityX.Right4 or OrbVelocityX.Right8;

    /// <summary>Per-stage start state. VelocityY is deliberately kept.</summary>
    public void StageStart()
    {
        Left = Playfield.OrbLeftStart;
        Top = Playfield.OrbTopStart;
        Y.StageStart();
        ForceFrame = 0;
        VelocityX = OrbVelocityX.Left4;
        PrevLeft = Playfield.OrbLeftStart;
        PrevTop = Playfield.OrbTopStart;
        InPortal = false;
    }

    /// <summary>Returns the pixels to add to <see cref="Top"/> this frame.</summary>
    public int VelocityYUpdate() => Y.Update(ForceFrame);

    public void ForceNew(int immediate, OrbForce kind)
    {
        if (kind == OrbForce.BounceFromSurface)
        {
            Y.BounceFromSurface();
            if (VelocityX == OrbVelocityX.Zero)
            {
                // Two independent rolls, each only drawn while the previous force is young.
                if (ForceFrame < 17 && _w.Rng.Next() % 50 == 0) VelocityX = OrbVelocityX.Left4;
                if (ForceFrame < 17 && _w.Rng.Next() % 50 == 1) VelocityX = OrbVelocityX.Right4;
            }
        }
        if (kind == OrbForce.BounceFromTop) Y.BounceFromTop(ForceFrame);
        if (kind == OrbForce.Shot) Y.Shot();
        if (kind == OrbForce.Immediate) Y.Immediate(immediate);
        ForceFrame = 0;
    }

    public void ReflectX() =>
        VelocityX = VelocityX switch
        {
            OrbVelocityX.Left4 => OrbVelocityX.Right4,
            OrbVelocityX.Right4 => OrbVelocityX.Left4,
            OrbVelocityX.Left8 => OrbVelocityX.Right8,
            OrbVelocityX.Right8 => OrbVelocityX.Left8,
            _ => OrbVelocityX.Zero,
        };

    public void ReflectY()
    {
        Y.Reflect();
        ForceFrame = 0;
    }

    /// <summary>Moves by <paramref name="v"/> but bounces off the walls using the CURRENT <see cref="VelocityX"/>.</summary>
    public void MoveX(OrbVelocityX v)
    {
        switch (v)
        {
            case OrbVelocityX.Left4: Left -= 4; break;
            case OrbVelocityX.Right4: Left += 4; break;
            case OrbVelocityX.Left8: Left -= 8; break;
            case OrbVelocityX.Right8: Left += 8; break;
        }
        if (Left <= Playfield.OrbLeftMin)
        {
            if (VelocityX == OrbVelocityX.Left4) VelocityX = OrbVelocityX.Right4;
            else if (VelocityX == OrbVelocityX.Left8) VelocityX = OrbVelocityX.Right8;
        }
        if (Left >= Playfield.OrbLeftMax)
        {
            if (VelocityX == OrbVelocityX.Right4) VelocityX = OrbVelocityX.Left4;
            else if (VelocityX == OrbVelocityX.Right8) VelocityX = OrbVelocityX.Left8;
        }
    }

    /// <summary>First half of orb_and_pellets_and_stage_unput_update_render: free movement and floor/ceiling.</summary>
    public void Move()
    {
        if (InPortal) return;
        MoveX(VelocityX);
        Top += VelocityYUpdate();

        if (IsMovingLeft) RotationFrame++;
        if (IsMovingRight) RotationFrame--;
        // Quirk: the last cel is shown for a single frame only.
        if (RotationFrame >= Cels * FramesPerCel - 2) RotationFrame = 0;
        if (RotationFrame < 0) RotationFrame = (Cels - 1) * FramesPerCel;

        if (Top > Playfield.OrbTopMax)
        {
            ForceNew(0, OrbForce.BounceFromSurface);
            Top = Playfield.OrbTopMax;
            _w.CardComboCur = 0;
        }
        if (Top < Playfield.OrbTopMin)
        {
            ForceNew(0, OrbForce.BounceFromTop);
            Top = Playfield.OrbTopMin;
        }
    }

    public int AnimCel => RotationFrame / FramesPerCel;

    public void EndOfFrame()
    {
        PrevLeft = Left;
        PrevTop = Top;
    }

    public void Hash(ref StateHasher h)
    {
        h.Add(Left); h.Add(Top); h.Add(PrevLeft); h.Add(PrevTop);
        h.Add(InPortal); h.Add(RotationFrame);
        h.Add(ForceFrame); h.Add((int)VelocityX); Y.Hash(ref h);
    }
}
