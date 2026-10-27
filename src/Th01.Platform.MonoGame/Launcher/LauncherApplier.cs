namespace Th01.Platform.MonoGame.Launcher;

/// <summary>What the launcher's settings act on in the running program (the window, the mixer, the key map, the font...).</summary>
public interface ILauncherSurface
{
    bool IsFullscreen { get; }
    void SetFullscreen(bool on);
    /// <summary>Size of the window's drawing area when windowed.</summary>
    void SetWindowClientSize(int width, int height);
    void SetIntegerScale(bool on);
    /// <summary>0 = sharp pixels, 1 = smooth, 2 = CRT scanlines.</summary>
    void SetFilter(int filter);
    void SetVolumes(float music, float effects);
    void SetNumpad(bool on);
    void ConfigureFonts(string? romPath, bool preferRom, string? gameFolder);
    void SetRecordMissingArt(bool on);
}

/// <summary>
/// Puts the launcher's settings to work, at once, whenever they changed (the model's revision). Window size is applied only
/// while windowed: if it changes during fullscreen it is applied when the player leaves fullscreen.
/// </summary>
public sealed class LauncherApplier(ILauncherSurface surface)
{
    int _revision = -1, _windowScale = -1;

    /// <summary>Applies the settings if <paramref name="revision"/> is new. Returns whether anything was applied.</summary>
    public bool Apply(LauncherSettings s, int revision, string? gameFolder)
    {
        if (revision == _revision) return false;
        _revision = revision;
        if (surface.IsFullscreen != s.Fullscreen) surface.SetFullscreen(s.Fullscreen);
        if (!surface.IsFullscreen && _windowScale != s.WindowScale)
        {
            _windowScale = s.WindowScale;
            surface.SetWindowClientSize(640 * (s.WindowScale + 1), 400 * (s.WindowScale + 1));
        }
        surface.SetIntegerScale(s.IntegerScale);
        surface.SetFilter(s.Filter);
        surface.SetVolumes(s.MusicLevel, s.EffectsLevel);
        surface.SetNumpad(s.Numpad);
        surface.ConfigureFonts(s.FontRomPath, s.PreferFontRom, gameFolder);
        surface.SetRecordMissingArt(s.RecordMissingArt);
        return true;
    }
}
