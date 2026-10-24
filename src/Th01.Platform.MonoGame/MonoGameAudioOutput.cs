using Microsoft.Xna.Framework.Audio;
using Th01.Audio.Playback;

namespace Th01.Platform.MonoGame;

/// <summary>
/// Sends the samples of an <see cref="AudioMixer"/> to the sound card through a <see cref="DynamicSoundEffectInstance"/>.
/// Call <see cref="Update"/> once per frame from the game thread (it keeps a few short buffers queued).
/// If no audio device exists (or it fails) the output disables itself and the game stays silent.
/// </summary>
public sealed class MonoGameAudioOutput : IDisposable
{
    const int BlockFrames = 1024, QueuedBlocks = 4;

    readonly AudioMixer _mixer;
    readonly short[] _samples = new short[BlockFrames * AudioMixer.Channels];
    readonly byte[] _bytes = new byte[BlockFrames * AudioMixer.Channels * sizeof(short)];
    DynamicSoundEffectInstance? _instance;
    bool _failed;

    public MonoGameAudioOutput(AudioMixer mixer) { _mixer = mixer; }

    public void Update()
    {
        if (_failed) return;
        try
        {
            if (_instance is null)
            {
                _instance = new DynamicSoundEffectInstance(AudioMixer.SampleRate, AudioChannels.Stereo);
                _instance.Play();
            }
            while (_instance.PendingBufferCount < QueuedBlocks)
            {
                _mixer.Mix(_samples);
                Buffer.BlockCopy(_samples, 0, _bytes, 0, _bytes.Length);
                _instance.SubmitBuffer(_bytes);
            }
        }
        catch (Exception e) when (e is NoAudioHardwareException or InvalidOperationException or NullReferenceException or DllNotFoundException)
        {
            _failed = true; // no sound device: stay silent
            _instance?.Dispose();
            _instance = null;
        }
    }

    public void Dispose() { _instance?.Dispose(); _instance = null; }
}
