using Th01.Core.Audio;
using Th01.Core.Game;
using Th01.Core.Input;

namespace Th01.Core.Tests.Game;

public class BonusSoundTests
{
    sealed class Recorder : ISoundSink
    {
        public readonly List<int> Se = new();
        public void PlayBgm(string trackFileName) { }
        public void StopBgm() { }
        public void FadeOutBgm() { }
        public void PlaySe(int effectId) => Se.Add(effectId);
    }

    [Fact]
    public void Every_digit_of_the_totle_screen_takes_ten_plus_its_value_steps()
    {
        // Time 0, combo 0, MIKO/Bomb 0, stage 10000 -> 4+1 digits of 0 three times (3 x 5 x 10), the stage digits
        // 1,0,0,0,0,0 (10+1 and 5 x 10), the total 6 digits (all of Total = 100000: 1 0 | 0 0 0 0).
        var b = new StageBonus(0, 0, 0, 10000, 100000);
        int expected = 3 * 5 * 10 + (11 + 5 * 10) + (11 + 10 + 4 * 10);
        Assert.Equal(expected, b.TotleSoundSteps());
        // 6553 = 6,5,5,3: 6+5+5+3 = 19 extra steps over zeros
        Assert.Equal(new StageBonus(0, 0, 0, 0, 0).TotleSoundSteps() + 19, new StageBonus(6553, 0, 0, 0, 0).TotleSoundSteps());
    }

    [Fact]
    public void The_boss_bonus_plays_sound_2_once_per_step_and_the_card_stage_bonus_is_silent()
    {
        var rec = new Recorder();
        var stage = new StageSetup
        {
            StageId = StageSetup.BossStage, Route = 0,
            Cards = new bool[StageSetup.Rows, StageSetup.Cols],
            Obstacles = new byte[StageSetup.Rows, StageSetup.Cols],
        };
        var g = new GameSession(new GameOptions { FrameRand = 3, Stage = stage }, rec);
        for (int i = 0; i < SinGyoku.EntranceFrames; i++) g.Step(GameButtons.None);
        g.World.StageCleared = true;
        g.Step(GameButtons.None);
        Assert.Equal(SessionPhase.StageBonus, g.Phase);
        int steps = g.Bonus!.Value.TotleSoundSteps();
        rec.Se.Clear();
        for (int i = 0; i < TotleTimeline.TimeAt + steps + 10; i++) g.Step(GameButtons.None);   // the digits start once the picture is in
        Assert.Equal(steps, rec.Se.Count(id => id == 2));

        var card = new Recorder();
        var h = new GameSession(new GameOptions { FrameRand = 3 }, card);
        for (int i = 0; i < 5; i++) h.Step(GameButtons.Shot);
        h.World.StageCleared = true;
        card.Se.Clear();
        h.Step(GameButtons.None);
        Assert.Equal(SessionPhase.StageBonus, h.Phase);
        for (int i = 0; i < 100; i++) h.Step(GameButtons.None);
        Assert.DoesNotContain(2, card.Se);
    }
}
