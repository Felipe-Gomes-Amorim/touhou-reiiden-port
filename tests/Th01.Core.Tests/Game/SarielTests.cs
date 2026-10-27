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

    // ---- part 2: form 1 phase machine (patterns are stubs that end at once) ----

    [Fact]
    public void A_finished_pattern_advances_the_ring_and_counts_it()
    {
        var (_, s) = Make();
        s.Phase = 3; s.PatternsUntilNext = 99; s.PatternCur = 0;      // phase 3 patterns are still stubs that end at once
        s.Update();
        Assert.Equal((3, 1, 1), (s.Phase, s.PatternCur, s.PatternsDone));
        s.Update();
        Assert.Equal((0, 2), (s.PatternCur, s.PatternsDone));
        s.Phase = 5; s.PatternCur = 5;
        s.Update();
        Assert.Equal(10, s.PatternCur);
        s.Phase = 3; s.PatternCur = 0;
        s.Update();
        Assert.Equal(1, s.PatternCur);
        s.Update();
        Assert.Equal(0, s.PatternCur);
    }

    [Fact]
    public void The_phase_ends_when_the_pattern_count_is_reached_unless_invincible()
    {
        var (_, s) = Make();
        s.Phase = 3; s.PatternsUntilNext = 2; s.Invincible = true; s.PatternCur = 0;
        s.Update(); s.Update();
        Assert.Equal(3, s.Phase);
        s.Invincible = false;
        s.Update();
        Assert.Equal((4, 0, 0, 0), (s.Phase, s.PatternCur, s.PatternsDone, s.PhaseFrame));
    }

    [Fact]
    public void Zero_hp_goes_to_phase_99()
    {
        var (_, s) = Make();
        s.Phase = 7; s.PatternsUntilNext = 99; s.Hp = 0;
        s.Update();
        Assert.Equal(Sariel.PhaseForm1Defeated, s.Phase);
    }

    [Theory]
    [InlineData(2, 3, 1)]
    [InlineData(4, 5, 2)]
    [InlineData(6, 7, 3)]
    [InlineData(8, 1, 0)]
    public void A_background_transition_takes_109_ticks_and_6000_random_numbers(int phase, int next, int bg)
    {
        var (w, s) = Make(seed: 11);
        s.Phase = phase;
        var probe = new IRand(11);
        int ticks = 0;
        while (s.Phase == phase) { s.Update(); ticks++; Assert.True(ticks < 1000); }
        Assert.Equal(109, ticks);
        Assert.Equal((next, bg), (s.Phase, s.BackgroundId));
        for (int i = 0; i < 6000; i++) probe.Next();
        int roll = probe.Next();
        int expect = next switch { 3 => roll % 5 + 1, 5 => roll % 4 + 3, 7 => roll % 5 + 2, _ => roll % 6 + 1 };
        Assert.Equal(expect, s.PatternsUntilNext);
    }

    [Fact]
    public void The_form_1_cycle_is_deterministic_and_returns_to_phase_1()
    {
        ulong Run()
        {
            var (_, s) = Make(seed: 3);
            s.Update();                                   // phase 0 -> 1
            var seen = new HashSet<int>();
            for (int i = 0; i < 3000; i++) { s.Update(); seen.Add(s.Phase); }
            Assert.Contains(8, seen); Assert.Contains(7, seen);
            var h = new StateHasher(); s.Hash(ref h); return h.Value;
        }
        Assert.Equal(Run(), Run());
    }

    // ---- part 3: phase 1, pattern 0 (random purple lasers) ----

    [Fact]
    public void Purple_lasers_wait_50_frames_pick_ten_points_and_fire_one_every_10_frames()
    {
        var (w, s) = Make(Rank.Hard, seed: 21);
        s.Phase = 1; s.PatternsUntilNext = 99;
        var probe = new IRand(21);
        for (int f = 1; f < 50; f++) { s.Update(); Assert.Equal(f, s.PhaseFrame); }
        w.Rng = probe;
        var mirror = new IRand(21);
        s.Update();                                          // frame 50
        for (int i = 0; i < 10; i++)
        {
            Assert.Equal(Playfield.Left + 120 + mirror.Next() % 400, s.SpawnerX[i]);
            Assert.Equal(Playfield.Top + 36 + mirror.Next() % 100, s.SpawnerY[i]);
            Assert.InRange(s.SpawnerX[i], 120, 519); Assert.InRange(s.SpawnerY[i], 100, 199);
        }
        int fired = 0;
        for (int f = 51; f < 150; f++)
        {
            int i = (f - 50) / 10;
            Assert.False(w.Lasers[i].Alive);
            s.Update();
            if (f % 10 == 9)
            {
                int tx = Playfield.Left + mirror.Next() % 640;
                var l = w.Lasers[i];
                Assert.True(l.Alive);
                int angle = AngleMath.Iatan2(Playfield.Bottom - s.SpawnerY[i], tx - s.SpawnerX[i]);
                Assert.Equal((AngleMath.Cos8(angle), AngleMath.Sin8(angle), 68 / 8 * 0 + 76 / 8, 25, 4), (l.StepX, l.StepY, l.RayExtendSpeed, l.MoveoutAtAge, l.Color));
                fired++;
            }
            else Assert.False(w.Lasers[i].Alive);
        }
        Assert.Equal(10, fired);
        while (s.PhaseFrame != 0 && s.PatternCur == 0) s.Update();
        Assert.Equal((1, 1), (s.PatternCur, s.PatternsDone));
    }

    [Fact]
    public void Purple_lasers_last_201_frames_and_are_deterministic()
    {
        ulong Run()
        {
            var (_, s) = Make(seed: 8);
            s.Phase = 1; s.PatternsUntilNext = 99;
            int n = 0;
            while (s.PatternsDone == 0) { s.Update(); n++; Assert.True(n < 300); }
            Assert.Equal(201, n);
            var h = new StateHasher(); s.Hash(ref h); return h.Value;
        }
        Assert.Equal(Run(), Run());
    }

    // ---- part 4: birds, vortices (pattern 1) and birds on an ellipse arc (pattern 2) ----

    static int Pellets(GameWorld w) => Enumerable.Range(0, Th01.Core.Game.Pellets.Count).Count(i => w.Pellets.Moving[i] || w.Pellets.CloudFrame[i] != 0);

    [Fact]
    public void Vortices_stand_then_rise_towards_the_middle_then_sweep_sideways_for_320_frames()
    {
        var (w, s) = Make(Rank.Hard, seed: 5);
        s.Phase = 1; s.PatternCur = 1; s.PatternsUntilNext = 99;
        for (int f = 1; f <= 49; f++) s.Update();
        Assert.Equal(0, Pellets(w));
        for (int f = 50; f <= 51; f++) s.Update();
        Assert.Equal(0, Pellets(w));
        s.Update();                                        // frame 52: first volley straight down from both
        Assert.Equal(((0, 200), (608, 200)), ((s.VortexLeft[0], s.VortexTop[0]), (s.VortexLeft[1], s.VortexTop[1])));
        Assert.True(Pellets(w) >= 2);
        while (s.PhaseFrame < 101) s.Update();
        Assert.Equal((5 * 2, 200 - 2 * 2, 608 - 5 * 2), (s.VortexLeft[0], s.VortexTop[0], s.VortexLeft[1]));
        int ticks = s.PhaseFrame;
        while (s.PatternsDone == 0) { s.Update(); ticks++; Assert.True(ticks < 1000); }
        Assert.Equal(320, ticks);
    }

    [Fact]
    public void Birds_on_an_arc_drop_eggs_then_hatch_into_birds_that_fire()
    {
        var (w, s) = Make(Rank.Normal, seed: 12);
        s.Phase = 1; s.PatternCur = 2; s.PatternsUntilNext = 99;
        for (int f = 1; f <= 199; f++) s.Update();
        Assert.InRange(s.EggsAlive, 1, 10);                  // one every 15 frames from 60 on, 9 or 10 of them at most
        Assert.Equal(0, s.BirdAlive.Count(b => b));
        s.Update();                                          // frame 200: the eggs become birds
        Assert.Equal(s.EggsAlive, s.BirdAlive.Count(b => b));
        Assert.All(Enumerable.Range(0, Sariel.BirdCount).Where(i => s.BirdAlive[i]), i => Assert.Equal(Sariel.BirdHatchTicks, s.BirdHatch[i]));
        while (s.PhaseFrame < 225) s.Update();
        Assert.Equal(0, Pellets(w));
        Assert.All(Enumerable.Range(0, Sariel.BirdCount).Where(i => s.BirdAlive[i]), i => Assert.Equal(0, s.BirdHatch[i]));
        s.Update();                                          // frame 226: first volley
        Assert.True(Pellets(w) > 0);
        int ticks = s.PhaseFrame;
        while (s.PatternsDone == 0) { s.Update(); ticks++; Assert.True(ticks < 1000); }
        Assert.Equal(400, ticks);
    }

    [Fact]
    public void A_flying_bird_hits_the_player_only_when_not_invincible()
    {
        foreach (bool invincible in new[] { false, true })
        {
            var (w, s) = Make();
            s.Phase = 1; s.PatternCur = 1; s.PatternsUntilNext = 99;
            w.PlayerInvincible = invincible;
            s.BirdAlive[3] = true;
            s.BirdHatch[3] = 0;
            s.BirdLeft[3] = w.Player.Left << 4;
            s.BirdTop[3] = (Player.Top - 4) << 4;
            s.Update();
            Assert.Equal(!invincible, w.PlayerIsHit);
        }
    }
}
