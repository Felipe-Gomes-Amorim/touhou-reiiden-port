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
        Assert.Equal((3, 0x70, 2, 12), (n.Voice, n.Volume, n.Detune, n.DurationTicks));
        Assert.Equal(4 * 12 + 2, n.Semitone);   // E7 02 transposes up two semitones
        Assert.Equal(new TempoChange(0, 0x50), Assert.Single(t.Tempos));
        Assert.True(t.Loops);
    }

    [SkippableFact]
    public void RealSongs_Transposed_Notes_Stay_In_Range_And_Only_Lfo_Commands_Are_Left_Ignored()
    {
        string? folder = GameDataConfig.Resolve();
        Skip.If(folder is null || !Directory.Exists(folder), $"Game data not configured. Set {GameDataConfig.EnvVar}.");
        int songs = 0;
        foreach (string file in Directory.GetFiles(folder!, "*.MDT"))
        {
            var song = MdrvSong.Load(File.ReadAllBytes(file));
            songs++;
            foreach (var t in song.Tracks)
            {
                if (t.Kind != ChannelKind.Drum)
                    Assert.All(t.Notes, n => Assert.InRange(n.Semitone, 0, 8 * 12 + 11));   // octaves 0..8
                Assert.All(t.Ignored.Keys, op => Assert.Contains(op, new[] { 0xED, 0xE8, 0xFC }));
            }
        }
        Skip.If(songs == 0, "No .MDT files found.");
    }

    [Fact]
    public void Transposition_Moves_Pitch_Across_Octaves_And_Not_The_Length()
    {
        var up = Decode(ChannelKind.Fm, 0xE7, 0x03, 0x4A, 0x0C, 0xFF);        // note 0xA + 3 = octave 5, note 1
        Assert.Equal(5 * 12 + 1, up.Notes[0].Semitone);
        Assert.Equal(12, up.Notes[0].DurationTicks);
        var down = Decode(ChannelKind.Fm, 0xE7, 0xFE, 0x41, 0x0C, 0xFF);      // 0xFE = -2: octave 4, note 1 -> octave 3, note B
        Assert.Equal(3 * 12 + 11, down.Notes[0].Semitone);
        var low = Decode(ChannelKind.Fm, 0xE7, 0xF0, 0x01, 0x0C, 0xFF);       // never below zero
        Assert.Equal(0, low.Notes[0].Semitone);
    }

    [Fact]
    public void Transposition_Applies_To_Ssg_But_Not_To_The_Drum_Track()
    {
        var ssg = Decode(ChannelKind.Ssg, 0xE7, 0x02, 0x40, 0x0C, 0xFF);
        Assert.Equal(50, ssg.Notes[0].Semitone);
        var drum = Decode(ChannelKind.Drum, 0xE7, 0x02, 0x40, 0x0C, 0xFF);
        Assert.Equal(48, drum.Notes[0].Semitone);
    }

    [Fact]
    public void Tied_Notes_Of_A_Transposed_Track_Still_Merge()
    {
        var t = Decode(ChannelKind.Fm, 0xE7, 0x02, 0x50, 0x18, 0x91, 0x50, 0x18, 0xFF);
        Assert.Single(t.Notes);
        Assert.Equal(48, t.Notes[0].DurationTicks);
    }

    [Fact]
    public void Relative_Volume_Changes_And_Is_Clamped()
    {
        var fm = Decode(ChannelKind.Fm, 0xEC, 0x70, 0xF4, 0x08, 0x40, 0x0C, 0xF5, 0x20, 0x40, 0x0C, 0xF4, 0xFF, 0x40, 0x0C, 0xF5, 0xFF, 0x40, 0x0C, 0xFF);
        Assert.Equal([0x78, 0x58, 127, 0], fm.Notes.Select(n => n.Volume));
        var ssg = Decode(ChannelKind.Ssg, 0xEC, 0x0A, 0xF4, 0x03, 0x40, 0x0C, 0xF4, 0x09, 0x40, 0x0C, 0xF5, 0x10, 0x40, 0x0C, 0xFF);
        Assert.Equal([13, 15, 0], ssg.Notes.Select(n => n.Volume));
        Assert.DoesNotContain(0xF4, fm.Ignored.Keys);
        Assert.DoesNotContain(0xF5, fm.Ignored.Keys);
    }

    [Fact]
    public void Pan_Command_Reaches_The_Notes_And_Defaults_To_Both_Sides()
    {
        var t = Decode(ChannelKind.Fm, 0x40, 0x0C, 0xF1, 0x01, 0x40, 0x0C, 0xF1, 0x02, 0x40, 0x0C, 0xF1, 0x03, 0x40, 0x0C, 0xFF);
        Assert.Equal([3, 1, 2, 3], t.Notes.Select(n => n.Pan));
        Assert.DoesNotContain(0xF1, t.Ignored.Keys);
    }

    [Fact]
    public void Fm_Note_Pan_Comes_From_The_Track_Not_From_The_Voice()
    {
        byte[] rec = new byte[32];
        rec[4] = 0x80 | 7;                         // the voice says "left only", algorithm 7
        for (int i = 0; i < 4; i++) { rec[6 + i] = 1; rec[14 + i] = 0x1F; rec[26 + i] = 0x0F; }
        var voice = FmVoice.Parse(rec);
        var buf = new StereoBuffer(NoteSynth.SampleRate);
        NoteSynth.Fm(buf, 0, voice, 440, NoteSynth.SampleRate / 2, 1.0, pan: 2);   // track says right only
        Assert.True(buf.Right.Max(Math.Abs) > 0.01f);
        Assert.Equal(0f, buf.Left.Max(Math.Abs));
    }

    [Fact]
    public void SsgVoiceByte_Chooses_Mix_And_Envelope_Preset()
    {
        var t = Decode(ChannelKind.Ssg,
            0xEB, 0x81, 0x40, 0x0C,    // tone only, preset 1
            0xEB, 0x42, 0x40, 0x0C,    // noise only, preset 2
            0xEB, 0x00, 0x40, 0x0C,    // tone and noise, preset 0
            0xEB, 0xC3, 0x40, 0x0C,    // top bits set: nothing sounds, no envelope
            0xFF);
        Assert.Equal([(true, false, 1), (false, true, 2), (true, true, 0), (false, false, -1)],
            t.Notes.Select(n => (n.Tone, n.Noise, n.EnvelopePreset)));
    }

    [Theory]
    [InlineData(0, 0, 32.7)]      // octave 0, C
    [InlineData(9, 0, 55.0)]      // octave 0, A
    [InlineData(4 * 12 + 9, 0, 880.0)]    // an SSG note sounds one octave above the FM note with the same octave digit
    [InlineData(6 * 12, 0, 2115.3)]       // period 3816 >> 6 = 59
    public void SsgFrequency_Follows_The_Chip_Periods(int semitone, int detune, double expected)
    {
        Assert.InRange(NoteSynth.SsgFrequency(semitone, detune), expected * 0.99, expected * 1.01);
    }

    [Fact]
    public void SsgFrequency_Subtracts_The_Detune_From_The_Period()
    {
        double plain = NoteSynth.SsgFrequency(6 * 12), up = NoteSynth.SsgFrequency(6 * 12, 4), down = NoteSynth.SsgFrequency(6 * 12, -4);
        Assert.Equal(124800.0 / 59, plain, 6);
        Assert.Equal(124800.0 / 55, up, 6);
        Assert.Equal(124800.0 / 63, down, 6);
    }

    [Fact]
    public void Envelope_Rate_Zero_Holds_The_Level_And_Never_Releases()
    {
        // The preset most effects use: full level, nothing to decay, no sustain fade and no release.
        var preset = new SsgEnvelopePreset(255, 255, 0, 255, 0, 0);
        var levels = SsgEnvelope.Levels(preset, gateTicks: 6, totalTicks: 400);
        Assert.All(levels, l => Assert.Equal(255, l));
    }

    [Fact]
    public void Envelope_Rate_Zero_Stage_Already_At_Its_Target_Is_Passed_On_Next_Tick()
    {
        // Decay rate 0 with the sustain level equal to the level reached: the stage counts as done, so the sustain fade starts.
        var levels = SsgEnvelope.Levels(new SsgEnvelopePreset(255, 255, 0, 255, 5, 5), gateTicks: 1000, totalTicks: 100);
        Assert.Equal(255, levels[0]);
        Assert.True(levels[60] < levels[2]);
    }

    [Fact]
    public void Note_Span_Reaches_The_Next_Note_Through_Rests()
    {
        var t = Decode(ChannelKind.Ssg, 0x40, 0x0C, 0x90, 0x0C, 0x42, 0x0C, 0xFF);
        Assert.Equal([24, 0], t.Notes.Select(n => n.SpanTicks));
    }

    [Fact]
    public void Envelope_Attack_Decay_Sustain_And_Release()
    {
        var p = new SsgEnvelopePreset(Initial: 0, Attack: 100, Decay: 16, Sustain: 128, SustainRate: 0, Release: 20);
        var l = SsgEnvelope.Levels(p, gateTicks: 20, totalTicks: 30);
        Assert.Equal([100, 200, 255], l.Take(3));                       // attack: 300 is past full level
        Assert.Equal(239, l[3]);                                        // decay: 16 per tick ...
        Assert.Equal(128, l[10]);                                       // ... until the sustain level
        Assert.All(l.Skip(10).Take(10), v => Assert.Equal(128, v));     // no sustain fade: it holds
        Assert.Equal([108, 88, 68, 48, 28, 8, 0], l.Skip(20).Take(7));  // release from the key-off tick
        Assert.All(l.Skip(27), v => Assert.Equal(0, v));
    }

    [Fact]
    public void Envelope_Sustain_Fades_And_Instant_Presets_Hold_Full_Level()
    {
        var fade = SsgEnvelope.Levels(new SsgEnvelopePreset(255, 255, 255, 255, 5, 5), 1000, 60);
        Assert.Equal(255, fade[0]);
        Assert.Equal(255, fade[1]);                    // each stage that is already at its target costs one tick
        Assert.Equal(250, fade[2]);                    // then the sustain fades 5 per tick
        Assert.NotEqual(0, fade[51]);
        Assert.Equal(0, fade[52]);                     // 255 / 5 = 51 steps
        var organ = SsgEnvelope.Levels(new SsgEnvelopePreset(255, 255, 255, 255, 0, 255), 10, 14);
        Assert.All(organ.Take(10), v => Assert.Equal(255, v));
        Assert.All(organ.Skip(10), v => Assert.Equal(0, v));   // release of 255 per tick = cut
        var stuck = SsgEnvelope.Levels(new SsgEnvelopePreset(255, 255, 255, 255, 0, 0), 5, 20);
        Assert.Equal(255, stuck[19]);                  // a release rate of 0 never gets anywhere
    }

    [Fact]
    public void Envelope_Output_Level_Is_Volume_Times_Envelope()
    {
        Assert.Equal(13, SsgEnvelope.OutputLevel(13, 255));
        Assert.Equal(15, SsgEnvelope.OutputLevel(15, 255));
        Assert.Equal(0, SsgEnvelope.OutputLevel(15, 0));
        Assert.Equal(8, SsgEnvelope.OutputLevel(15, 128));
        Assert.Equal(15, SsgEnvelope.OutputLevel(200, 255));   // never above the chip's top level
    }

    [Fact]
    public void Ssg_Note_Follows_Tick_Gains()
    {
        var buf = new StereoBuffer(NoteSynth.SampleRate);
        double[] gains = [1.0, 1.0, 0.0, 0.0];
        NoteSynth.Ssg(buf, 0, 440, 0, 1.0, tone: true, noise: false, noisePeriod: 0, tickGains: gains, framesPerTick: 1000);
        double Peak(int from, int to) { double m = 0; for (int i = from; i < to; i++) m = Math.Max(m, Math.Abs(buf.Left[i])); return m; }
        Assert.True(Peak(100, 1900) > 0.5);
        Assert.Equal(0, Peak(2000, 4000));
        Assert.Equal(0, Peak(4000, 8000));   // the note ends with the envelope
    }

    [SkippableFact]
    public void RealSongs_Have_Presets_For_Every_Envelope_They_Select()
    {
        string? folder = GameDataConfig.Resolve();
        Skip.If(folder is null || !Directory.Exists(folder), $"Game data not configured. Set {GameDataConfig.EnvVar}.");
        foreach (string file in Directory.GetFiles(folder!, "*.MDT"))
        {
            var song = MdrvSong.Load(File.ReadAllBytes(file));
            foreach (var n in song.Tracks.Where(t => t.Kind == ChannelKind.Ssg).SelectMany(t => t.Notes))
                if (n.EnvelopePreset >= 0) Assert.True(n.EnvelopePreset < song.SsgPresets.Count, $"{Path.GetFileName(file)} preset {n.EnvelopePreset}");
        }
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
        Assert.Empty(t.Ignored);   // FA with an offset outside the data is skipped without a call
    }

    [Fact]
    public void F6_F7_Repeat_With_16_Bit_Offsets()
    {
        // F6 n lo hi .. F7 x lo hi: the body plays n times; the offsets are not needed to decode
        var t = Decode(ChannelKind.Fm, 0xF6, 0x03, 0x0A, 0x00, 0x40, 0x04, 0xF7, 0x00, 0xF4, 0xFF, 0x41, 0x04, 0xFF);
        Assert.Equal([0, 4, 8, 12], t.Notes.Select(n => n.StartTick));
        Assert.Equal([0, 0, 0], t.Notes.Take(3).Select(n => n.Semitone - 48));   // the body three times
        Assert.Equal(1, t.Notes[3].Semitone - 48);                                // then the note after F7
        Assert.Empty(t.Ignored);
    }

    [Fact]
    public void F9_Skips_The_Rest_Of_The_Body_On_The_Last_Pass()
    {
        // F6 2 [A  F9 -> F7  B] F7 : A B A  (the offset counts from the address of F9's second byte)
        byte[] bytes = [0xF6, 0x02, 0x00, 0x00, 0x40, 0x04, 0xF9, 0x03, 0x00, 0x41, 0x04, 0xF7, 0x00, 0x00, 0x00, 0xFF];
        var t = Decode(ChannelKind.Fm, bytes);
        Assert.Equal([0, 1, 0], t.Notes.Select(n => n.Semitone - 48));
    }

    [Fact]
    public void FA_Calls_A_Pattern_Stored_Outside_The_Track()
    {
        // track = FA <7>, 41 04, FF ; pattern at 7 = 40 06, FF
        byte[] bytes = [0xFA, 0x07, 0x00, 0x41, 0x04, 0xFF, 0x00, 0x40, 0x06, 0xFF];
        var t = TrackDecoder.Decode(bytes, 0, 6, 0x42, ChannelKind.Drum);
        Assert.Equal([48 + 0, 48 + 1], t.Notes.Select(n => n.Semitone));
        Assert.Equal([0, 6], t.Notes.Select(n => n.StartTick));
        Assert.Equal(10, t.TotalTicks);
    }

    [Fact]
    public void Q_Shortens_The_Sounding_Part_Of_A_Note_But_Not_Its_Place()
    {
        var t = Decode(ChannelKind.Fm, 0xEA, 0x04, 0x40, 0x10, 0x41, 0x10, 0xFF);
        Assert.Equal([8, 8], t.Notes.Select(n => n.DurationTicks));
        Assert.Equal([0, 16], t.Notes.Select(n => n.StartTick));
        Assert.Equal(32, t.TotalTicks);
    }

    [Fact]
    public void E8_And_FC_Take_Four_Parameters()
    {
        // their bytes look like notes (0x14 0x0A, 0x3C 0x02) and must not become ticks
        var t = Decode(ChannelKind.Ssg, 0xE8, 0x14, 0x0A, 0x01, 0x00, 0xFC, 0x3C, 0x02, 0x05, 0x00, 0x40, 0x06, 0xFF);
        Assert.Equal(6, t.TotalTicks);
        Assert.Single(t.Notes);
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
    public void Volume_Curves_Follow_The_Chip_Steps()
    {
        Assert.Equal(1.0, NoteSynth.FmVolumeGain(127), 9);
        Assert.Equal(Math.Pow(10, -3 / 20.0), NoteSynth.FmVolumeGain(123), 9);      // 4 steps of 0.75 dB
        Assert.Equal(NoteSynth.FmVolumeGain(127), NoteSynth.FmVolumeGain(200), 9);  // out of range is clamped
        Assert.True(NoteSynth.FmVolumeGain(0) < 1e-4);
        Assert.Equal(1.0, NoteSynth.SsgVolumeGain(15), 9);
        Assert.Equal(Math.Pow(10, -3 / 20.0), NoteSynth.SsgVolumeGain(14), 9);      // 3 dB per level
        Assert.Equal(0.0, NoteSynth.SsgVolumeGain(0));
        for (int v = 1; v < 127; v++) Assert.True(NoteSynth.FmVolumeGain(v) < NoteSynth.FmVolumeGain(v + 1));
    }

    [Fact]
    public void Tempo_Map_Accumulates_Segments()
    {
        var map = new[] { new TempoChange(0, 60), new TempoChange(96, 120) };
        double slow = TempoMap.ToSeconds(map, 96), fast = TempoMap.ToSeconds(map, 192) - slow;
        Assert.Equal(2 * fast, slow, 6);
    }

    [Theory]
    [InlineData(0x48, 60)]    // 4340 / 72 = 60.27 -> 60 timer steps per tick
    [InlineData(0x41, 66)]    // 4340 / 65 = 66.77 -> 66
    [InlineData(0x11, 255)]   // 4340 / 17 = 255.3 -> 255
    [InlineData(0x10, 255)]   // below 17 the timer is set to its slowest
    [InlineData(0x00, 255)]
    [InlineData(0xFF, 17)]    // 4340 / 255 = 17.02 -> 17
    public void Tempo_Is_Whole_Timer_Steps_Per_Tick(int tempo, int steps)
    {
        Assert.Equal(steps * TempoMap.TimerStepSeconds, TempoMap.SecondsPerTick(tempo), 12);
    }

    [Fact]
    public void Tempo_Byte_Is_Quarter_Notes_Per_Minute_Within_The_Timer_Resolution()
    {
        for (int tempo = 0x30; tempo <= 0xA0; tempo++)
        {
            double quarter = 48 * TempoMap.SecondsPerTick(tempo);
            Assert.InRange(60.0 / quarter / tempo, 0.997, 1.003 + tempo / 4340.0);   // dropping the fraction of 4340/tempo speeds it up by up to 1/steps
        }
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
        // An effect is silenced the moment its stream ends: no release tail, whatever the envelope would still do.
        foreach (var e in bank.Effects)
        {
            int ticks = e.Parts.Max(p => p.TotalTicks);
            foreach (double spt in new[] { MdrvRenderer.EffectSecondsPerTick, TempoMap.SecondsPerTick(0x5B) })
            {
                var buf = MdrvRenderer.RenderEffect(e, spt);
                Assert.True(buf.Frames <= (ticks * spt + 0.06) * NoteSynth.SampleRate, $"effect {e.Index + 1} is longer than its stream");
                Assert.True(buf.Left.Max(Math.Abs) > 0.001f, $"effect {e.Index + 1} is silent");
            }
        }
        // They tick with the music's timer: a faster song makes them shorter.
        Assert.True(MdrvRenderer.RenderEffect(bank.Effects[4], TempoMap.SecondsPerTick(0x5B)).Frames < MdrvRenderer.RenderEffect(bank.Effects[4], TempoMap.SecondsPerTick(0x41)).Frames);
        Assert.NotNull(bank.ForSeId(17));
        // The effects use the song reading of the voice byte, with the envelope presets stored after the last effect.
        Assert.NotEmpty(bank.Effects[0].Presets);
        Assert.Contains(bank.Effects.SelectMany(e => e.Parts).SelectMany(p => p.Notes), n => n.EnvelopePreset >= 0);
        Assert.Contains(bank.Effects.SelectMany(e => e.Parts).SelectMany(p => p.Notes), n => n.Tone && n.Noise);
        Assert.Null(bank.ForSeId(0));
        Assert.Null(bank.ForSeId(18));
    }
}
