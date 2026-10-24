using Th01.Audio.Mdrv;

namespace Th01.Audio.Synth;

/// <summary>Stereo float accumulator the synths mix into.</summary>
public sealed class StereoBuffer(int frames)
{
    public readonly float[] Left = new float[frames], Right = new float[frames];
    public int Frames => Left.Length;

    public void Add(int frame, float l, float r)
    {
        if ((uint)frame >= (uint)Left.Length) return;
        Left[frame] += l; Right[frame] += r;
    }
}

/// <summary>
/// Offline synths for one note: a 4-operator FM voice (OPN-like), an SSG-like square/noise voice and simple drums.
/// Envelope rates are approximations (the real chip's tables are not used). Output is mixed into a <see cref="StereoBuffer"/>.
/// </summary>
public static class NoteSynth
{
    public const int SampleRate = 44100;
    const double Tau = 2 * Math.PI;

    /// <summary>Equal temperament with octave 4 / note 9 = 440 Hz (ASSUMPTION: octave numbering as in common MML).</summary>
    public static double Frequency(int semitone, double detuneCents = 0) =>
        440.0 * Math.Pow(2, (semitone - (4 * 12 + 9)) / 12.0 + detuneCents / 1200.0);

    const int SineSize = 4096;
    static readonly double[] SineTable = BuildSine();

    static double[] BuildSine()
    {
        var t = new double[SineSize + 1];
        for (int i = 0; i <= SineSize; i++) t[i] = Math.Sin(Tau * i / SineSize);
        return t;
    }

    /// <summary>Sine of a phase in radians (table with linear interpolation; about 4x faster than Math.Sin).</summary>
    static double FastSin(double radians)
    {
        double x = radians * (1.0 / Tau);
        x -= Math.Floor(x);
        double idx = x * SineSize;
        int i = (int)idx;
        double f = idx - i;
        return SineTable[i] + (SineTable[i + 1] - SineTable[i]) * f;
    }

    static double DbToGain(double db) => Math.Pow(10, -db / 20);

    sealed class Env
    {
        enum Stage { Attack, Decay, Sustain, Release, Off }
        Stage _stage = Stage.Attack;
        public double Level;
        readonly double _attackStep, _decayFactor, _sustainFactor, _releaseFactor, _sustainLevel;

        public Env(FmOperator op)
        {
            _attackStep = 1.0 / Math.Max(1, 3.0 * Math.Pow(2, -op.Attack / 3.2) * SampleRate);
            _decayFactor = RateFactor(op.Decay);
            _sustainFactor = RateFactor(op.Sustain);
            _releaseFactor = RateFactor(op.Release * 2 + 1);
            _sustainLevel = op.SustainLevel >= 15 ? DbToGain(93) : DbToGain(op.SustainLevel * 3);
        }

        // Per-sample multiplier that falls 60 dB in 20 s * 2^(-rate/3); rate 0 holds the level.
        static double RateFactor(int rate) =>
            rate <= 0 ? 1.0 : Math.Pow(10, -60.0 / 20 / (20.0 * Math.Pow(2, -rate / 3.0) * SampleRate));

        public void Release() { if (_stage != Stage.Off) _stage = Stage.Release; }

        public double Next()
        {
            switch (_stage)
            {
                case Stage.Attack:
                    Level += _attackStep * (1.2 - Level);
                    if (Level >= 0.999) { Level = 1; _stage = Stage.Decay; }
                    break;
                case Stage.Decay:
                    Level *= _decayFactor;
                    if (Level <= _sustainLevel) { Level = _sustainLevel; _stage = Stage.Sustain; }
                    break;
                case Stage.Sustain: Level *= _sustainFactor; break;
                case Stage.Release:
                    Level *= _releaseFactor;
                    if (Level < 1e-3) { Level = 0; _stage = Stage.Off; }
                    break;
            }
            return Level;
        }

        public bool Done => _stage == Stage.Off;
    }

    /// <summary>Renders an FM note. <paramref name="gateFrames"/> = key-on length, then the release tail is added.</summary>
    public static void Fm(StereoBuffer buf, int startFrame, FmVoice voice, double freq, int gateFrames, double gain)
    {
        var ops = voice.Operators;
        var env = new Env[4];
        var gains = new double[4];
        var ratio = new double[4];
        var phase = new double[4];
        for (int i = 0; i < 4; i++)
        {
            env[i] = new Env(ops[i]);
            gains[i] = DbToGain(ops[i].TotalLevel * 0.75);
            ratio[i] = ops[i].Multiple == 0 ? 0.5 : ops[i].Multiple;
        }
        double fbScale = voice.Feedback == 0 ? 0 : Math.PI * Math.Pow(2, voice.Feedback - 7);
        var e4 = new double[4];
        double s1a = 0, s1b = 0;
        float gl = voice.Left ? 1 : 0, gr = voice.Right ? 1 : 0;
        int maxFrames = gateFrames + SampleRate;
        for (int n = 0; n < maxFrames; n++)
        {
            if (n == gateFrames) foreach (var e in env) e.Release();
            for (int i = 0; i < 4; i++) e4[i] = env[i].Next() * gains[i];
            double Op(int i, double mod) => e4[i] < 1e-6 ? 0 : FastSin(Tau * phase[i] + mod) * e4[i];
            double o1 = e4[0] < 1e-6 ? 0 : FastSin(Tau * phase[0] + fbScale * (s1a + s1b)) * e4[0];
            s1b = s1a; s1a = o1;
            const double M = 4 * Math.PI;
            double o2, o3, o4, outp;
            switch (voice.Algorithm)
            {
                case 0: o2 = Op(1, o1 * M); o3 = Op(2, o2 * M); o4 = Op(3, o3 * M); outp = o4; break;
                case 1: o2 = Op(1, 0); o3 = Op(2, (o1 + o2) * M); o4 = Op(3, o3 * M); outp = o4; break;
                case 2: o2 = Op(1, 0); o3 = Op(2, o2 * M); o4 = Op(3, (o1 + o3) * M); outp = o4; break;
                case 3: o2 = Op(1, o1 * M); o3 = Op(2, 0); o4 = Op(3, (o2 + o3) * M); outp = o4; break;
                case 4: o2 = Op(1, o1 * M); o3 = Op(2, 0); o4 = Op(3, o3 * M); outp = (o2 + o4) * 0.5; break;
                case 5: o2 = Op(1, o1 * M); o3 = Op(2, o1 * M); o4 = Op(3, o1 * M); outp = (o2 + o3 + o4) / 3; break;
                case 6: o2 = Op(1, o1 * M); o3 = Op(2, 0); o4 = Op(3, 0); outp = (o2 + o3 + o4) / 3; break;
                default: o2 = Op(1, 0); o3 = Op(2, 0); o4 = Op(3, 0); outp = (o1 + o2 + o3 + o4) * 0.25; break;
            }
            for (int i = 0; i < 4; i++) phase[i] += freq / SampleRate * ratio[i];
            float s = (float)(outp * gain);
            buf.Add(startFrame + n, s * gl, s * gr);
            if (n >= gateFrames && env[0].Done && env[1].Done && env[2].Done && env[3].Done) break;
        }
    }

    /// <summary>SSG-like voice: square wave and/or noise with a short release. Optional pitch sweep (F-number units per tick).</summary>
    public static void Ssg(StereoBuffer buf, int startFrame, double freq, int gateFrames, double gain, bool tone, bool noise,
                           int noisePeriod, int sweepDelta = 0, double secondsPerTick = 0.02, int semitone = 0)
    {
        double phase = 0;
        uint lfsr = 1;
        double noisePhase = 0, noiseStep = 250000.0 / Math.Max(1, noisePeriod + 1) / SampleRate;
        double noiseVal = 1;
        int release = SampleRate / 100;
        double fnum0 = 617.0 * Math.Pow(2, (semitone % 12) / 12.0);
        for (int n = 0; n < gateFrames + release; n++)
        {
            double f = freq;
            if (sweepDelta != 0)
            {
                double ticks = n / (double)SampleRate / secondsPerTick;
                f = Math.Max(20, freq * (fnum0 + sweepDelta * ticks) / fnum0);
            }
            phase += f / SampleRate;
            phase -= Math.Floor(phase);
            noisePhase += noiseStep;
            if (noisePhase >= 1)
            {
                noisePhase -= 1;
                lfsr = (lfsr >> 1) | (((lfsr ^ (lfsr >> 3)) & 1) << 16);
                noiseVal = (lfsr & 1) != 0 ? 1 : -1;
            }
            double v = 0;
            if (tone) v += phase < 0.5 ? 1 : -1;
            if (noise) v += noiseVal;
            if (tone && noise) v *= 0.5;
            double env = n < 40 ? n / 40.0 : 1;
            if (n >= gateFrames) env *= 1 - (n - gateFrames) / (double)release;
            float s = (float)(v * env * gain);
            buf.Add(startFrame + n, s, s);
        }
    }

    /// <summary>Very simple drum voices. ASSUMPTION: ids 1 kick, 2 snare, 4 crash, others hi-hat (not verified).</summary>
    public static void Drum(StereoBuffer buf, int startFrame, int drumId, double gain)
    {
        uint seed = 12345;
        double Noise() { seed = seed * 1664525u + 1013904223u; return ((seed >> 8) & 0xFFFF) / 32768.0 - 1; }
        int kind = drumId & 0x0F;
        double len = kind switch { 1 => 0.18, 2 => 0.2, 4 => 0.45, 3 => 0.06, _ => 0.05 };
        int frames = (int)(len * SampleRate);
        double ph = 0, prev = 0;
        for (int n = 0; n < frames; n++)
        {
            double t = n / (double)SampleRate, k = Math.Exp(-t / (len / 4));
            double v;
            if (kind == 1) { ph += (45 + 120 * Math.Exp(-t * 30)) / SampleRate; v = Math.Sin(Tau * ph) * k * 1.5; }
            else if (kind == 2) { ph += 190.0 / SampleRate; v = (Noise() * 0.7 + Math.Sin(Tau * ph) * 0.4) * k; }
            else { double x = Noise(); v = (x - prev) * 0.5 * k; prev = x; }
            float s = (float)(v * gain);
            buf.Add(startFrame + n, s, s);
        }
    }
}
