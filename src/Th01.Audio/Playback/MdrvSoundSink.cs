using Th01.Audio.Mdrv;
using Th01.Audio.Synth;
using Th01.Core.Audio;

namespace Th01.Audio.Playback;

/// <summary>
/// <see cref="ISoundSink"/> that plays the game's own MDRV music and effects. Music is rendered in the background
/// (a few seconds per track) and starts when ready; effects are rendered once at construction.
/// The platform pulls samples with <see cref="Mixer"/>.<see cref="AudioMixer.Mix"/>. Missing files make the sink silent, never throw.
/// </summary>
public sealed class MdrvSoundSink : ISoundSink, IDisposable
{
    public const double FadeSeconds = 1.5;

    readonly Func<string, byte[]?> _load;
    readonly Dictionary<int, short[]> _effects = [];
    readonly object _lock = new();
    int _request;
    bool _disposed;

    public AudioMixer Mixer { get; } = new();

    /// <summary>Raised (on a worker thread) when a track could not be loaded or rendered; for logging only.</summary>
    public event Action<string, Exception>? Failed;

    /// <param name="loadFile">Returns the bytes of a game file by name (e.g. "ST0.MDT"), or null if it is missing.</param>
    /// <param name="effectsFile">Name of the sound-effect bank.</param>
    public MdrvSoundSink(Func<string, byte[]?> loadFile, string effectsFile = SoundEffectCatalog.BankFile)
    {
        _load = loadFile;
        try
        {
            if (loadFile(effectsFile) is { } bytes)
            {
                var bank = SoundEffectBank.Load(bytes);
                foreach (var e in bank.Effects)
                    _effects[e.Index + 1] = MdrvRenderer.ToPcm16(MdrvRenderer.RenderEffect(e));
            }
        }
        catch (Exception ex) when (ex is InvalidDataException or ArgumentException or IndexOutOfRangeException or IOException)
        {
            Failed?.Invoke(effectsFile, ex);
        }
    }

    public void PlayBgm(string trackFileName)
    {
        int request;
        lock (_lock) { request = ++_request; }
        Mixer.StopMusic();
        Task.Run(() =>
        {
            try
            {
                if (_load(trackFileName) is not { } bytes) return;
                var rendered = SongLooper.Render(MdrvSong.Load(bytes));
                lock (_lock)
                {
                    if (_disposed || request != _request) return; // another request came in meanwhile
                    Mixer.PlayMusic(rendered);
                }
            }
            catch (Exception ex) when (ex is InvalidDataException or ArgumentException or IndexOutOfRangeException or IOException or OutOfMemoryException)
            {
                Failed?.Invoke(trackFileName, ex);
            }
        });
    }

    public void StopBgm()
    {
        lock (_lock) { _request++; }
        Mixer.StopMusic();
    }

    public void FadeOutBgm()
    {
        lock (_lock) { _request++; }
        Mixer.FadeOutMusic(FadeSeconds);
    }

    public void PlaySe(int effectId)
    {
        if (_effects.TryGetValue(effectId, out var pcm)) Mixer.PlayEffect(effectId, pcm);
    }

    public void Dispose()
    {
        lock (_lock) { _disposed = true; _request++; }
        Mixer.StopMusic();
    }
}
