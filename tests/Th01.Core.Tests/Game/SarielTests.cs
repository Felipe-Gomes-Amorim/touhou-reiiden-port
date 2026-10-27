using Th01.Core.Audio;
using Th01.Core.Game;
using Th01.Core.Rng;

namespace Th01.Core.Tests.Game;

/// <summary>Rules: docs/GAMEPLAY-NOTES.md section 33 (ref: th01/main/boss/b20m.cpp). Part 1: set-up, entrance, phase 0 and hit boxes.</summary>
public class SarielTests
{
    // The boss is not wired into GameWorld yet, so tests build it on a world of another stage.
    static (GameWorld w, Sariel s) Make(Rank rank = Rank.Normal, uint seed = 5)
    {
        var w = new GameWorld(new GameOptions { FrameRand = seed, Rank = rank, Lives = 3, Bombs = 1 }, NullSoundSink.Instance);
        var s = new Sariel(w);
        w.Orb.Left = 0; w.Orb.Top = 380; w.Orb.PrevLeft = 0; w.Orb.PrevTop = 380;
        w.Rng = new IRand(seed);
        return (w, s);
    }

    [Fact]
    public void Set_up_matches_the_original_values()
    {
        var (_, s) = Make();
        Assert.Equal(18, s.Hp);
        Assert.Equal((304, 144), (s.Left, s.Top));
        Assert.Equal((8, 2), (s.HpFirstWhite, s.HpFirstRedWhite));
        Assert.Equal(new[] { 3, 4, 5 }, s.FlashColors);
        Assert.Equal(4 * StageSetup.StagesPerScene - 1, Sariel.Id);
        Assert.False(s.HitboxOrbInactive);
        s.Phase = Sariel.PhaseForm2;
        Assert.Equal((10, 3), (s.HpFirstWhite, s.HpFirstRedWhite));
    }

    [Fact]
    public void The_entrance_is_the_scroll_the_transition_and_the_rings()
    {
        var (_, s) = Make();
        int ticks = 1;
        while (!s.EntranceFrame()) ticks++;
        Assert.Equal(400 + 130 * 2 + 100, ticks);
        Assert.Equal(Sariel.EntranceTotal, ticks);
    }

    [Fact]
    public void Phase_0_starts_phase_1_with_the_seed_reset_and_a_pattern_count_of_1_to_6()
    {
        foreach (uint seed in new uint[] { 1, 2, 3, 4, 5, 6, 7, 8, 9, 10 })
        {
            var (w, s) = Make(seed: seed);
            w.Rng = new IRand(777);
            w.Orb.Left = 320; w.Orb.Top = 120;                         // on the face: phase 0 must not apply hits
            s.Update();
            Assert.Equal((1, 18, 0, 0), (s.Phase, s.Hp, s.PhaseFrame, s.PatternCur));
            var expect = new IRand(seed);
            Assert.Equal(expect.Next() % 6 + 1, s.PatternsUntilNext);
            Assert.InRange(s.PatternsUntilNext, 1, 6);
        }
    }

    [Fact]
    public void The_face_is_hit_by_the_current_orb_and_scores_10000()
    {
        var (w, s) = Make();
        s.Phase = 1;
        w.Orb.Left = 314 + 32; w.Orb.Top = 104 + 32;                  // the far corner of the face box
        s.HitUpdateForTests();
        Assert.Equal(17, s.Hp);
        Assert.Equal(10000u, w.Score);
        var (w2, s2) = Make();
        s2.Phase = 1;
        w2.Orb.Left = 314 + 33; w2.Orb.Top = 110;
        s2.HitUpdateForTests();
        Assert.Equal(18, s2.Hp);
    }

    [Fact]
    public void The_shield_is_hit_by_the_previous_orb_within_its_box_unless_inactive()
    {
        var (w, s) = Make();
        s.Phase = 1;
        w.Orb.PrevLeft = 304 - 24; w.Orb.PrevTop = 144 + 40;           // corner of the box
        Assert.True(s.OrbHitsShield());
        s.HitUpdateForTests();
        Assert.Equal(17, s.Hp);
        var (w2, s2) = Make();
        w2.Orb.PrevLeft = 304 + 41; w2.Orb.PrevTop = 150;
        Assert.False(s2.OrbHitsShield());
        w2.Orb.PrevLeft = 320; w2.Orb.PrevTop = 150;
        s2.HitboxOrbInactive = true;
        Assert.False(s2.OrbHitsShield());
    }

    [Fact]
    public void The_current_orb_alone_does_not_hit_the_shield()
    {
        var (w, s) = Make();
        s.Phase = 1;
        w.Orb.Left = 304; w.Orb.Top = 150;                            // inside the shield box, outside the face box, previous far away
        s.HitUpdateForTests();
        Assert.Equal(18, s.Hp);
    }
}
