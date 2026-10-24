using Th01.Assets.Config;
using Th01.Audio.Mdrv;
using Th01.Audio.Synth;

namespace Th01.Audio.Tests;

public class MdrvTests
{
    static DecodedTrack Decode(ChannelKind kind, params byte[] bytes) => TrackDecoder.Decode(bytes, 0, bytes.Length, 0x80, kind);

    [Fact]
    public void Notes_Rests_And_Octaves()
    {
        var t = Decode(ChannelKind.Fm, 0x90, 0x18, 0x43, 0x0C, 0x5B, 0x30, 0xFF);
        Assert.Equal(2, t.Notes.Count);
        Assert.Equal(24, t.Notes[0].StartTick);
        Assert.Equal(4 * 12 + 3, t.Notes[0].Semitone);
        Assert.Equal(5 * 12 + 11, t.Notes[1].Semitone);
        Assert.Equal(36, t.Notes[1].StartTick);
        Assert.Equal(36 + 48, t.TotalTicks);
        Assert.False(t.Loops);
    }

    [Fact]
    public void Repeat_E4_E5_Expands()
    {
        var t = Decode(ChannelKind.Ssg, 0xE4, 0x03, 0x40, 0x06, 0xE5, 0xFF);
        Assert.Equal([0, 6, 12], t.Notes.Select(n => n.StartTick));
    }

    [Fact]
    public void Volta_E0_E1_E2_SkipsTheEndingOnTheLastPass()
    {
        // [A | B] x2 then C  =>  A B A C
        var t = Decode(ChannelKind.Fm, 0xE0, 0x02, 0x40, 0x04, 0xE1, 0x41, 0x04, 0xE2, 0x42, 0x04, 0xFF);
        Assert.Equal([0, 1, 0, 2], t.Notes.Select(n => n.Semitone - 48));
    }

    [Fact]
    public void Nested_Repeats_Work()
    {
        var t = Decode(ChannelKind.Fm, 0xE4, 0x02, 0xE4, 0x02, 0x40, 0x01, 0xE5, 0x41, 0x01, 0xE5, 0xFF);
        Assert.Equal(6, t.Notes.Count);
        Assert.Equal(6, t.TotalTicks);
    }

    [Fact]
    public void Tie_Extends_The_Same_Pitch()
    {
        var t = Decode(ChannelKind.Fm, 0x50, 0x18, 0x91, 0x50, 0x18, 0xFF);
        Assert.Single(t.Notes);
        Assert.Equal(48, t.Notes[0].DurationTicks);
    }

    [Fact]
    public void State_Commands_Reach_The_Notes()
    {
        var t = Decode(ChannelKind.Fm, 0xE9, 0x50, 0xEB, 0x03, 0xEC, 0x70, 0xE6, 0x02, 0xE7, 0x02, 0x40, 0x0C, 0xF3, 0x00, 0x00);
        var n = Assert.Single(t.Notes);
        Assert.Equal((3, 0x70, 2, 10), (n.Voice, n.Volume, n.Detune, n.DurationTicks));
        Assert.Equal(new TempoChange(0, 0x50), Assert.Single(t.Tempos));
        Assert.True(t.Loops);
    }

    [Fact]
    public void Ssg_Mixer_Flags_And_Sweep()
    {
        var t = Decode(ChannelKind.Ssg, 0xEB, 0x01, 0xEF, 0x14, 0x40, 0x04, 0xEB, 0x02, 0xF2, 0x10, 0x06, 0xD5, 0xFE, 0xFF);
        Assert.True(t.Notes[0].Noise);
        Assert.Equal(0x14, t.Notes[0].NoisePeriod);
        Assert.True(t.Notes[1].Tone);
        Assert.Equal(-299, t.Notes[1].SweepDelta);
        Assert.Equal(10, t.TotalTicks);
    }

    [Fact]
    public void Drum_Track_Skips_Its_Long_Ec_Forms()
    {
        var t = Decode(ChannelKind.Drum, 0xEC, 0x3D, 1, 2, 3, 4, 5, 6, 0xEC, 0x83, 0x0C, 0xEB, 0x04, 0x40, 0x10, 0xFA, 0x89, 0x00, 0xFF);
        var n = Assert.Single(t.Notes);
        Assert.Equal(4, n.DrumId);
        Assert.Equal([0xFA], t.Ignored.Keys);
    }

    [Fact]
    public void Truncated_Or_Endless_Streams_Do_Not_Hang()
    {
        Assert.Empty(Decode(ChannelKind.Fm, 0x40).Notes.Where(n => n.StartTick > 0));
        var loop = Decode(ChannelKind.Fm, 0xE4, 0x00, 0x40, 0x01, 0xE5); // count 0 is treated as 1
        Assert.Single(loop.Notes);
    }

    [Fact]
    public void FmVoice_Parses_The_Record()
    {
        var rec = new byte[32];
        byte[] body = [0x78, 0xED, 0x00, 0x52, 0x12, 0x13, 0x34, 0x14, 0x02, 0x0F, 0x04, 0x9A, 0xDE, 0x5A, 0x5A, 0x06, 0x05, 0x04, 0x06, 0, 0, 0, 0, 0x84, 0x24, 0xF4, 0x24, 0x80];
        body.CopyTo(rec, 3);
        var v = FmVoice.Parse(rec);
        Assert.Equal((5, 5, true, true), (v.Algorithm, v.Feedback, v.Left, v.Right));
        Assert.Equal(2, v.Operators[0].Multiple);
        Assert.Equal(0x14, v.Operators[0].TotalLevel);
        Assert.Equal(0x1A, v.Operators[0].Attack);
        Assert.Equal(8, v.Operators[0].SustainLevel);
        Assert.False(v.IsEmpty);
        Assert.True(FmVoice.Parse(new byte[32]).IsEmpty);
    }

    [Fact]
    public void Tempo_Map_Accumulates_Segments()
    {
        var map = new[] { new TempoChange(0, 60), new TempoChange(96, 120) };
        double slow = TempoMap.ToSeconds(map, 96, 1.0), fast = TempoMap.ToSeconds(map, 192, 1.0) - slow;
        Assert.Equal(2 * fast, slow, 6);
    }

    static double DominantFrequency(float[] s, int from, int count)
    {
        double best = 0, bestF = 0;
        for (double f = 100; f < 2000; f += 1)
        {
            double re = 0, im = 0;
            for (int i = 0; i < count; i++)
            {
                double a = 2 * Math.PI * f * i / NoteSynth.SampleRate;
                re += s[from + i] * Math.Cos(a); im += s[from + i] * Math.Sin(a);
            }
            double p = re * re + im * im;
            if (p > best) { best = p; bestF = f; }
        }
        return bestF;
    }

    [Fact]
    public void Ssg_Note_Has_The_Requested_Pitch()
    {
        var buf = new StereoBuffer(NoteSynth.SampleRate);
        NoteSynth.Ssg(buf, 0, NoteSynth.Frequency(4 * 12 + 9), NoteSynth.SampleRate / 2, 0.2, true, false, 0);
        Assert.InRange(DominantFrequency(buf.Left, 1000, 8000), 438, 442);
    }

    [Fact]
    public void Fm_Carrier_Has_The_Requested_Pitch_And_Releases()
    {
        // Algorithm 7, only operator 1 audible, multiple 1, instant attack, no decay.
        var rec = new byte[32];
        rec[4] = 0xC7;
        rec[6] = 1; rec[7] = 1; rec[8] = 1; rec[9] = 1;
        rec[10] = 0; rec[11] = 127; rec[12] = 127; rec[13] = 127;
        for (int i = 0; i < 4; i++) { rec[14 + i] = 0x1F; rec[26 + i] = 0x0F; }
        var voice = FmVoice.Parse(rec);
        var buf = new StereoBuffer(NoteSynth.SampleRate * 3);
        NoteSynth.Fm(buf, 0, voice, 440, NoteSynth.SampleRate, 1.0);
        Assert.InRange(DominantFrequency(buf.Left, 4000, 8000), 438, 442);
        double tail = buf.Left.Skip(NoteSynth.SampleRate + 8000).Take(2000).Select(Math.Abs).Max();
        Assert.True(tail < 0.01, $"release tail {tail}");
        Assert.All(buf.Left, v => Assert.False(float.IsNaN(v)));
    }

    [Fact]
    public void Renderer_Mixes_A_Synthetic_Song()
    {
        // header only needs: track table, voice fields. Build the smallest valid MDT by hand.
        var d = new byte[0x40 + 4 + 12 + 1 + 64];
        BitConverter.GetBytes((ushort)0x0302).CopyTo(d, 0);
        int trackAt = 0x44, endAt = 0x44 + 12, voiceAt = endAt + 1;
        BitConverter.GetBytes((ushort)0x80).CopyTo(d, 4); BitConverter.GetBytes((ushort)trackAt).CopyTo(d, 6);
        BitConverter.GetBytes((ushort)0x10).CopyTo(d, 8); BitConverter.GetBytes((ushort)endAt).CopyTo(d, 10);
        BitConverter.GetBytes((ushort)voiceAt).CopyTo(d, 0x3A); BitConverter.GetBytes((ushort)(voiceAt + 64)).CopyTo(d, 0x3C);
        byte[] track = [0xE9, 0x48, 0xEB, 0x01, 0xEC, 0x70, 0x49, 0x18, 0x90, 0x18, 0xFF];
        track.CopyTo(d, trackAt);
        d[endAt] = 0xFF;
        d[voiceAt + 32 + 4] = 0xC7;
        for (int i = 0; i < 4; i++) { d[voiceAt + 32 + 6 + i] = 1; d[voiceAt + 32 + 14 + i] = 0x1F; d[voiceAt + 32 + 26 + i] = 0x0F; }
        var song = MdrvSong.Load(d);
        Assert.Single(song.Tracks);
        Assert.Equal(2, song.Voices.Count);
        var pcm = MdrvRenderer.ToPcm16(MdrvRenderer.RenderSong(song));
        Assert.Contains(pcm, s => Math.Abs(s) > 500);
        var wav = WavWriter.ToBytes(pcm);
        Assert.Equal("RIFF", System.Text.Encoding.ASCII.GetString(wav, 0, 4));
        Assert.Equal(44 + pcm.Length * 2, wav.Length);
    }

    [Fact]
    public void Catalog_Ids_Fit_The_Bank()
    {
        Assert.All(SoundEffectCatalog.UsedByCore.Keys, id => Assert.InRange(id, 1, SoundEffectCatalog.EffectCount));
    }

    // ---- With the real game files (skipped without TH01_DATA_PATH) ----

    static string? DataFolder()
    {
        string? f = GameDataConfig.Resolve();
        return f is not null && Directory.Exists(f) ? f : null;
    }

    [SkippableFact]
    public void RealFiles_AllTracksDecodeAndRenderAudibly()
    {
        string? folder = DataFolder();
        Skip.If(folder is null, $"Game data not configured. Set {GameDataConfig.EnvVar}.");
        string? outDir = Environment.GetEnvironmentVariable("TH01_AUDIO_OUT");
        var report = new System.Text.StringBuilder();
        foreach (string f in Directory.GetFiles(folder!, "*.MDT").OrderBy(x => x))
        {
            var song = MdrvSong.Load(File.ReadAllBytes(f));
            var buf = MdrvRenderer.RenderSong(song, 20);
            var pcm = MdrvRenderer.ToPcm16(buf);
            int peak = pcm.Max(s => Math.Abs((int)s));
            string unknown = string.Join(",", song.Tracks.SelectMany(t => t.Ignored.Keys).Distinct().OrderBy(x => x).Select(x => x.ToString("X2")));
            report.AppendLine($"{Path.GetFileName(f),-12} tracks={song.Tracks.Count} notes={song.Tracks.Sum(t => t.Notes.Count),5} len={song.TickToSeconds(song.TotalTicks),6:F1}s peak={peak,5} ignored=[{unknown}]");
            // INIT.MDT is a silent placeholder (a single rest).
            if (song.Tracks.Sum(t => t.Notes.Count) > 0) Assert.True(peak > 1000, $"{f} is (almost) silent");
            else Assert.Equal(0, peak);
            Assert.DoesNotContain(buf.Left, float.IsNaN);
            if (outDir is not null) { Directory.CreateDirectory(outDir); WavWriter.Save(Path.Combine(outDir, Path.GetFileNameWithoutExtension(f) + ".wav"), pcm); }
        }
        if (outDir is not null) File.WriteAllText(Path.Combine(outDir, "report.txt"), report.ToString());
    }

    [SkippableFact]
    public void RealFiles_SoundEffectsDecodeAndRender()
    {
        string? folder = DataFolder();
        Skip.If(folder is null, $"Game data not configured. Set {GameDataConfig.EnvVar}.");
        string path = Path.Combine(folder!, "ZIGOKU.MDE");
        Skip.If(!File.Exists(path), "ZIGOKU.MDE not found.");
        var bank = SoundEffectBank.Load(File.ReadAllBytes(path));
        Assert.Equal(17, bank.Effects.Count);
        string? outDir = Environment.GetEnvironmentVariable("TH01_AUDIO_OUT");
        var report = new System.Text.StringBuilder();
        foreach (var e in bank.Effects)
        {
            var pcm = MdrvRenderer.ToPcm16(MdrvRenderer.RenderEffect(e));
            int peak = pcm.Length == 0 ? 0 : pcm.Max(s => Math.Abs((int)s));
            report.AppendLine($"se{e.Index + 1,2} parts={e.Parts.Count} notes={e.Parts.Sum(p => p.Notes.Count)} ticks={e.Parts.Max(p => p.TotalTicks)} peak={peak}");
            if (outDir is not null) { Directory.CreateDirectory(outDir); WavWriter.Save(Path.Combine(outDir, $"se{e.Index + 1:D2}.wav"), pcm); }
        }
        if (outDir is not null) File.WriteAllText(Path.Combine(outDir, "se-report.txt"), report.ToString());
        Assert.NotNull(bank.ForSeId(17));
        Assert.Null(bank.ForSeId(0));
        Assert.Null(bank.ForSeId(18));
    }
}
