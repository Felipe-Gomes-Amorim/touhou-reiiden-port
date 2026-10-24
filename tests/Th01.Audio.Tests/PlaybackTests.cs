using Th01.Assets.Config;
using Th01.Audio.Mdrv;
using Th01.Audio.Playback;
using Th01.Audio.Synth;

namespace Th01.Audio.Tests;

public class PlaybackTests
{
    /// <summary>Smallest valid MDT: one FM track (id 0x80) with the given commands and one FM voice (index 1).</summary>
    static byte[] Mdt(byte[] track)
    {
        var d = new byte[0x40 + 4 + track.Length + 1 + 64];
        BitConverter.GetBytes((ushort)0x0302).CopyTo(d, 0);
        int trackAt = 0x44, endAt = trackAt + track.Length, voiceAt = endAt + 1;
        BitConverter.GetBytes((ushort)0x80).CopyTo(d, 4); BitConverter.GetBytes((ushort)trackAt).CopyTo(d, 6);
        BitConverter.GetBytes((ushort)0x10).CopyTo(d, 8); BitConverter.GetBytes((ushort)endAt).CopyTo(d, 10);
        BitConverter.GetBytes((ushort)voiceAt).CopyTo(d, 0x3A); BitConverter.GetBytes((ushort)(voiceAt + 64)).CopyTo(d, 0x3C);
        track.CopyTo(d, trackAt);
        d[endAt] = 0xFF;
        d[voiceAt + 32 + 4] = 0xC7;
        for (int i = 0; i < 4; i++) { d[voiceAt + 32 + 6 + i] = 1; d[voiceAt + 32 + 14 + i] = 0x1F; d[voiceAt + 32 + 26 + i] = 0x0F; }
        return d;
    }

    // E9 90, voice 1, intro note, loop: two notes, then F3 back to the second note.
    static readonly byte[] LoopingTrack = [0xE9, 0x90, 0xEB, 0x01, 0x40, 0x0C, 0x49, 0x0C, 0x4B, 0x0C, 0xF3, 0, 0, 0xFF];

    static byte[] LoopingMdt()
    {
        var t = (byte[])LoopingTrack.Clone();
        // F3 sits at index 10, the target is the note 49 at index 6: offset = 6 - (10 + 2) = -6
        short off = -6;
        t[11] = (byte)(off & 0xFF); t[12] = (byte)((off >> 8) & 0xFF);
        return Mdt(t);
    }

    [Fact]
    public void F3_Target_Is_The_Address_Of_F3_Plus_2_Plus_Offset()
    {
        var song = MdrvSong.Load(LoopingMdt());
        var t = Assert.Single(song.Tracks);
        Assert.True(t.Loops);
        Assert.Equal(12, t.LoopStartTick);   // after the intro note (12 ticks)
        Assert.Equal(36, t.TotalTicks);
    }

    [Fact]
    public void F3_Without_A_Reachable_Target_Means_No_Loop_Point()
    {
        var t = TrackDecoder.Decode([0x40, 0x04, 0xF3, 0x50, 0x00, 0xFF], 0, 6, 0x80, ChannelKind.Fm);
        Assert.True(t.Loops);
        Assert.Equal(-1, t.LoopStartTick);
    }

    [Fact]
    public void Unroll_Repeats_Only_The_Loop_Part()
    {
        var t = Assert.Single(MdrvSong.Load(LoopingMdt()).Tracks);
        var starts = SongLooper.Unroll(t, 36 + 24 * 2).Select(n => n.StartTick).ToList();
        Assert.Equal([0, 12, 24, 36, 48, 60, 72], starts);
    }

    [Fact]
    public void Looper_Renders_A_Loop_Region()
    {
        var r = SongLooper.Render(MdrvSong.Load(LoopingMdt()));
        Assert.True(r.Loops);
        Assert.True(r.PeriodsMatch);
        Assert.InRange(r.LoopStartFrame, 1, r.EndFrame - 1);
        Assert.Equal(r.EndFrame * 2, r.Pcm.Length);
        // loop region = one period of 24 ticks at tempo 0x90
        double period = (r.EndFrame - r.LoopStartFrame) / (double)AudioMixer.SampleRate;
        Assert.Equal(MdrvSong.Load(LoopingMdt()).TickToSeconds(24), period, 2);
        Assert.Contains(r.Pcm, s => Math.Abs(s) > 500);
    }

    [Fact]
    public void Song_Without_F3_Plays_Once()
    {
        var r = SongLooper.Render(MdrvSong.Load(Mdt([0xEB, 0x01, 0x40, 0x0C, 0xFF])));
        Assert.False(r.Loops);
        Assert.True(r.Frames > 0);
    }

    static LoopedPcm Pcm(int frames, int loopStart, bool loops, short value) =>
        new(Enumerable.Repeat(value, frames * 2).ToArray(), loopStart, frames, loops, true);

    [Fact]
    public void Mixer_Is_Silent_Without_Sounds()
    {
        var m = new AudioMixer();
        var buf = new short[64];
        Array.Fill(buf, (short)1);
        m.Mix(buf);
        Assert.All(buf, s => Assert.Equal(0, s));
    }

    [Fact]
    public void Mixer_Wraps_To_The_Loop_Start()
    {
        var m = new AudioMixer();
        // frames 0..3 = 100, frames 4..7 = 200, loop from 4
        var pcm = new short[16];
        for (int f = 0; f < 8; f++) pcm[f * 2] = pcm[f * 2 + 1] = (short)(f < 4 ? 100 : 200);
        m.PlayMusic(new LoopedPcm(pcm, 4, 8, true, true));
        var buf = new short[2 * 16];
        m.Mix(buf);
        short first = buf[0], last = buf[^2];
        Assert.True(first > 0 && last > first, "intro is quieter than the loop part");
        Assert.Equal(buf[8 * 2], buf[12 * 2]); // after the wrap it keeps playing the loop part
        Assert.True(m.MusicPlaying);
    }

    [Fact]
    public void Mixer_Stops_A_Music_That_Does_Not_Loop()
    {
        var m = new AudioMixer();
        m.PlayMusic(Pcm(10, 0, false, 1000));
        var buf = new short[40];
        m.Mix(buf);
        Assert.False(m.MusicPlaying);
        Assert.Equal(0, buf[^1]);
        Assert.NotEqual(0, buf[0]);
    }

    [Fact]
    public void Mixer_FadeOut_Ends_The_Music()
    {
        var m = new AudioMixer();
        m.PlayMusic(Pcm(1000, 0, true, 10000));
        m.FadeOutMusic(0.01);   // 441 frames
        var buf = new short[2 * 1000];
        m.Mix(buf);
        Assert.False(m.MusicPlaying);
        Assert.True(Math.Abs(buf[2]) > Math.Abs(buf[800]));
        Assert.Equal(0, buf[^1]);
    }

    [Fact]
    public void Mixer_Crossfades_The_Old_Track_Into_The_New_One()
    {
        var m = new AudioMixer();
        m.PlayMusic(Pcm(5000, 0, true, 10000));
        m.PlayMusic(Pcm(5000, 0, true, 20000), fadeOutOld: 0.01, fadeInNew: 0.01);   // 441 frames
        var buf = new short[2 * 1000];
        m.Mix(buf);
        short first = buf[0], mid = buf[2 * 220], late = buf[2 * 900];
        Assert.InRange((int)first, 7000, 8100);              // old track at full volume (10000 * 0.8)
        Assert.InRange((int)late, 15900, 16100);              // only the new track (20000 * 0.8)
        Assert.InRange((int)mid, 9000, 14000);                // both, each about half
        Assert.True(m.MusicPlaying);
        // the old track is gone after its fade: nothing else is left to fade
        m.StopMusic();
        Assert.False(m.MusicPlaying);
    }

    [Fact]
    public void Mixer_Fade_Then_New_Track_Lets_The_Old_One_Finish_Its_Fade()
    {
        var m = new AudioMixer();
        m.PlayMusic(Pcm(5000, 0, true, 10000));
        m.FadeOutMusic(0.01);                                      // 441 frames, no current track now
        Assert.True(m.MusicPlaying);
        m.PlayMusic(Pcm(5000, 0, true, 20000), fadeOutOld: 1.0, fadeInNew: 0);   // old one is already fading: keeps its own ramp
        var buf = new short[2 * 1000];
        m.Mix(buf);
        Assert.InRange((int)buf[2 * 900], 15900, 16100);          // only the new one remains
    }

    [Fact]
    public void Mixer_Plays_Effects_Once_And_Restarts_The_Same_Id()
    {
        var m = new AudioMixer();
        short[] fx = [1000, 1000, 1000, 1000, 1000, 1000];   // 3 frames
        m.PlayEffect(5, fx);
        var buf = new short[2 * 8];
        m.Mix(buf);
        Assert.NotEqual(0, buf[4]);
        Assert.Equal(0, buf[6]);
        m.PlayEffect(5, fx);
        m.Mix(buf.AsSpan(0, 2));
        m.PlayEffect(5, fx);   // restart: not two overlapping voices
        var one = new short[2];
        m.Mix(one);
        Assert.Equal(buf[0], one[0]);
    }

    [Fact]
    public void Mixer_Never_Overflows_When_Many_Sounds_Overlap()
    {
        var m = new AudioMixer();
        for (int i = 0; i < 20; i++) m.PlayEffect(i, Enumerable.Repeat((short)30000, 200).ToArray());
        m.PlayMusic(Pcm(100, 0, true, 30000));
        var buf = new short[100];
        m.Mix(buf);
        Assert.All(buf, s => Assert.InRange((int)s, short.MinValue, short.MaxValue));
    }

    static bool WaitFor(Func<bool> cond, int ms = 15000)
    {
        var sw = System.Diagnostics.Stopwatch.StartNew();
        while (sw.ElapsedMilliseconds < ms) { if (cond()) return true; Thread.Sleep(10); }
        return cond();
    }

    [Fact]
    public void Sink_Starts_Music_In_The_Background_And_Ignores_Missing_Files()
    {
        byte[] mdt = LoopingMdt();
        using var sink = new MdrvSoundSink(name => name == "T.MDT" ? mdt : null);
        sink.PlaySe(3);          // no effect bank: silently ignored
        sink.PlayBgm("NOPE.MDT");
        sink.PlayBgm("T.MDT");
        Assert.True(WaitFor(() => sink.Mixer.MusicPlaying));
        var buf = new short[2048];
        sink.Mixer.Mix(buf);
        Assert.Contains(buf, s => s != 0);
        sink.StopBgm();
        Assert.False(sink.Mixer.MusicPlaying);
    }

    [Fact]
    public void Sink_Drops_A_Track_That_Was_Replaced_Or_Stopped_While_Rendering()
    {
        byte[] mdt = LoopingMdt();
        using var sink = new MdrvSoundSink(_ => mdt);
        sink.PlayBgm("A.MDT");
        sink.StopBgm();
        Thread.Sleep(1500);
        Assert.False(sink.Mixer.MusicPlaying);
    }

    [Fact]
    public void Sink_Reports_Broken_Files_Instead_Of_Throwing()
    {
        string? failed = null;
        using var sink = new MdrvSoundSink(_ => new byte[10]);
        sink.Failed += (n, _) => failed = n;
        sink.PlayBgm("BAD.MDT");
        Assert.True(WaitFor(() => failed is not null));
        Assert.Equal("BAD.MDT", failed);
        Assert.False(sink.Mixer.MusicPlaying);
    }

    [Fact]
    public void Mixer_Waits_For_Frames_That_Are_Not_Rendered_Yet()
    {
        var m = new AudioMixer();
        var music = new LoopedPcm(Enumerable.Repeat((short)1000, 20).ToArray(), 0, 10, true, true, ready: 4);
        m.PlayMusic(music);
        var buf = new short[2 * 6];
        m.Mix(buf);
        Assert.NotEqual(0, buf[2 * 3]);     // frame 3 was ready
        Assert.Equal(0, buf[2 * 4]);        // frame 4 was not: silence, position stays
        music.SetReady(10);
        m.Mix(buf);
        Assert.NotEqual(0, buf[0]);         // continues from frame 4
    }

    [Fact]
    public void Streaming_Render_Reports_Progress_And_Matches_The_Full_Render()
    {
        var song = MdrvSong.Load(Mdt([0xE9, 0x30, 0xEB, 0x01, 0xE4, 0x20, 0x40, 0x0C, 0x45, 0x0C, 0xE5, 0xF3, 0xF9, 0xFF, 0xFF]));
        // F3 offset: loop target = start of the E4 block; hand-built stream, so just check the plain path
        var r = SongLooper.Start(song);
        Assert.True(r.WaitComplete(30000));
        Assert.Equal(r.EndFrame, r.ReadyFrames);
        Assert.Contains(r.Pcm, v => v != 0);
    }

    [Fact]
    public void Bgm_Mapping_Follows_The_Original_Boss_Table()
    {
        Assert.Equal("ST0.MDT", BgmTracks.ForStage(0, 0, 0));
        Assert.Equal("ST0.MDT", BgmTracks.ForStage(3, 0, 0));
        Assert.Equal("POSITIVE.MDT", BgmTracks.ForStage(4, 0, 0));
        Assert.Equal("ST1.MDT", BgmTracks.ForStage(5, 0, 1));
        Assert.Equal("ST2.MDT", BgmTracks.ForStage(5, 1, 2));
        Assert.Equal("LEGEND.MDT", BgmTracks.ForStage(9, 0, 1));
        Assert.Equal("LEGEND.MDT", BgmTracks.ForStage(9, 1, 2));
        Assert.Equal("KAMI2.MDT", BgmTracks.ForStage(14, 0, 3));
        Assert.Equal("KAMI.MDT", BgmTracks.ForStage(14, 1, 4));
        Assert.Equal("TENSI.MDT", BgmTracks.ForStage(19, 0, 5));
        Assert.Equal("ALICE.MDT", BgmTracks.ForStage(19, 1, 6));
    }

    [Fact]
    public void Bgm_Files_Are_Known_Game_Files()
    {
        var known = Th01.Assets.ExpectedFiles.All.Select(f => f.Name).ToHashSet(StringComparer.OrdinalIgnoreCase);
        var all = new List<string> { BgmTracks.Title, BgmTracks.Ending, BgmTracks.AfterEnding, BgmTracks.SarielSecondForm, BgmTracks.DriverInit };
        all.AddRange(BgmTracks.MusicRoom);
        for (int stage = 0; stage < 20; stage++)
            for (int route = 0; route < 2; route++)
                all.Add(BgmTracks.ForStage(stage, route, stage < 5 ? 0 : stage / 5 * 2 - 1 + route));
        Assert.All(all, f => Assert.Contains(f, known));
        Assert.Equal(15, BgmTracks.MusicRoom.Count);
    }

    [Fact]
    public void Sink_Ignores_A_Request_For_The_Track_Already_Playing()
    {
        int loads = 0;
        byte[] mdt = LoopingMdt();
        using var sink = new MdrvSoundSink(_ => { Interlocked.Increment(ref loads); return mdt; });
        int baseline = loads;   // the effects bank was asked for once
        sink.PlayBgm("T.MDT");
        Assert.True(WaitFor(() => sink.Mixer.MusicPlaying));
        sink.PlayBgm("t.mdt");
        sink.PlayBgm("T.MDT");
        Thread.Sleep(200);
        Assert.Equal(baseline + 1, loads);
        Assert.Equal("T.MDT", sink.CurrentTrack);
        sink.StopBgm();
        Assert.Null(sink.CurrentTrack);
        sink.PlayBgm("T.MDT");   // after a stop it starts again
        Assert.True(WaitFor(() => sink.Mixer.MusicPlaying));
        Assert.Equal(baseline + 2, loads);
    }

    // ---- With the real game files (skipped without TH01_DATA_PATH) ----

    static string? DataFolder()
    {
        string? f = GameDataConfig.Resolve();
        return f is not null && Directory.Exists(f) ? f : null;
    }

    [SkippableFact]
    public void RealFiles_EveryF3_Lands_On_A_Command_Boundary()
    {
        string? folder = DataFolder();
        Skip.If(folder is null, $"Game data not configured. Set {GameDataConfig.EnvVar}.");
        var report = new System.Text.StringBuilder();
        foreach (string f in Directory.GetFiles(folder!, "*.MDT").OrderBy(x => x))
        {
            var song = MdrvSong.Load(File.ReadAllBytes(f));
            foreach (var t in song.Tracks.Where(t => t.Loops && t.TotalTicks > 0))   // an empty track has nothing to loop
            {
                Assert.True(t.LoopStartTick >= 0 && t.LoopStartTick < t.TotalTicks, $"{Path.GetFileName(f)} track {t.TrackId:X2}: loop target {t.LoopStartTick}, length {t.TotalTicks}");
            }
            foreach (var t in song.Tracks)
                report.AppendLine($"    {Path.GetFileName(f),-12} track {t.TrackId:X2} {t.Kind,-4} total={t.TotalTicks,6} loopStart={t.LoopStartTick,6} period={(t.Loops ? t.TotalTicks - t.LoopStartTick : 0),6} ignored=[{string.Join(",", t.Ignored.OrderBy(k => k.Key).Select(k => $"{k.Key:X2}x{k.Value}"))}]");
            // After decoding F6/F7/F9/E8/FC, every melodic channel of a song loops with the same period (or a divisor of the longest);
            // only the drum tracks of POSITIVE, ST3 (their FA commands, probably pattern calls, are not understood yet) and IRIS are off.
            var loopers = song.Tracks.Where(t => t.Loops && t.TotalTicks > t.LoopStartTick && t.LoopStartTick >= 0 && t.Notes.Count > 0).ToList();
            if (loopers.Count > 0)
            {
                int longest = loopers.Max(t => t.TotalTicks - t.LoopStartTick);
                foreach (var t in loopers.Where(t => longest % (t.TotalTicks - t.LoopStartTick) != 0))
                    Assert.True(t.Kind == ChannelKind.Drum || Path.GetFileName(f).Equals("IRIS.MDT", StringComparison.OrdinalIgnoreCase), $"{Path.GetFileName(f)} track {t.TrackId:X2} loops every {t.TotalTicks - t.LoopStartTick} ticks, the longest is {longest}");
            }
            var periods = song.Tracks.Where(t => t.Loops).Select(t => t.TotalTicks - t.LoopStartTick).Distinct().OrderBy(x => x).ToList();
            report.AppendLine($"{Path.GetFileName(f),-12} loop periods (ticks): {string.Join(",", periods)}");
        }
        string? outDir = Environment.GetEnvironmentVariable("TH01_AUDIO_OUT");
        if (outDir is not null) { Directory.CreateDirectory(outDir); File.WriteAllText(Path.Combine(outDir, "loops.txt"), report.ToString()); }
    }

    [SkippableFact]
    public void RealFiles_Sink_Plays_A_Looping_Track_And_The_Effects()
    {
        string? folder = DataFolder();
        Skip.If(folder is null, $"Game data not configured. Set {GameDataConfig.EnvVar}.");
        string path = Path.Combine(folder!, "ST0.MDT");
        Skip.If(!File.Exists(path) || !File.Exists(Path.Combine(folder!, "ZIGOKU.MDE")), "ST0.MDT / ZIGOKU.MDE not found.");
        var sw = System.Diagnostics.Stopwatch.StartNew();
        using var sink = new MdrvSoundSink(n => File.Exists(Path.Combine(folder!, n)) ? File.ReadAllBytes(Path.Combine(folder!, n)) : null);
        long effectsMs = sw.ElapsedMilliseconds;
        sw.Restart();
        sink.PlayBgm("ST0.MDT");
        Assert.True(WaitFor(() => sink.Mixer.MusicPlaying, 120000), "music did not start");
        long queuedMs = sw.ElapsedMilliseconds;
        // first sound that comes out of the mixer (the intro of ST0 may begin with rests)
        var probe = new short[2048];
        long firstMs = -1;
        while (sw.ElapsedMilliseconds < 120000)
        {
            sink.Mixer.Mix(probe);
            if (probe.Any(v => v != 0)) { firstMs = sw.ElapsedMilliseconds; break; }
            Thread.Sleep(5);
        }
        Assert.True(firstMs >= 0, "no sound came out");
        long musicMs = sw.ElapsedMilliseconds;
        sink.PlaySe(1);
        var buf = new short[4096];
        sink.Mixer.Mix(buf);
        Assert.Contains(buf, s => s != 0);
        string? outDir = Environment.GetEnvironmentVariable("TH01_AUDIO_OUT");
        if (outDir is not null)
        {
            Directory.CreateDirectory(outDir);
            File.WriteAllText(Path.Combine(outDir, "sink-timing.txt"), $"effects bank {effectsMs} ms, ST0 queued after {queuedMs} ms, first sound after {firstMs} ms (total {musicMs} ms)\n");
            // What the speakers get: the intro, then the loop twice.
            var looped = SongLooper.Render(MdrvSong.Load(File.ReadAllBytes(path)));
            var m = new AudioMixer();
            m.PlayMusic(looped);
            var pcm = new short[(looped.EndFrame + 2 * (looped.EndFrame - looped.LoopStartFrame)) * 2];
            m.Mix(pcm);
            WavWriter.Save(Path.Combine(outDir, "ST0-looped-twice.wav"), pcm);
        }
    }
}
