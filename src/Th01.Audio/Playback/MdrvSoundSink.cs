using Th01.Audio.Mdrv;
using Th01.Audio.Synth;
using Th01.Core.Audio;

namespace Th01.Audio.Playback;

/// <summary>
/// <see cref="ISoundSink"/> that plays the game's own MDRV music and effects. Music is rendered in the background in
/// time order and starts as soon as its first few seconds are ready; effects are rendered once at construction.
/// The platform pulls samples with <see cref="Mixer"/>.<see cref="AudioMixer.Mix"/>. Missing files make the sink silent, never throw.
/// Asking for the track that is already playing (or being prepared) does nothing, as in the original, where the stage music
/// only restarts at a scene or boss change. A different track takes over with a crossfade (the original fades the old one out).
/// </summary>
public sealed class MdrvSoundSink : ISoundSink, IDisposable
{
    /// <summary>Length of <see cref="FadeOutBgm"/>.</summary>
    public const double FadeSeconds = 1.5;
    /// <summary>Track change while music plays: the old track fades out this long, the new one fades in (crossfade).</summary>
    public const double CrossfadeOutSeconds = 1.0, CrossfadeInSeconds = 0.4;

    readonly Func<string, byte[]?> _load;
    readonly Dictionary<int, short[]> _effects = [];
    readonly object _lock = new();
    CancellationTokenSource? _cancel;
    string? _current;
    int _request;
    bool _disposed;

    public AudioMixer Mixer { get; } = new();

    /// <summary>Raised (on a worker thread) when a track could not be loaded or rendered; for logging only.</summary>
    public event Action<string, Exception>? Failed;

    /// <summary>The track last requested and not stopped since, or null.</summary>
    public string? CurrentTrack { get { lock (_lock) return _current; } }

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
        CancellationTokenSource cts;
        lock (_lock)
        {
            if (_disposed || string.Equals(_current, trackFileName, StringComparison.OrdinalIgnoreCase)) return;
            _cancel?.Cancel();
            cts = _cancel = new CancellationTokenSource();
            _current = trackFileName;
            request = ++_request;
        }
        // The old track keeps playing until the new one is ready, then they crossfade.
        Task.Run(() =>
        {
            try
            {
                if (_load(trackFileName) is not { } bytes) return;
                var song = MdrvSong.Load(bytes);
                var rendering = SongLooper.Start(song, cts.Token);   // returns at once, fills in the background
                lock (_lock)
                {
                    if (_disposed || request != _request) { cts.Cancel(); return; }   // another request came in meanwhile
                    bool crossfade = Mixer.MusicPlaying;
                    Mixer.PlayMusic(rendering, crossfade ? CrossfadeOutSeconds : 0, crossfade ? CrossfadeInSeconds : 0);
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
        lock (_lock) { Supersede(); }
        Mixer.StopMusic();
    }

    public void FadeOutBgm()
    {
        // The render is not cancelled: the part already rendered has to last through the fade.
        lock (_lock) { _request++; _current = null; _cancel = null; }
        Mixer.FadeOutMusic(FadeSeconds);
    }

    // Caller holds _lock: forget the current track and stop its background render.
    void Supersede()
    {
        _request++;
        _current = null;
        _cancel?.Cancel();
        _cancel = null;
    }

    public void PlaySe(int effectId)
    {
        if (_effects.TryGetValue(effectId, out var pcm)) Mixer.PlayEffect(effectId, pcm);
    }

    public void Dispose()
    {
        lock (_lock) { _disposed = true; Supersede(); }
        Mixer.StopMusic();
    }
}
