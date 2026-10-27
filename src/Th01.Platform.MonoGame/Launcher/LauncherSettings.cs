using System.Text.Json;
using System.Text.Json.Nodes;
using Th01.Core.Game;

namespace Th01.Platform.MonoGame.Launcher;

/// <summary>
/// What the player chose in the launcher, kept in launcher.json in the user's data folder (never in the repository).
/// A missing or damaged file gives the defaults, and so does any single key that is missing or out of range.
/// The file is shared with the remembered game (<see cref="LauncherFile"/> merges keys), so only our keys are written.
/// </summary>
public sealed class LauncherSettings
{
    public const string FileName = "launcher.json";

    public LauncherLanguage Language { get; set; } = LauncherLanguage.English;

    // Accessibility. These only change how things are drawn, never the simulation.
    public bool SoftenBombFlash { get; set; } = true;
    public bool ReduceShake { get; set; }
    public bool RemoveTitleLines { get; set; }
    public bool PhotosensitivityWarning { get; set; } = true;

    // Video.
    public bool Fullscreen { get; set; }
    /// <summary>0 = 1x (640x400), 1 = 2x, 2 = 3x.</summary>
    public int WindowScale { get; set; } = 1;
    public bool IntegerScale { get; set; } = true;
    /// <summary>0 = sharp pixels, 1 = smooth, 2 = CRT scanlines.</summary>
    public int Filter { get; set; }

    // Audio: 0..10 steps.
    public int MusicVolume { get; set; } = 10;
    public int EffectsVolume { get; set; } = 10;

    // Font.
    /// <summary>True prefers the user's FONT.ROM when there is one (the free font is the fallback either way).</summary>
    public bool PreferFontRom { get; set; } = true;
    public string? FontRomPath { get; set; }

    // Controls and diagnostics.
    public bool Numpad { get; set; } = true;
    public bool RecordMissingArt { get; set; } = true;

    /// <summary>The "reduce lights and motion" switch of the design: on when all three reductions are on.</summary>
    public bool ReduceAll
    {
        get => SoftenBombFlash && ReduceShake && RemoveTitleLines;
        set { SoftenBombFlash = value; ReduceShake = value; RemoveTitleLines = value; }
    }

    public VisualOptions ToVisualOptions() => new()
    {
        BombFlash = SoftenBombFlash ? BombFlash.Softened : BombFlash.Original,
        ScreenShake = !ReduceShake,
    };

    public float MusicLevel => MusicVolume / 10f;
    public float EffectsLevel => EffectsVolume / 10f;

    /// <summary>Back to the defaults; the language and the chosen game stay (the player must still be able to read the screen).</summary>
    public void ResetToDefaults()
    {
        var d = new LauncherSettings { Language = Language };
        CopyFrom(d);
    }

    void CopyFrom(LauncherSettings o)
    {
        Language = o.Language;
        SoftenBombFlash = o.SoftenBombFlash; ReduceShake = o.ReduceShake; RemoveTitleLines = o.RemoveTitleLines; PhotosensitivityWarning = o.PhotosensitivityWarning;
        Fullscreen = o.Fullscreen; WindowScale = o.WindowScale; IntegerScale = o.IntegerScale; Filter = o.Filter;
        MusicVolume = o.MusicVolume; EffectsVolume = o.EffectsVolume;
        PreferFontRom = o.PreferFontRom; FontRomPath = o.FontRomPath;
        Numpad = o.Numpad; RecordMissingArt = o.RecordMissingArt;
    }

    public static LauncherSettings Load(string? path = null)
    {
        path ??= Path.Combine(Directory.GetCurrentDirectory(), FileName);
        var s = new LauncherSettings();
        var o = LauncherFile.Read(path);
        if (string.Equals(Str(o, "language"), "ja", StringComparison.OrdinalIgnoreCase)) s.Language = LauncherLanguage.Japanese;
        s.SoftenBombFlash = Bool(o, "softenBombFlash", s.SoftenBombFlash);
        s.ReduceShake = Bool(o, "reduceShake", s.ReduceShake);
        s.RemoveTitleLines = Bool(o, "removeTitleLines", s.RemoveTitleLines);
        s.PhotosensitivityWarning = Bool(o, "photosensitivityWarning", s.PhotosensitivityWarning);
        s.Fullscreen = Bool(o, "fullscreen", s.Fullscreen);
        s.WindowScale = Int(o, "windowScale", s.WindowScale, 0, 2);
        s.IntegerScale = Bool(o, "integerScale", s.IntegerScale);
        s.Filter = Int(o, "filter", s.Filter, 0, 2);
        s.MusicVolume = Int(o, "musicVolume", s.MusicVolume, 0, 10);
        s.EffectsVolume = Int(o, "effectsVolume", s.EffectsVolume, 0, 10);
        s.PreferFontRom = Bool(o, "preferFontRom", s.PreferFontRom);
        s.FontRomPath = Str(o, "fontRomPath");
        s.Numpad = Bool(o, "numpad", s.Numpad);
        s.RecordMissingArt = Bool(o, "recordMissingArt", s.RecordMissingArt);
        return s;
    }

    public void Save(string? path = null)
    {
        path ??= Path.Combine(Directory.GetCurrentDirectory(), FileName);
        LauncherFile.Update(path, o =>
        {
            o["language"] = Language == LauncherLanguage.Japanese ? "ja" : "en";
            o["softenBombFlash"] = SoftenBombFlash;
            o["reduceShake"] = ReduceShake;
            o["removeTitleLines"] = RemoveTitleLines;
            o["photosensitivityWarning"] = PhotosensitivityWarning;
            o["fullscreen"] = Fullscreen;
            o["windowScale"] = WindowScale;
            o["integerScale"] = IntegerScale;
            o["filter"] = Filter;
            o["musicVolume"] = MusicVolume;
            o["effectsVolume"] = EffectsVolume;
            o["preferFontRom"] = PreferFontRom;
            if (FontRomPath is null) o.Remove("fontRomPath"); else o["fontRomPath"] = FontRomPath;
            o["numpad"] = Numpad;
            o["recordMissingArt"] = RecordMissingArt;
        });
    }

    static string? Str(JsonObject o, string key) => o[key] is JsonValue v && v.TryGetValue<string>(out var s) ? s : null;
    static bool Bool(JsonObject o, string key, bool fallback) => o[key] is JsonValue v && v.TryGetValue<bool>(out var b) ? b : fallback;
    static int Int(JsonObject o, string key, int fallback, int min, int max) =>
        o[key] is JsonValue v && v.TryGetValue<int>(out var i) ? Math.Clamp(i, min, max) : fallback;
}
