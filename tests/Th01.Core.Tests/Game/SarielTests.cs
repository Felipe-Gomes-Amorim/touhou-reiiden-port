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
        Assert.Equal(400 + 293 + Sariel.RingsTicks, ticks);
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
        void RunPatternOut(Sariel x) { int g = 0; int done = x.PatternsDone; while (x.PatternsDone == done && g++ < 2000) x.Update(); }
        s.Phase = 7; s.PatternsUntilNext = 99; s.PatternCur = 1;
        RunPatternOut(s);
        Assert.Equal((7, 2, 1), (s.Phase, s.PatternCur, s.PatternsDone));
        RunPatternOut(s);                                    // pattern 2 wraps the ring of phase 7 back to 0
        Assert.Equal((0, 2), (s.PatternCur, s.PatternsDone));
        s.Phase = 5; s.PatternCur = 5; s.PatternsDone = 0;
        RunPatternOut(s);
        Assert.Equal(10, s.PatternCur);                      // 5 -> 10: the ring of phase 5 jumps to a value with no pattern
        s.Phase = 3; s.PatternCur = 0; s.PatternsDone = 0;
        RunPatternOut(s);
        Assert.Equal(1, s.PatternCur);                       // the ring of phase 3 alternates
        s.Phase = 1; s.PatternCur = 2; s.PatternsDone = 0;
        RunPatternOut(s);
        Assert.Equal(0, s.PatternCur);                       // 2 -> 0
    }

    [Fact]
    public void The_phase_ends_when_the_pattern_count_is_reached_unless_invincible()
    {
        var (_, s) = Make();
        s.Phase = 7; s.PatternsUntilNext = 1; s.Invincible = true; s.PatternCur = 1;
        int g = 0;
        while (s.PatternsDone == 0 && g++ < 2000) { s.Invincible = true; s.InvincibilityFrame = 0; s.Update(); }
        Assert.Equal((7, 1), (s.Phase, s.PatternsDone));      // done, but invincible: it stays
        s.Invincible = true; s.InvincibilityFrame = 0;
        s.Update();
        Assert.Equal(7, s.Phase);
        s.Invincible = false;
        s.Update();
        Assert.Equal((8, 0, 0, 0), (s.Phase, s.PatternCur, s.PatternsDone, s.PhaseFrame));
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
            for (int i = 0; i < 40000; i++) { s.Update(); seen.Add(s.Phase); }
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

    // ---- part 5: phase 3 ----

    [Fact]
    public void Phase_3_particles_spawn_every_seventh_frame_with_two_random_numbers_and_move_on_even_frames()
    {
        var (w, s) = Make(seed: 31);
        s.Phase = 3; s.PatternCur = 0; s.PatternsUntilNext = 99;
        var mirror = new IRand(31);
        for (int f = 1; f <= 6; f++) s.Update();
        Assert.False(s.ParticleAlive[0]);
        w.Rng = mirror;
        var probe = new IRand(31);
        s.Update();                                          // frame 7
        Assert.True(s.ParticleAlive[0]);
        Assert.Equal((probe.Next() % 640, 0, probe.Next() % 15 + 2), (s.ParticleLeft[0], s.ParticleTop[0], s.ParticleVy[0]));
        s.Update();                                          // frame 8 (even): it moves
        Assert.Equal(s.ParticleVy[0], s.ParticleTop[0]);
    }

    [Fact]
    public void The_snowflake_comes_out_after_the_16_call_wand_raise_then_detonates_in_seven_steps()
    {
        var (w, s) = Make(Rank.Normal, seed: 3);
        s.Phase = 3; s.PatternCur = 0; s.PatternsUntilNext = 99;
        w.PlayerInvincible = true;
        for (int f = 1; f <= 174; f++) { s.Update(); Assert.True(s.SnowflakeState is -1 or 0); }
        s.Update();                                          // frame 175: the 16th wand call spawns and moves the flake
        Assert.Equal(Sariel.SnowFlakeMoving, s.SnowflakeState);
        Assert.True(s.SnowflakeTop > Sariel.WandEmitTop * 16);
        int guard = 0;
        while (s.SnowflakeState != Sariel.SnowDetonationStart && guard++ < 400) s.Update();
        Assert.Equal(Sariel.SnowDetonationStart, s.SnowflakeState);   // reached the bottom; the detonation starts next tick
        s.Update();
        Assert.Equal((4, 48, 16, 24), (s.SnowflakeState, s.SnowRadiusOuter1, s.SnowRadiusOuter2, s.SnowRadiusInner));
        var radii = new List<int>();
        guard = 0;
        while (s.SnowflakeState >= 0 && guard++ < 200) { s.Update(); if (s.SnowflakeState % 8 == 0 && s.SnowflakeState > 0) radii.Add(s.SnowRadiusOuter1); }
        Assert.Equal(new[] { 42, 36, 30, 24, 18, 12 }, radii);
        Assert.Equal(-1, s.SnowflakeState);
    }

    [Fact]
    public void The_detonation_hurts_a_player_standing_under_it_unless_invincible()
    {
        foreach (bool invincible in new[] { false, true })
        {
            var (w, s) = Make();
            s.Phase = 3; s.PatternCur = 0; s.PatternsUntilNext = 99;
            w.PlayerInvincible = true;
            for (int f = 0; f < 11; f++) s.Update();            // past the frames that set the start frame
            w.PlayerInvincible = invincible;
            s.SnowflakeState = Sariel.SnowDetonationStart + 1;
            s.SnowflakeLeft = w.Player.Left << 4;
            s.Update();
            Assert.Equal(!invincible, w.PlayerIsHit);
        }
    }

    [Fact]
    public void The_a2_rings_fire_four_clouded_pellets_each_interval_and_the_sling_pattern_lasts_299_frames()
    {
        var (w, s) = Make(Rank.Normal, seed: 8);
        s.Phase = 3; s.PatternCur = 1; s.PatternsUntilNext = 99;
        for (int f = 1; f < 12; f++) s.Update();
        Assert.Equal(0, Pellets(w));
        s.Update();                                          // frame 12 (interval 12 on Normal)
        Assert.Equal(4, Pellets(w));
        Assert.True(w.Pellets.SpawnWithCloud);
        for (int f = 13; f <= 24; f++) s.Update();
        Assert.Equal(8, Pellets(w));
        int ticks = s.PhaseFrame;
        while (s.PatternsDone == 0) { s.Update(); ticks++; Assert.True(ticks < 1000); }
        Assert.Equal(299, ticks);
        Assert.Equal((0, 1), (s.PhaseFrame, s.PatternCur == 0 ? 1 : 0));   // the next pattern of the ring of two
    }

    [Fact]
    public void Sling_clusters_start_at_frame_150_every_interval()
    {
        var (w, s) = Make(Rank.Hard, seed: 2);
        s.Phase = 3; s.PatternCur = 1; s.PatternsUntilNext = 99;
        w.PlayerInvincible = true;
        int slings = 0;
        for (int f = 1; f <= 160; f++)
        {
            s.Update();
            slings = Enumerable.Range(0, Th01.Core.Game.Pellets.Count).Count(i => w.Pellets.Motion[i] == PelletMotion.SlingAimed && (w.Pellets.Moving[i] || w.Pellets.CloudFrame[i] != 0));
            if (f < 150) Assert.Equal(0, slings);
        }
        Assert.Equal(6, slings);                             // frames 150, 152, ..., 160 with interval 2
    }

    // ---- part 6: phase 5 ----

    [Fact]
    public void Four_aimed_lasers_fire_at_50_100_150_and_200_and_the_pattern_lasts_301_frames()
    {
        var (w, s) = Make(Rank.Hard, seed: 4);
        s.Phase = 5; s.PatternCur = 0; s.PatternsUntilNext = 99;
        w.Player.Left = 100;
        for (int f = 1; f < 50; f++) s.Update();
        Assert.False(w.Lasers[0].Alive);
        s.Update();                                          // 50
        var l0 = w.Lasers[0];
        Assert.True(l0.Alive);
        Assert.Equal((460, 120, 8 + 0 * 0 + 0, 50), (l0.OriginLeft >> 8, l0.OriginY >> 8, l0.WidthCel + 1, l0.MoveoutAtAge));
        Assert.Equal(AngleMath.Cos8(AngleMath.Iatan2(400 - 120, 100 - 460)), l0.StepX);
        Assert.Equal(68 / 8, l0.RayExtendSpeed);            // Hard: 8.5 * 8 / 8
        w.Player.Left = 300;                                 // the second laser mirrors the FIRST aim, not the current one
        for (int f = 51; f <= 100; f++) s.Update();
        var l1 = w.Lasers[1];
        Assert.True(l1.Alive);
        Assert.Equal(AngleMath.Cos8(AngleMath.Iatan2(400 - 120, 639 - 100 - 180)), l1.StepX);
        for (int f = 101; f <= 150; f++) s.Update();
        Assert.Equal((608, 164), (w.Lasers[2].OriginLeft >> 8, w.Lasers[2].OriginY >> 8));
        for (int f = 151; f <= 200; f++) s.Update();
        Assert.Equal((24, 164), (w.Lasers[3].OriginLeft >> 8, w.Lasers[3].OriginY >> 8));
        int ticks = s.PhaseFrame;
        while (s.PatternsDone == 0) { s.Update(); ticks++; Assert.True(ticks < 1000); }
        Assert.Equal(301, ticks);
    }

    [Fact]
    public void The_shake_pattern_lasts_50_frames_and_the_rain_drops_a_group_every_8_frames()
    {
        var (w, s) = Make(Rank.Normal, seed: 6);
        s.Phase = 5; s.PatternCur = 1; s.PatternsUntilNext = 99;
        var lines = new HashSet<int>();
        int ticks = 0;
        while (s.PatternsDone == 0) { s.Update(); ticks++; lines.Add(s.ShakeLine); Assert.True(ticks < 200); }
        Assert.Equal(50, ticks);
        Assert.Contains(396, lines); Assert.Contains(4, lines);
        Assert.Equal(0, s.ShakeLine);

        var (w2, s2) = Make(Rank.Normal, seed: 6);
        s2.Phase = 5; s2.PatternCur = 2; s2.PatternsUntilNext = 99;
        w2.PlayerInvincible = true;
        for (int f = 1; f < 8; f++) s2.Update();
        int before = Pellets(w2);
        s2.Update();                                         // frame 8
        Assert.Equal(before + 1, Pellets(w2));
    }

    [Fact]
    public void Radial_stacks_fire_pellets_every_5_frames_from_201_and_a_laser_every_15()
    {
        var (w, s) = Make(Rank.Normal, seed: 9);
        s.Phase = 5; s.PatternCur = 3; s.PatternsUntilNext = 99;
        for (int f = 1; f < 205; f++) s.Update();
        Assert.Equal(0, Pellets(w));
        s.Update();                                          // 205: one aimed + two at +-4
        Assert.Equal(3, Pellets(w));
        for (int f = 206; f < 210; f++) s.Update();
        Assert.False(w.Lasers[0].Alive);
        s.Update();                                          // 210: %15 == 0, slot (210 - 215) / 15 = 0
        Assert.True(w.Lasers[0].Alive);
        int ticks = s.PhaseFrame;
        while (s.PatternsDone == 0) { s.Update(); ticks++; Assert.True(ticks < 1000); }
        Assert.Equal(450, ticks);
    }

    [Fact]
    public void Wavy_particles_spawn_every_seventh_frame_and_rise_one_pixel_on_even_frames()
    {
        var (w, s) = Make(seed: 14);
        s.Phase = 5; s.PatternCur = 1; s.PatternsUntilNext = 99;
        var probe = new IRand(14);
        for (int f = 1; f < 7; f++) s.Update();
        w.Rng = new IRand(14);
        s.Update();                                          // frame 7
        Assert.True(s.WavyAlive[0]);
        Assert.Equal((probe.Next() % 640, 400), (s.WavyLeft[0], s.WavyTop[0]));
        s.Update();                                          // frame 8
        Assert.Equal((399, 1), (s.WavyTop[0], s.WavyAge[0]));
    }

    // ---- part 7: phase 7 ----

    [Fact]
    public void Four_semicircle_spreads_fire_20_10_8_and_10_pellets_and_last_151_frames()
    {
        var (w, s) = Make(Rank.Normal, seed: 3);
        s.Phase = 7; s.PatternCur = 1; s.PatternsUntilNext = 99;
        w.Pellets.SpawnWithCloud = false;
        for (int f = 1; f < 50; f++) s.Update();
        Assert.Equal(0, Pellets(w));
        s.Update();
        Assert.Equal(20, Pellets(w));
        for (int f = 51; f <= 60; f++) s.Update();
        Assert.Equal(30, Pellets(w));
        for (int f = 61; f <= 70; f++) s.Update();
        Assert.Equal(38, Pellets(w));
        for (int f = 71; f <= 80; f++) s.Update();
        Assert.Equal(48, Pellets(w));
        int ticks = s.PhaseFrame;
        while (s.PatternsDone == 0) { s.Update(); ticks++; Assert.True(ticks < 1000); }
        Assert.Equal(151, ticks);
    }

    [Fact]
    public void Symmetric_birds_are_laid_in_mirrored_pairs_every_10_frames_and_the_pattern_lasts_300()
    {
        var (w, s) = Make(Rank.Normal, seed: 3);
        s.Phase = 7; s.PatternCur = 0; s.PatternsUntilNext = 99;
        for (int f = 1; f < 60; f++) s.Update();
        Assert.Equal(0, s.BirdAlive.Count(b => b));
        s.Update();                                          // 60: x = 2 + 2 * 10
        var alive = Enumerable.Range(0, Sariel.BirdCount).Where(i => s.BirdAlive[i]).ToArray();
        Assert.Equal(2, alive.Length);
        Assert.Equal((22, 367, 617, 367), (s.BirdLeft[alive[0]] >> 4, s.BirdTop[alive[0]] >> 4, s.BirdLeft[alive[1]] >> 4, s.BirdTop[alive[1]] >> 4));
        int ticks = s.PhaseFrame;
        while (s.PatternsDone == 0) { s.Update(); ticks++; Assert.True(ticks < 1000); }
        Assert.Equal(300, ticks);
    }

    [Fact]
    public void Vertical_stacks_fire_columns_skipping_the_player_and_the_rain_starts_early_from_frame_131()
    {
        var (w, s) = Make(Rank.Normal, seed: 5);
        s.Phase = 7; s.PatternCur = 2; s.PatternsUntilNext = 99;
        w.Player.Left = 300;
        for (int f = 1; f <= 142; f++) s.Update();
        w.Rng = new IRand(77);
        s.Update();                                          // 143 = 11 * 13: two rounds of a left and a right pellet, one random number each
        var probe = new IRand(77);
        for (int i = 0; i < 4; i++) probe.Next();
        Assert.Equal(probe.Next(), w.Rng.Next());
        Assert.Equal(0, s.RaysTargetLeftX % 32);             // and the ray moved on by one column
        for (int f = 144; f <= 160; f++) s.Update();         // 160: the first column of stacks, no random numbers
        int ticks = s.PhaseFrame;
        while (s.PatternsDone == 0) { s.Update(); ticks++; Assert.True(ticks < 2000); }
        Assert.Equal(601, ticks);
    }

    // ---- part 8: form 1 defeated, form 2 ----

    [Fact]
    public void Zero_hp_in_form_1_resets_the_field_then_runs_200_frames_of_shaking_and_the_transition_into_form_2()
    {
        var (w, s) = Make(seed: 17);
        s.Phase = 1; s.PatternCur = 0; s.PatternsUntilNext = 99;
        w.Lasers[2].Spawn(100, 100, 200, 300, 60, 15, 20, 4);
        w.Orb.Left = 123; w.Orb.Top = 222; w.Player.Left = 55;
        s.Hp = 0;
        s.Update();
        Assert.Equal(Sariel.PhaseForm1Defeated, s.Phase);
        Assert.True(s.BlockingTransition);
        s.Update();                                          // the first call of the animation does the resets
        Assert.False(w.Lasers[2].Alive);
        Assert.Equal((Playfield.OrbLeftStart, Playfield.OrbTopStart, Playfield.PlayerLeftStart), (w.Orb.Left, w.Orb.Top, w.Player.Left));
        int ticks = 0;
        bool sawShake = false, sawFlash = false;
        while (s.Phase == Sariel.PhaseForm1Defeated && ticks++ < 1000)
        {
            s.Update();
            sawShake |= s.Form1DefeatShakeLine != 0;
            sawFlash |= s.Form1DefeatFlash;
        }
        Assert.Equal(Sariel.Form1DefeatShakeTicks + Sariel.Form1DefeatTransitionTicks, ticks);
        Assert.True(sawShake && sawFlash);
        Assert.Equal((Sariel.PhaseForm2, 6, 0, 0, 10, 3), (s.Phase, s.Hp, s.PhaseFrame, s.PatternCur, s.HpFirstWhite, s.HpFirstRedWhite));
        Assert.False(s.BlockingTransition);
    }

    static (GameWorld w, Sariel s) Form2(int cur, Rank rank = Rank.Normal, uint seed = 6)
    {
        var (w, s) = Make(rank, seed);
        s.Phase = Sariel.PhaseForm2; s.Hp = 6; s.PatternCur = cur;
        w.PlayerInvincible = true;
        return (w, s);
    }

    [Fact]
    public void Form_2_cycles_through_the_patterns_0_to_8_then_4_to_8_and_ends_at_zero_hp()
    {
        var (w, s) = Form2(0);
        var order = new List<int> { 0 };
        int guard = 0;
        while (order.Count < 14 && guard++ < 20000)
        {
            s.Update();
            if (s.PatternCur != order[^1]) order.Add(s.PatternCur);
        }
        Assert.Equal(new[] { 0, 1, 2, 3, 4, 5, 6, 7, 8, 4, 5, 6, 7, 8 }, order);
        s.Hp = 0;
        s.Update();
        Assert.True(s.Defeated);
        Assert.Equal(Sariel.PhaseForm2Defeated, s.Phase);
    }

    [Fact]
    public void Form_2_pattern_lengths()
    {
        var (_, s) = Form2(0);
        var lengths = new Dictionary<int, int>();
        int cur = 0, ticks = 0, total = 0;
        while (lengths.Count < 6 && total++ < 20000)
        {
            s.Update();
            ticks++;
            if (s.PatternCur != cur) { lengths[cur] = ticks; cur = s.PatternCur; ticks = 0; }
        }
        Assert.Equal(101, lengths[0]);
        Assert.Equal(104, lengths[2]);                        // the climbing ray, then the rain
        Assert.Equal(301, lengths[4]);                        // leaves with the interval a spray left in pattern_state
        Assert.Equal(201, lengths[5]);
    }

    [Fact]
    public void A_curved_spray_starts_at_frame_100_and_a_falling_leaf_hurts_a_player_it_touches()
    {
        var (w, s) = Form2(1, Rank.Normal, 9);
        for (int f = 1; f < 100; f++) s.Update();
        int Live() => Enumerable.Range(0, Th01.Core.Game.Pellets.Count).Count(i => w.Pellets.Moving[i] || w.Pellets.CloudFrame[i] != 0);
        Assert.Equal(0, Live());
        s.Update();                                          // 100: the first shot
        Assert.Equal(1, Live());

        var (w2, s2) = Form2(5);
        w2.PlayerInvincible = false;
        s2.LeafFlag[0] = Sariel.LfLeaf;
        s2.LeafLeft[0] = (w2.Player.Left + 4) * 10;
        s2.LeafTop[0] = (Player.Top + 8) * 10;
        s2.PhaseFrame = 1;
        s2.Update();                                         // frame 2 (even): the leaf pass runs and tests the player
        Assert.True(w2.PlayerIsHit);
    }
}
