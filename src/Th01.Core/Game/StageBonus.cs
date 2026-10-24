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

    /// <summary>
    /// Number of "digit scramble" steps of the TOTLE screen, one PlaySe(2) each. Every digit counts through two loops of
    /// 0..9 and stops on its value: 10 + digit steps. The digits come in the order of the screen: Time, Card combo and
    /// MIKO/Bomb as 4 digits plus a fixed 0, the stage number as 5 digits plus two fixed 0s, then the total as 6 digits.
    /// ref: th01/main/bonus.cpp totle_metric_digit_animate(), totle_animate()
    /// </summary>
    public int TotleSoundSteps()
    {
        int steps = 0;
        void Digit(int d) => steps += 10 + d % 10;
        void Four(int v) { Digit(v / 1000); Digit(v % 1000 / 100); Digit(v % 100 / 10); Digit(v % 10); Digit(0); }
        Four(Time); Four(CardCombo); Four(Resources);
        Digit(StageNumber / 10000); Digit(StageNumber % 10000 / 1000); Digit(StageNumber % 1000 / 100); Digit(StageNumber % 100 / 10); Digit(0); Digit(0);
        int div = (int)(Total / 10000), rem = (int)(Total % 10000);
        Digit(div / 10); Digit(div % 10); Digit(rem / 1000); Digit(rem % 1000 / 100); Digit(rem % 100 / 10); Digit(rem % 10);
        return steps;
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
