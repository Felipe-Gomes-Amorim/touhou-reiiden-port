namespace Th01.Core.Game;

/// <summary>
/// End-of-stage bonus: four clamped metrics, each added ten times. All values in points.
/// ref: th01/main/bonus.cpp stagebonus_animate()
/// </summary>
public readonly record struct StageBonus(int Time, int CardCombo, int Resources, int StageNumber, uint Total)
{
    const int Max = 6553;

    static int Clamp(int v) => v > Max ? Max : v;

    /// <summary>End-of-scene (TOTLE) bonus after a boss: bigger multipliers. ref: bonus.cpp totle_animate()</summary>
    public static StageBonus ComputeTotle(int stageNum, int timerValue, int cardComboMax, int lives, int bombs)
    {
        int time = Clamp(timerValue * 5);
        int combo = Clamp(cardComboMax * 200);
        int res = Clamp(lives * 500 + bombs * 200);
        int stage = Math.Min(stageNum * 1000, 65530);
        return new StageBonus(time, combo, res, stage, (uint)(time + combo + res + stage) * 10);
    }

    /// <param name="stageNum">1-based number of the cleared stage (stage_id + 1).</param>
    public static StageBonus Compute(int stageNum, int timerValue, int cardComboMax, int lives, int bombs)
    {
        // The original works on 16-bit ints; none of these products overflow with in-game values.
        int time = Clamp(timerValue * 3);
        int combo = Clamp(cardComboMax * 100);
        int res = Clamp(lives * 200 + bombs * 100);
        int stage = Clamp((stageNum - 1) * 200);
        return new StageBonus(time, combo, res, stage, (uint)(time + combo + res + stage) * 10);
    }
}
