namespace Th01.Core.Game;

/// <summary>
/// The 8 sparkles around Reimu while she is invincible (after a miss or a bomb). Purely decorative, but each refresh
/// draws three random numbers, so they shape the RNG sequence of the whole game: they must run even when nothing is
/// drawn. A sparkle lives 1..7 frames plus the frame it was born, then is re-rolled.
/// ref: th01/main/player/inv_spr.cpp
/// </summary>
public sealed class InvincibilitySparkles
{
    public const int Count = 8;
    const int Frames = 8;
    const int Done = 0xFF;

    public readonly int[] Frame = new int[Count];
    public readonly int[] Left = new int[Count];
    public readonly int[] Top = new int[Count];

    readonly GameWorld _w;

    public InvincibilitySparkles(GameWorld w) => _w = w;

    /// <summary>True while the sparkles are active (frame[0] is the "off" marker otherwise).</summary>
    public bool Active => Frame[0] != Done;

    public void Update(bool invincible)
    {
        if (!invincible)
        {
            if (Frame[0] == Done) return;
            Frame[0] = Done;   // the original unblits them here
            return;
        }
        if (Frame[0] == Done)
            for (int i = 0; i < Count; i++) Frame[i] = 0;

        for (int i = 0; i < Count; i++)
        {
            if (Frame[i] == Frames + 1 || Frame[i] == 0)
            {
                Left[i] = _w.Rng.Next() % 48 + (_w.Player.Left - 8);
                Top[i] = _w.Rng.Next() % 48 + (Player.Top - 16);
                Frame[i] = _w.Rng.Next() % 7 + 1;
            }
            Frame[i]++;
        }
    }

    public void Hash(ref StateHasher h)
    {
        for (int i = 0; i < Count; i++) { h.Add(Frame[i]); h.Add(Left[i]); h.Add(Top[i]); }
    }
}
