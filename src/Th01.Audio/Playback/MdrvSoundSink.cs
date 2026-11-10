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
    /// <summary>
    /// Fade-out of the music, measured in the original (a boss defeat over a song at 13.56 ms per tick): it starts at once, drops
    /// about 24 dB in the first 0.9 s, then about 10 dB per second, and the song stops 5.87 s (<see cref="FadeTicks"/> ticks) after the
    /// start. The time scales with the playing song's tick. APPROXIMATION of the driver, whose own fade routine (one attenuation
    /// step of 0.75 dB every second tick, stopping at step 127 = 254 ticks) does not reproduce this length or shape.
    /// </summary>
    public const double FadeTicks = 433;
    public static readonly (double Fraction, double Db)[] FadeCurve =
    [
        (0, 0), (0.1 / 5.87, 4.3), (0.5 / 5.87, 11.7), (0.9 / 5.87, 23.9), (1.9 / 5.87, 33.6), (2.9 / 5.87, 42.5),
        (3.9 / 5.87, 51.9), (4.9 / 5.87, 62.7), (1, 76),
    ];
    /// <summary>Length of <see cref="FadeOutBgm"/> for a song with the given seconds per tick.</summary>
    public static double FadeSeconds(double secondsPerTick) => FadeTicks * secondsPerTick;
    /// <summary>Track change while music plays: the old track fades out this long, the new one fades in (crossfade).</summary>
    public const double CrossfadeOutSeconds = 1.0, CrossfadeInSeconds = 0.4;

    readonly Func<string, byte[]?> _load;
    SoundEffectBank? _bank;
    readonly Dictionary<(int Id, int Tempo), short[]> _effects = [];   // rendered at the tempo the driver's timer has when they play
    const int StartupTempo = 0x41;
    int _tempo = StartupTempo;   // the tempo byte last set by a song (the effects share the music's timer)
    readonly object _lock = new();
    CancellationTokenSource? _cancel;
    string? _current;
    int _request;
    bool _disposed;
    double _secondsPerTick = TempoMap.SecondsPerTick(TempoMap.DefaultTempo);   // of the current track, for the fade

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
                _bank = SoundEffectBank.Load(bytes);
                foreach (var e in _bank.Effects) EffectPcm(e.Index + 1, StartupTempo);
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
                    _tempo = song.Tempos.Count > 0 ? song.Tempos[0].Tempo : TempoMap.DefaultTempo;
                    _secondsPerTick = TempoMap.SecondsPerTick(_tempo);
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
        double seconds;
        lock (_lock) { _request++; _current = null; _cancel = null; seconds = FadeSeconds(_secondsPerTick); }
        Mixer.FadeOutMusic(seconds, FadeCurve);
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
        int tempo;
        lock (_lock) tempo = _tempo;
        if (EffectPcm(effectId, tempo) is { } pcm) Mixer.PlayEffect(effectId, pcm);
    }

    // Effects tick with the driver's one timer, so they run at the speed of the song that set its tempo last.
    short[]? EffectPcm(int effectId, int tempo)
    {
        lock (_effects)
        {
            if (_effects.TryGetValue((effectId, tempo), out var pcm)) return pcm;
            if (_bank?.ForSeId(effectId) is not { } effect) return null;
            return _effects[(effectId, tempo)] = MdrvRenderer.ToPcm16(MdrvRenderer.RenderEffect(effect, TempoMap.SecondsPerTick(tempo)));
        }
    }

    public void Dispose()
    {
        lock (_lock) { _disposed = true; Supersede(); }
        Mixer.StopMusic();
    }
}
