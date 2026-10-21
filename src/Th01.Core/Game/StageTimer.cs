namespace Th01.Core.Game;

/// <summary>
/// Stage timer and the HARRY UP state. The countdown ticks every 4th frame_rand value.
/// The pellets HARRY UP fires (and their RNG draws) join with the pellet port.
/// ref: th01/main/stage/timer.cpp
/// </summary>
public sealed class StageTimer
{
    public const int Tick = 2;
    const int StagesOutsideRoute = 5;
    const int StagesOnRoute = 15;

    // ref: timer_init_for() table: 5 shrine stages, then 15 per route (Makai, Jigoku).
    static readonly int[] StageTimes =
    {
        1000, 1000, 1000, 1000, 2000,
        1200, 1200, 1200, 1200, 3000,
        1400, 1400, 1400, 1400, 4000,
        1600, 1600, 1600, 600, 6000,
        1200, 1200, 1200, 1200, 2000,
        1400, 1400, 1400, 1400, 4000,
        1600, 1800, 1800, 1600, 8000,
    };

    /// <summary>stage_timer: ticks left before HARRY UP.</summary>
    public int Value;
    /// <summary>frame_since_harryup.</summary>
    public int FrameSinceHarryUp;
    byte _harryUpCycle;

    public static int InitialFor(int stageId, int route) =>
        stageId < StagesOutsideRoute ? StageTimes[stageId] : StageTimes[route * StagesOnRoute + stageId];

    public void InitFor(int stageId, int route)
    {
        Value = InitialFor(stageId, route);
        FrameSinceHarryUp = 0;
    }

    /// <summary>timer_tick_and_put(). Returns true on the tick the timer hits 0 (blocking HARRY UP banner follows).</summary>
    public bool TickDown(GameWorld w)
    {
        if (Value > 0)
        {
            Value -= Tick;
            if (Value == 0)
            {
                w.Pellets.SpeedRaise(2);   // pellet_speed_raise(0.05)
                w.Sound.PlaySe(17);
                return true;
            }
            return false;
        }
        PatternHarryUp(w);
        return false;
    }

    void PatternHarryUp(GameWorld w)
    {
        FrameSinceHarryUp++;
        _harryUpCycle++;
        bool fire =
            FrameSinceHarryUp < 200 ? _harryUpCycle % 5 == 0 :
            FrameSinceHarryUp < 300 ? _harryUpCycle % 4 == 0 :
            FrameSinceHarryUp < 400 ? _harryUpCycle % 3 == 0 :
            FrameSinceHarryUp < 500 ? _harryUpCycle % 2 == 0 :
            true;
        if (!fire) return;
        // rank + 1 random-wide pellets from random columns of the top edge (playfield_rand_x()).
        for (int i = 0; i <= (int)w.Rank; i++)
            w.Pellets.AddGroup(w.Rng.Next() % Playfield.Width, Playfield.Top, PelletGroup.OneRandomWide, Pellets.Sp3_0);
    }

    /// <summary>timer_extend_and_put(): after losing a life; cancels HARRY UP.</summary>
    public void Extend()
    {
        Value += Value != 0 ? 200 : 1000;
        FrameSinceHarryUp = 0;
    }

    public void Hash(ref StateHasher h)
    {
        h.Add(Value); h.Add(FrameSinceHarryUp); h.Add(_harryUpCycle);
    }
}
