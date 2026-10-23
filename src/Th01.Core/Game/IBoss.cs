namespace Th01.Core.Game;

/// <summary>What the session, the trace and the state hash need from any boss. One boss per boss stage.</summary>
public interface IBoss
{
    /// <summary>boss_phase: 0 entrance, 8 = defeated; the rest is boss specific.</summary>
    int Phase { get; }
    int PhaseFrame { get; }
    int InvincibilityFrame { get; }
    int Hp { get; }
    int PatternCur { get; }
    int Left { get; }
    int Top { get; }
    /// <summary>Vertical centre of the sprite: where the defeat animation starts.</summary>
    int CenterY { get; }
    bool Defeated { get; }

    /// <summary>One frame of the blocking entrance loop; true on the last one.</summary>
    bool EntranceFrame();

    /// <summary>One tick of the boss for the phases after the entrance.</summary>
    void Update();

    void Hash(ref StateHasher h);
}
