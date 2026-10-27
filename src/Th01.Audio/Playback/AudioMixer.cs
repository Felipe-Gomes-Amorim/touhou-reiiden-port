using Th01.Audio.Synth;

namespace Th01.Audio.Playback;

/// <summary>
/// Real-time mixer: one looping music stream plus a few one-shot effects, pulled by the audio device in blocks.
/// Engine-free: the platform only has to call <see cref="Mix"/> and send the samples to the sound card.
/// All public members are thread-safe (the game thread starts sounds, the audio thread mixes).
/// </summary>
public sealed class AudioMixer
{
    public const int SampleRate = NoteSynth.SampleRate, Channels = 2, MaxEffects = 8;
    const float MusicGain = 0.8f, EffectGain = 0.6f;

    /// <summary>Player volume for music and for effects, 0..1 on top of the fixed gains above (the launcher's two sliders).</summary>
    public float MusicLevel { get => _musicLevel; set => _musicLevel = Math.Clamp(value, 0f, 1f); }
    public float EffectLevel { get => _effectLevel; set => _effectLevel = Math.Clamp(value, 0f, 1f); }
    volatile float _musicLevel = 1f, _effectLevel = 1f;

    sealed class Voice(int id, short[] pcm) { public readonly int Id = id; public readonly short[] Pcm = pcm; public int Pos; }

    /// <summary>A music stream being played; <see cref="Step"/> is the volume change per frame (a fade), 0 when steady.</summary>
    sealed class Stream(LoopedPcm pcm, float volume, float step)
    {
        public readonly LoopedPcm Pcm = pcm;
        public int Pos;
        public float Volume = volume, Step = step;
    }

    const int MaxFading = 3;

    readonly object _lock = new();
    readonly List<Voice> _voices = [];
    Stream? _music;                       // the current track
    readonly List<Stream> _fading = [];   // earlier tracks on their way out

    public bool MusicPlaying { get { lock (_lock) return _music is not null || _fading.Count > 0; } }

    /// <summary>
    /// Starts a music stream from its beginning. The track that was playing is dropped, or faded out over
    /// <paramref name="fadeOutOld"/> seconds while the new one fades in over <paramref name="fadeInNew"/> (a crossfade).
    /// </summary>
    public void PlayMusic(LoopedPcm music, double fadeOutOld = 0, double fadeInNew = 0)
    {
        lock (_lock)
        {
            if (_music is { } old && fadeOutOld > 0)
            {
                if (old.Step >= 0) old.Step = Ramp(-old.Volume, fadeOutOld);
                _fading.Add(old);
                while (_fading.Count > MaxFading) _fading.RemoveAt(0);
            }
            _music = fadeInNew > 0 ? new Stream(music, 0f, Ramp(1f, fadeInNew)) : new Stream(music, 1f, 0f);
        }
    }

    public void StopMusic()
    {
        lock (_lock) { _music = null; _fading.Clear(); }
    }

    /// <summary>Fades the current music out over <paramref name="seconds"/> and stops it.</summary>
    public void FadeOutMusic(double seconds)
    {
        lock (_lock)
        {
            if (_music is not { } m) return;
            m.Step = Ramp(-m.Volume, seconds);
            _fading.Add(m);
            _music = null;
            while (_fading.Count > MaxFading) _fading.RemoveAt(0);
        }
    }

    static float Ramp(float delta, double seconds) => (float)(delta / Math.Max(1, seconds * SampleRate));

    /// <summary>Plays an effect (interleaved stereo); the same id restarts, at most <see cref="MaxEffects"/> at once.</summary>
    public void PlayEffect(int id, short[] pcm)
    {
        if (pcm.Length == 0) return;
        lock (_lock)
        {
            _voices.RemoveAll(v => v.Id == id);
            if (_voices.Count >= MaxEffects) _voices.RemoveAt(0);
            _voices.Add(new Voice(id, pcm));
        }
    }

    /// <summary>Fills <paramref name="output"/> (interleaved 16-bit stereo, length a multiple of 2) with the next samples.</summary>
    public void Mix(Span<short> output)
    {
        lock (_lock)
        {
            for (int i = 0; i + 1 < output.Length; i += 2)
            {
                float l = 0, r = 0;
                float musicGain = MusicGain * _musicLevel, effectGain = EffectGain * _effectLevel;
                if (_music is { } m && !Advance(m, musicGain, ref l, ref r)) _music = null;
                for (int f = _fading.Count - 1; f >= 0; f--)
                    if (!Advance(_fading[f], musicGain, ref l, ref r)) _fading.RemoveAt(f);
                for (int v = _voices.Count - 1; v >= 0; v--)
                {
                    var voice = _voices[v];
                    l += voice.Pcm[voice.Pos] * effectGain;
                    r += voice.Pcm[voice.Pos + 1] * effectGain;
                    voice.Pos += 2;
                    if (voice.Pos + 1 >= voice.Pcm.Length) _voices.RemoveAt(v);
                }
                output[i] = Clip(l);
                output[i + 1] = Clip(r);
            }
        }
    }

    /// <summary>Adds the next frame of a stream to l/r; false when the stream is over (ended, or faded out).</summary>
    static bool Advance(Stream st, float gain, ref float l, ref float r)
    {
        var m = st.Pcm;
        if (st.Pos >= m.EndFrame)
        {
            if (!m.Loops) return false;
            st.Pos = m.LoopStartFrame;
        }
        if (st.Pos >= m.ReadyFrames) return true;   // still being rendered: wait without advancing
        l += m.Pcm[st.Pos * 2] * st.Volume * gain;
        r += m.Pcm[st.Pos * 2 + 1] * st.Volume * gain;
        st.Pos++;
        if (st.Step != 0)
        {
            st.Volume += st.Step;
            if (st.Step < 0 && st.Volume <= 0) return false;
            if (st.Step > 0 && st.Volume >= 1) { st.Volume = 1; st.Step = 0; }
        }
        return true;
    }

    static short Clip(float x) => (short)Math.Clamp((int)x, short.MinValue, short.MaxValue);
}
