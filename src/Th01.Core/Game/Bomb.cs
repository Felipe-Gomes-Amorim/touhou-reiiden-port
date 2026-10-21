namespace Th01.Core.Game;

/// <summary>
/// Bomb timeline (state side only; the original's palette flashes, kuji-in sprites and screen shake are drawing).
/// ref: th01/main/player/bomb.cpp
/// </summary>
public sealed class Bomb
{
    public const int KeyframeActive = 50;
    public const int KeyframeDone = 140;

    /// <summary>bomb_damaging: the bomb hurts everything on screen.</summary>
    public bool Damaging;

    /// <summary>
    /// Runs one frame of the bomb. Returns true on the last frame. Pellet decay calls the original makes at
    /// frames 0 and on a few modulo schedules are NOT applied yet (pellets are not ported); see GAMEPLAY-NOTES.
    /// </summary>
    public bool UpdateFrame(GameWorld w, uint frame)
    {
        w.PlayerInvincible = true;
        if (frame == 0)
        {
            w.Pellets.Decay();
        }
        else if (frame < KeyframeActive)
        {
            // Kuji-in phase.
            if (frame % 30 == 0) w.Sound.PlaySe(8);
            if (frame % 10 == 0) w.Pellets.Decay();
        }
        else if (frame == KeyframeActive)
        {
            Damaging = true;
        }
        else if (frame < KeyframeDone)
        {
            if (frame % 10 == 0) w.Sound.PlaySe(9);
        }
        else if (frame == KeyframeDone)
        {
            w.PlayerInvincibilityTime = 100;
            Damaging = false;
            return true;
        }
        return false;
    }

    public void Hash(ref StateHasher h) => h.Add(Damaging);
}
