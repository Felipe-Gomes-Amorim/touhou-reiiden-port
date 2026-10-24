using Th01.Core.Audio;
using Th01.Core.Game;
using Th01.Core.Input;
using Th01.Core.Rng;
using Th01.Core.Sim;

namespace Th01.Core.Tests.Game;

public class ElisTests
{
    static StageSetup MakaiBoss(int id = Elis.Id, int route = 0) => new()
    {
        StageId = id, Route = route,
        Cards = new bool[StageSetup.Rows, StageSetup.Cols],
        Obstacles = new byte[StageSetup.Rows, StageSetup.Cols],
    };

    /// <summary>A world with Elis past her entrance, in the given phase, the Orb out of the way and the player unkillable.</summary>
    static (GameWorld w, Elis e) Fight(int phase = 1, int pattern = 0, Rank rank = Rank.Normal, uint seed = 9)
    {
        var w = new GameWorld(new GameOptions { FrameRand = seed, Stage = MakaiBoss(), Rank = rank }, NullSoundSink.Instance);
        var e = (Elis)w.Boss!;
        e.Phase = phase;
        e.Pattern = pattern;
        e.PhaseFrame = 0;
        w.Orb.Left = 0; w.Orb.Top = 380; w.Orb.PrevLeft = 0; w.Orb.PrevTop = 380;
        w.Player.Left = 304;
        w.PlayerInvincible = true;
        w.Rng = new IRand(seed);
        return (w, e);
    }

    static void Run(Elis e, int n) { for (int i = 0; i < n; i++) e.Update(); }

    static IRand Advanced(uint seed, int draws)
    {
        var r = new IRand(seed);
        for (int i = 0; i < draws; i++) r.Next();
        return r;
    }

    // ---- set-up ----

    [Fact]
    public void Who_gets_her_and_where_she_starts()
    {
        Assert.Equal((256, 96), (Elis.BaseLeft, Elis.BaseTop));
        var (_, e) = Fight();
        Assert.Equal(14, e.Hp);
        Assert.Equal((Elis.BaseLeft, Elis.BaseTop), (e.Left, e.Top));
        Assert.IsType<Elis>(new GameWorld(new GameOptions { Stage = MakaiBoss() }, NullSoundSink.Instance).Boss);
        Assert.IsType<Kikuri>(new GameWorld(new GameOptions { Stage = MakaiBoss(route: 1) }, NullSoundSink.Instance).Boss);
        Assert.Equal(6, e.HpFirstRedWhite);
        Assert.Equal(10, e.HpFirstWhite);
    }

    [Fact]
    public void Entrance_takes_the_blocking_loops_of_the_original()
    {
        var g = new GameSession(new GameOptions { FrameRand = 3, Stage = MakaiBoss() });
        Assert.Equal(SessionPhase.BossEntrance, g.Phase);
        int ticks = 0;
        while (g.Phase == SessionPhase.BossEntrance && ticks < 5000) { g.Step(GameButtons.Shot); ticks++; }
        Assert.Equal(SessionPhase.Playing, g.Phase);
        // loop 1: iteration t (1..119) lasts ((120 - t) / 12) + 1 frames; loop 2: 141 iterations of 1 frame
        int expected = 141;
        for (int t = 1; t < 120; t++) expected += (120 - t) / 12 + 1;
        Assert.Equal(expected, ticks);
        var e = (Elis)g.World.Boss!;
        Assert.Equal((1, 1, 0), (e.Phase, e.Pattern, e.PhaseFrame));      // phase 1 starts with the lasers, no teleport
    }

    // ---- hit boxes ----

    [Theory]
    [InlineData(8, 0, true)]
    [InlineData(7, 0, false)]
    [InlineData(88, 0, true)]
    [InlineData(89, 0, false)]
    [InlineData(40, -12, true)]
    [InlineData(40, -13, false)]
    [InlineData(40, 56, true)]
    [InlineData(40, 57, false)]
    public void The_girl_is_hit_by_the_orbs_previous_position_in_her_box(int dx, int dy, bool hit)
    {
        var (w, e) = Fight(phase: 1, pattern: 1);
        e.PhaseFrame = 1;
        w.Orb.PrevLeft = e.Left + dx; w.Orb.PrevTop = e.Top + dy;
        e.Update();
        Assert.Equal(hit ? 13 : 14, e.Hp);
        if (hit) Assert.Equal(7000u, w.Score);
    }

    [Fact]
    public void The_girl_cannot_be_hit_while_she_teleports_away()
    {
        var (w, e) = Fight(phase: 1, pattern: 0);
        Run(e, 19);
        Assert.False(e.HitboxOrbInactive);
        w.Orb.PrevLeft = e.Left + 40; w.Orb.PrevTop = e.Top + 20;       // inside her box from now on
        e.Update();                                        // frame 20: the hitbox switches off before the hit test
        Assert.True(e.HitboxOrbInactive);
        Run(e, 55);                                        // up to frame 75
        Assert.Equal(14, e.Hp);
        w.Orb.PrevLeft = e.Left + 40; w.Orb.PrevTop = e.Top + 20;       // she has moved to her new spot at frame 52
        Run(e, 1);                                         // frame 76: back on
        Assert.False(e.HitboxOrbInactive);
        Assert.Equal(13, e.Hp);
    }

    // ---- teleport ----

    [Fact]
    public void The_phase_2_teleport_lasts_81_frames_draws_ten_numbers_every_fourth_frame_and_ends_on_the_base_spot()
    {
        var (w, e) = Fight(phase: 2, seed: 7);
        Run(e, 19);
        Assert.False(e.HitboxOrbInactive);
        Run(e, 1);                                         // frame 20
        Assert.True(e.HitboxOrbInactive);
        Run(e, 56);                                        // frame 76
        Assert.False(e.HitboxOrbInactive);
        Run(e, 5);                                         // frame 81: the teleport ends and phase 3 begins
        Assert.Equal(3, e.Phase);
        Assert.Equal(1, e.Pattern);
        Assert.Equal((Elis.BaseLeft, Elis.BaseTop), (e.Left, e.Top));
        // 20 calls on frames 4, 8, ..., 80 with 5 stars x (left, top) each
        Assert.Equal(Advanced(7, 200).Seed, w.Rng.Seed);
    }

    [Fact]
    public void A_phase_1_teleport_draws_its_two_target_numbers_on_every_call()
    {
        var (w, e) = Fight(phase: 1, pattern: 0, seed: 7);
        e.Update();                                        // frame 1: the target (top, then left) is drawn, nothing else
        Assert.Equal(Advanced(7, 2).Seed, w.Rng.Seed);
        Run(e, 2);
        Assert.Equal(Advanced(7, 6).Seed, w.Rng.Seed);
        Run(e, 1);                                         // frame 4: 2 + 10 star draws
        Assert.Equal(Advanced(7, 18).Seed, w.Rng.Seed);
    }

    [Fact]
    public void After_the_teleport_a_pattern_is_chosen_with_one_draw_and_zero_means_teleport_again()
    {
        var (w, e) = Fight(phase: 1, pattern: 0, seed: 7);
        e.TeleportDone = true;
        var probe = new IRand(7);
        int expected = probe.Next() % 4;
        e.Update();
        Assert.Equal(expected, e.Pattern);
        Assert.False(e.TeleportDone);
        Assert.Equal(probe.Seed, w.Rng.Seed);
    }

    // ---- phase 1 patterns ----

    [Fact]
    public void Eleven_lasers_sweep_across_from_frame_70_every_10_frames()
    {
        var (w, e) = Fight(phase: 1, pattern: 1, seed: 5);
        Run(e, 69);
        Assert.Equal(0, w.Lasers.Slots.Count(l => l.Alive));
        Run(e, 1);
        Assert.True(w.Lasers[7].Alive);
        Run(e, 99);
        Assert.Equal(169, e.PhaseFrame);                   // still running at frame 169
        Assert.Equal(1, e.Pattern);
        Run(e, 1);                                         // frame 170: the 11th laser, then the pattern is over
        Assert.Equal(0, e.Pattern);
        Assert.Equal(0, e.PhaseFrame);
        Assert.Equal(10, w.Lasers.Slots.Count(l => l.Alive));
    }

    [Fact]
    public void The_missile_rain_fires_from_the_rifts_every_third_frame_after_60()
    {
        var (w, e) = Fight(phase: 1, pattern: 2, seed: 5);
        Run(e, 62);
        Assert.Equal(0, w.Missiles.Alive);
        Run(e, 1);                                         // frame 63
        Assert.Equal(1, w.Missiles.Alive);
        Run(e, 3);
        Assert.Equal(2, w.Missiles.Alive);
        Run(e, 107);                                       // frame 173: over
        Assert.Equal(0, e.Pattern);
    }

    // ---- phase 3 ----

    [Fact]
    public void Phase_3_patterns_start_with_the_star_of_david_and_then_run_with_a_fresh_frame_counter()
    {
        var (w, e) = Fight(phase: 3, pattern: 1);
        Run(e, 131);
        Assert.NotEqual(0, e.PhaseFrame);
        Run(e, 1);                                         // 10 + 31 steps of 2 for the summon, then 60 more: frame 132
        Assert.Equal(0, e.PhaseFrame);
        Assert.Equal(0, w.Pellets.Alive);
        Run(e, 9);
        Assert.Equal(0, w.Pellets.Alive);
        Run(e, 1);                                         // frame 10 of the pattern: the first ring (16 on Normal)
        Assert.Equal(16, w.Pellets.Alive);
        Run(e, 6);
        Assert.Equal(32, w.Pellets.Alive);
        Run(e, 8);
        Assert.Equal(48, w.Pellets.Alive);
    }

    [Fact]
    public void The_clusters_pattern_places_ten_spheres_and_fires_clusters_from_each()
    {
        var (w, e) = Fight(phase: 3, pattern: 2);
        Run(e, 132);                                       // the star
        Assert.Equal(0, e.PhaseFrame);
        Run(e, 100);
        Assert.Equal(0, w.Pellets.Alive);
        Run(e, 10);                                        // frame 110: the first cluster (7 on Normal)
        Assert.Equal(7, w.Pellets.Alive);
        Run(e, 10);
        Assert.Equal(14, w.Pellets.Alive);
    }

    [Fact]
    public void Phase_3_ends_when_hp_reaches_6_after_a_teleport_and_phase_4_transforms_her_into_the_bat_in_182_frames()
    {
        var (w, e) = Fight(phase: 3, pattern: 0);
        e.Hp = 6;
        e.PhaseFrame = 80;                                 // the end of a teleport: the phase changes on the call that finishes it
        e.Update();
        Assert.Equal(4, e.Phase);
        Assert.Equal(Elis.FGirl, e.Form);
        int ticks = 0;
        while (e.Phase == 4 && ticks < 1000) { e.Update(); ticks++; }
        Assert.Equal(5, e.Phase);
        Assert.Equal(81 + 101, ticks);                     // wave teleport back to the base, then the transformation
        Assert.Equal(Elis.FBat, e.Form);
        Assert.Equal((e.Left + 40, e.Top + 32), (e.BatLeft, e.BatTop));
    }

    // ---- phase 5 ----

    [Fact]
    public void The_bat_flies_to_a_far_target_and_changes_back_into_the_girl_who_then_changes_into_a_bat_again()
    {
        var (w, e) = Fight(phase: 5, seed: 11);
        e.Form = Elis.FBat;
        e.BatLeft = e.Left + 40; e.BatTop = e.Top + 32;
        var forms = new List<int> { e.Form };
        for (int i = 0; i < 6000 && forms.Count < 4; i++)
        {
            e.Update();
            w.Pellets.ResetNonClouds();
            if (e.Form != forms[^1]) forms.Add(e.Form);
        }
        Assert.Equal(new[] { Elis.FBat, Elis.FGirl, Elis.FBat, Elis.FGirl }, forms);
        Assert.Equal(5, e.Phase);
    }

    [Fact]
    public void The_bat_stays_inside_the_clamp_box_while_it_flies()
    {
        var (w, e) = Fight(phase: 5, seed: 3);
        e.Form = Elis.FBat;
        for (int i = 0; i < 3000; i++)
        {
            e.Update();
            w.Pellets.ResetNonClouds();
            Assert.InRange(e.BatLeft, Playfield.Left, Playfield.Right + 96);
            Assert.InRange(e.BatTop, Playfield.Top, Playfield.Bottom - 96);
        }
    }

    [Fact]
    public void The_bats_spreads_follow_the_pattern_it_drew()
    {
        var (w, e) = Fight(phase: 5, seed: 11);
        e.Form = Elis.FBat;
        e.BatLeft = 300; e.BatTop = 150;
        Run(e, 3);                                         // frame 3: the target is chosen and the first pattern is drawn
        int before = w.Pellets.Alive;
        Run(e, 40);
        Assert.True(w.Pellets.Alive >= before);
    }

    // ---- defeat ----

    [Fact]
    public void The_last_hp_ends_the_fight_and_clears_missiles_and_lasers()
    {
        var (w, e) = Fight(phase: 5);
        e.Hp = 1;
        e.Form = Elis.FGirl;
        w.Missiles.Add(300, 200, 1, 1);
        w.Lasers[3].Spawn(320, 300, 320, 400, 48, 7, 20, 4);
        w.Orb.PrevLeft = e.Left + 40; w.Orb.PrevTop = e.Top + 20;
        e.Update();
        Assert.Equal(0, e.Hp);
        Assert.True(e.Defeated);
        Assert.Equal(0, w.Missiles.Alive);
        Assert.False(w.Lasers[3].Alive);
    }

    [Fact]
    public void Defeat_goes_straight_to_the_bonus()
    {
        var g = new GameSession(new GameOptions { FrameRand = 12, Stage = MakaiBoss(), Lives = 3, Bombs = 1, Rank = Rank.Hard });
        int ticks = 0;
        while (g.Phase == SessionPhase.BossEntrance && ticks++ < 5000) g.Step(GameButtons.None);
        var e = (Elis)g.World.Boss!;
        e.Phase = 5; e.Form = Elis.FGirl; e.Hp = 1; e.Invincible = false;
        g.World.Orb.Left = e.Left + 40; g.World.Orb.Top = e.Top + 20; g.World.Orb.Y.Force = 0; g.World.Orb.ForceFrame = 0;
        g.World.Orb.PrevLeft = g.World.Orb.Left; g.World.Orb.PrevTop = g.World.Orb.Top;
        g.Step(GameButtons.None);
        Assert.True(e.Defeated);
        g.Step(GameButtons.None);
        Assert.Equal(SessionPhase.BossDefeat, g.Phase);
        ticks = 0;
        while (g.Phase == SessionPhase.BossDefeat && ticks++ < 2000) g.Step(GameButtons.None);
        Assert.Equal(SessionPhase.StageBonus, g.Phase);
        StageTests.Dismiss(g);
        Assert.Equal(SessionOutcome.Cleared, g.Outcome);
    }

    // ---- determinism ----

    static (ulong hash, int maxPhase, int maxAlive) FightRun(uint seed, Rank rank)
    {
        var g = new GameSession(new GameOptions { FrameRand = seed, Stage = MakaiBoss(), Rank = rank, Lives = 1000, Bombs = 2 });
        uint x = 0xBADC0FFE;
        GameButtons cur = GameButtons.None;
        int maxPhase = 0, maxAlive = 0;
        for (int i = 0; i < 14000; i++)
        {
            x ^= x << 13; x ^= x >> 17; x ^= x << 5;
            if (i % 6 == 0)
            {
                cur = GameButtons.None;
                if ((x & 1) != 0) cur |= GameButtons.Left;
                if ((x & 2) != 0) cur |= GameButtons.Right;
                if ((x & 4) != 0) cur |= GameButtons.Shot;
            }
            if (i % 120 == 119 && g.Phase == SessionPhase.Playing && g.World.Boss is Elis { Phase: 1 or 3 or 5 } e)
            {
                g.World.Orb.Left = e.IsBat ? e.BatLeft : e.Left + 40;
                g.World.Orb.Top = e.IsBat ? e.BatTop : e.Top + 20;
            }
            g.Step(cur);
            if (g.World.Boss is Elis ee) maxPhase = Math.Max(maxPhase, ee.Phase);
            maxAlive = Math.Max(maxAlive, g.World.Pellets.Alive);
        }
        return (g.StateHash(), maxPhase, maxAlive);
    }

    [Theory]
    [InlineData(Rank.Normal)]
    [InlineData(Rank.Lunatic)]
    public void A_full_fight_is_deterministic(Rank rank)
    {
        var a = FightRun(31, rank);
        var b = FightRun(31, rank);
        Assert.Equal(a, b);
        Assert.True(a.maxPhase >= 5, $"phase reached: {a.maxPhase}");
        Assert.True(a.maxAlive > 5);
        Assert.NotEqual(a.hash, FightRun(32, rank).hash);
    }
}
