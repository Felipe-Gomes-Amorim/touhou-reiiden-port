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

    sealed class Voice(int id, short[] pcm) { public readonly int Id = id; public readonly short[] Pcm = pcm; public int Pos; }

    readonly object _lock = new();
    readonly List<Voice> _voices = [];
    LoopedPcm? _music;
    int _musicPos;
    float _musicVolume = 1f, _musicVolumeStep;   // step per frame while fading

    public bool MusicPlaying { get { lock (_lock) return _music is not null; } }

    /// <summary>Starts a music stream from its beginning, replacing the current one.</summary>
    public void PlayMusic(LoopedPcm music)
    {
        lock (_lock) { _music = music; _musicPos = 0; _musicVolume = 1f; _musicVolumeStep = 0; }
    }

    public void StopMusic()
    {
        lock (_lock) { _music = null; _musicPos = 0; }
    }

    /// <summary>Fades the music out over <paramref name="seconds"/> and stops it.</summary>
    public void FadeOutMusic(double seconds)
    {
        lock (_lock)
        {
            if (_music is null) return;
            _musicVolumeStep = (float)(-_musicVolume / Math.Max(1, seconds * SampleRate));
        }
    }

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
                if (_music is { } m)
                {
                    if (_musicPos >= m.EndFrame)
                    {
                        if (m.Loops) _musicPos = m.LoopStartFrame; else { _music = null; goto effects; }
                    }
                    if (_musicPos >= m.ReadyFrames) goto effects;   // still being rendered: wait without advancing
                    l = m.Pcm[_musicPos * 2] * _musicVolume * MusicGain;
                    r = m.Pcm[_musicPos * 2 + 1] * _musicVolume * MusicGain;
                    _musicPos++;
                    if (_musicVolumeStep != 0)
                    {
                        _musicVolume += _musicVolumeStep;
                        if (_musicVolume <= 0) { _music = null; _musicVolume = 1f; _musicVolumeStep = 0; }
                    }
                }
            effects:
                for (int v = _voices.Count - 1; v >= 0; v--)
                {
                    var voice = _voices[v];
                    l += voice.Pcm[voice.Pos] * EffectGain;
                    r += voice.Pcm[voice.Pos + 1] * EffectGain;
                    voice.Pos += 2;
                    if (voice.Pos + 1 >= voice.Pcm.Length) _voices.RemoveAt(v);
                }
                output[i] = Clip(l);
                output[i + 1] = Clip(r);
            }
        }
    }

    static short Clip(float x) => (short)Math.Clamp((int)x, short.MinValue, short.MaxValue);
}
