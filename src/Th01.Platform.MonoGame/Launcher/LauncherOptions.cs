using Microsoft.Xna.Framework.Input;
using Th01.Core.Input;

namespace Th01.Platform.MonoGame.Launcher;

public enum RowKind { Toggle, Choice, Bar, Keys, Button, Info }

public enum RowId
{
    ReduceAll, SoftFlash, ReduceShake, NoLines, Warning,
    Fullscreen, WindowSize, IntegerScale, Filter,
    Music, Effects,
    Language, Font, FontRom, LocateFontRom,
    Move, Shot, Bomb, Pause, Numpad, PadShot, PadBomb, PadPause, Gamepad,
    Rank, Lives, Bombs,
    OpenFolder, Restore,
    RecordArt, Report, LastReport,
}

/// <summary>One line of an options tab, ready to draw.</summary>
/// <param name="Value">Text of a choice, bar, info line or the prompt while listening for a key.</param>
/// <param name="Level">0..10 filled cells of a bar.</param>
public sealed record OptionRow(RowId Id, RowKind Kind, Str Label, Str Hint, string Value = "", bool On = false, int Level = 0,
    string[]? Keys = null, string? Button = null, bool Danger = false, string? Tag = null, bool Good = false, bool Listening = false);

/// <summary>What the options screens need from the program around them (kept out of the model so it can be tested without a window).</summary>
public interface ILauncherServices
{
    bool GamepadConnected { get; }
    /// <summary>Name the system gives the connected pad; null when none (or it has no name).</summary>
    string? GamepadName { get; }
    FontRomState FontRomStatus { get; }
    /// <summary>Opens the folder with the scores and settings in the file explorer.</summary>
    void OpenDataFolder();
    /// <summary>Lines of the error / missing-art report; empty when there is nothing to report.</summary>
    IReadOnlyList<string> ReportLines();
    /// <summary>Counts for the "last report" row.</summary>
    ReportSummary Summary();
    /// <summary>Read the report files again (called when the options open, a tab changes or the report is shown).</summary>
    void RefreshReport();
    /// <summary>Plays a short effect so a volume change can be heard.</summary>
    void PreviewEffect();
    /// <summary>Asks for a FONT.ROM file; null = cancelled.</summary>
    string? PickFontRom();
}

public sealed partial class LauncherModel
{
    public const int VisibleRows = 6;
    static readonly OptionsTab[] AllTabs = Enum.GetValues<OptionsTab>();

    ILauncherServices? _services;
    InputBindings? _bindings;
    Func<GameSettings>? _gameSettings;
    readonly Queue<CaptureTarget> _capture = new();
    bool _confirmRestore;
    GameSettings? _gameCache;   // the game tab reads the file once per visit, not once per drawn frame

    enum CaptureTarget { Up, Down, Left, Right, Shot, Bomb, Pause, PadShot, PadBomb, PadPause }

    /// <summary>Everything the options screens act on. Null services/bindings leave those rows inert (tests, no window).</summary>
    public void Attach(ILauncherServices? services, InputBindings? bindings, Func<GameSettings>? gameSettings)
    {
        _services = services;
        _bindings = bindings;
        _gameSettings = gameSettings;
    }

    public LauncherSettings Settings => _settings;

    /// <summary>Goes up on every change of a setting, so the program can apply what changed (window, volume, filter...).</summary>
    public int Revision { get; private set; }

    public int Row { get; private set; }

    /// <summary>First visible row of the list (the screen shows <see cref="VisibleRows"/>).</summary>
    public int RowScroll { get; private set; }

    public bool ShowingReport { get; private set; }
    public IReadOnlyList<string> ReportLines { get; private set; } = [];

    /// <summary>True while a key or button for a remap is awaited: the program feeds <see cref="OfferKey"/> / <see cref="OfferButton"/>.</summary>
    public bool Listening => _capture.Count > 0;
    public bool ListeningForButton => Listening && _capture.Peek() is CaptureTarget.PadShot or CaptureTarget.PadBomb or CaptureTarget.PadPause;

    public IReadOnlyList<OptionRow> Rows => BuildRows(Tab);
    public OptionRow FocusedRow => Rows[Math.Min(Row, Rows.Count - 1)];

    void Changed()
    {
        Revision++;
        _save?.Invoke(_settings);
    }

    /// <summary>The window changed fullscreen by itself (F11): keep the setting in step and save it.</summary>
    public void SetFullscreen(bool on)
    {
        if (_settings.Fullscreen == on) return;
        _settings.Fullscreen = on;
        Changed();
    }

    void SelectTab(OptionsTab tab)
    {
        Tab = tab;
        Row = 0; RowScroll = 0;
        _capture.Clear();
        _confirmRestore = false;
        _gameCache = null;
        _services?.RefreshReport();
    }

    /// <summary>Sets one of the two volumes (0..10). Effects play a sample so the change can be heard; music has nothing playing here, but the mixer level is applied at once.</summary>
    public void SetVolume(RowId id, int level)
    {
        if (id == RowId.Music) _settings.MusicVolume = level; else _settings.EffectsVolume = level;
        Changed();
        if (id == RowId.Effects && level > 0) _services?.PreviewEffect();
    }

    void PressOptions(LauncherKey key)
    {
        if (ShowingReport)
        {
            if (key is LauncherKey.Back or LauncherKey.Select) ShowingReport = false;
            return;
        }
        if (Listening)
        {
            if (key == LauncherKey.Back) _capture.Clear();   // the program does not offer Esc as a key, so Back cancels
            return;
        }
        var rows = Rows;
        switch (key)
        {
            case LauncherKey.Back: Screen = LauncherScreen.Home; _confirmRestore = false; return;
            case LauncherKey.TabPrev: SelectTab(AllTabs[((int)Tab + AllTabs.Length - 1) % AllTabs.Length]); return;
            case LauncherKey.TabNext: SelectTab(AllTabs[((int)Tab + 1) % AllTabs.Length]); return;
            case LauncherKey.Up: MoveRow(-1, rows.Count); return;
            case LauncherKey.Down: MoveRow(1, rows.Count); return;
        }
        var row = rows[Math.Min(Row, rows.Count - 1)];
        int dir = key == LauncherKey.Left ? -1 : key == LauncherKey.Right ? 1 : 0;
        if (key == LauncherKey.Select || dir != 0) Adjust(row, key == LauncherKey.Select, dir);
    }

    void MoveRow(int dir, int count)
    {
        _confirmRestore = false;
        Row = (Row + dir + count) % count;
        if (Row < RowScroll) RowScroll = Row;
        if (Row >= RowScroll + VisibleRows) RowScroll = Row - VisibleRows + 1;
    }

    /// <summary>Select (<paramref name="select"/>) or Left/Right (<paramref name="dir"/>) on a row.</summary>
    void Adjust(OptionRow row, bool select, int dir)
    {
        var s = _settings;
        switch (row.Kind)
        {
            case RowKind.Toggle: SetToggle(row.Id, select ? !row.On : dir > 0); return;
            case RowKind.Choice: SetChoice(row.Id, select ? 1 : dir); return;
            case RowKind.Bar:
                if (select) return;
                SetVolume(row.Id, Math.Clamp(row.Level + dir, 0, 10));
                return;
            case RowKind.Keys:
                if (select) BeginCapture(row.Id);
                return;
            case RowKind.Button:
                if (select) Run(row.Id);
                return;
        }
    }

    void SetToggle(RowId id, bool on)
    {
        var s = _settings;
        switch (id)
        {
            case RowId.ReduceAll: s.ReduceAll = on; break;
            case RowId.SoftFlash: s.SoftenBombFlash = on; break;
            case RowId.ReduceShake: s.ReduceShake = on; break;
            case RowId.NoLines: s.RemoveTitleLines = on; break;
            case RowId.Warning: s.PhotosensitivityWarning = on; break;
            case RowId.Fullscreen: s.Fullscreen = on; break;
            case RowId.IntegerScale: s.IntegerScale = on; break;
            case RowId.Numpad: s.Numpad = on; break;
            case RowId.RecordArt: s.RecordMissingArt = on; break;
            default: return;
        }
        Changed();
    }

    void SetChoice(RowId id, int step)
    {
        var s = _settings;
        switch (id)
        {
            case RowId.WindowSize: s.WindowScale = (s.WindowScale + step + 3) % 3; break;
            case RowId.Filter: s.Filter = (s.Filter + step + 3) % 3; break;
            case RowId.Font: s.PreferFontRom = !s.PreferFontRom; break;
            case RowId.Language: SetLanguage(Language == LauncherLanguage.English ? LauncherLanguage.Japanese : LauncherLanguage.English); Revision++; return;
            default: return;
        }
        Changed();
    }

    void Run(RowId id)
    {
        switch (id)
        {
            case RowId.OpenFolder: _services?.OpenDataFolder(); break;
            case RowId.Restore:
                if (!_confirmRestore) { _confirmRestore = true; break; }
                _confirmRestore = false;
                _settings.ResetToDefaults();
                if (_bindings is not null) { _bindings.ResetToDefaults(); SaveBindings(); }
                Changed();
                break;
            case RowId.Report:
                _services?.RefreshReport();
                ReportLines = _services?.ReportLines() ?? [];
                ShowingReport = true;
                break;
            case RowId.LocateFontRom:
                if (_services?.PickFontRom() is { } path) { _settings.FontRomPath = path; Changed(); }
                break;
        }
    }

    // ---- Key and button remapping ----------------------------------------------------------------------------------------

    void BeginCapture(RowId id)
    {
        _capture.Clear();
        switch (id)
        {
            case RowId.Move:
                foreach (var t in new[] { CaptureTarget.Up, CaptureTarget.Down, CaptureTarget.Left, CaptureTarget.Right }) _capture.Enqueue(t);
                break;
            case RowId.Shot: _capture.Enqueue(CaptureTarget.Shot); break;
            case RowId.Bomb: _capture.Enqueue(CaptureTarget.Bomb); break;
            case RowId.Pause: _capture.Enqueue(CaptureTarget.Pause); break;
            case RowId.PadShot: _capture.Enqueue(CaptureTarget.PadShot); break;
            case RowId.PadBomb: _capture.Enqueue(CaptureTarget.PadBomb); break;
            case RowId.PadPause: _capture.Enqueue(CaptureTarget.PadPause); break;
        }
    }

    /// <summary>A key pressed while <see cref="Listening"/> for a key: becomes the binding of the awaited action.</summary>
    public void OfferKey(Keys key)
    {
        if (!Listening || ListeningForButton || _bindings is null || key == Keys.Escape) return;
        var target = _capture.Dequeue();
        var button = target switch
        {
            CaptureTarget.Up => GameButtons.Up, CaptureTarget.Down => GameButtons.Down, CaptureTarget.Left => GameButtons.Left,
            CaptureTarget.Right => GameButtons.Right, CaptureTarget.Shot => GameButtons.Shot, CaptureTarget.Bomb => GameButtons.Bomb,
            _ => GameButtons.Pause,
        };
        _bindings.Keyboard[button] = [key];
        SaveBindings();
        Revision++;
    }

    /// <summary>A pad button pressed while <see cref="ListeningForButton"/>.</summary>
    public void OfferButton(Buttons button)
    {
        if (!ListeningForButton || _bindings is null) return;
        var target = _capture.Dequeue();
        var game = target switch { CaptureTarget.PadShot => GameButtons.Shot, CaptureTarget.PadBomb => GameButtons.Bomb, _ => GameButtons.Pause };
        _bindings.Gamepad[game] = [button];
        SaveBindings();
        Revision++;
    }

    void SaveBindings()
    {
        try { _bindings?.Save(); }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException) { Console.Error.WriteLine($"Could not save {InputBindings.FileName}: {e.Message}"); }
    }

    /// <summary>Short name of a key for the key caps.</summary>
    public static string KeyName(Keys k) => k switch
    {
        Keys.Up => "↑", Keys.Down => "↓", Keys.Left => "←", Keys.Right => "→",
        Keys.Escape => "Esc", Keys.Space => "Space", Keys.Enter => "Enter", Keys.Back => "Bksp",
        Keys.LeftShift => "L-Shift", Keys.RightShift => "R-Shift", Keys.LeftControl => "L-Ctrl", Keys.RightControl => "R-Ctrl",
        Keys.LeftAlt => "L-Alt", Keys.RightAlt => "R-Alt", Keys.Tab => "Tab",
        >= Keys.D0 and <= Keys.D9 => ((char)('0' + (k - Keys.D0))).ToString(),
        >= Keys.NumPad0 and <= Keys.NumPad9 => "Num" + (char)('0' + (k - Keys.NumPad0)),
        _ => k.ToString(),
    };

    // ---- Rows --------------------------------------------------------------------------------------------------------------

    string OnOff(bool on) => Text(on ? Str.ValOn : Str.ValOff);

    OptionRow Toggle(RowId id, Str label, Str hint, bool on) => new(id, RowKind.Toggle, label, hint, OnOff(on), on);
    OptionRow Choice(RowId id, Str label, Str hint, string value) => new(id, RowKind.Choice, label, hint, value);

    OptionRow KeysRow(RowId id, Str label, Str hint, GameButtons button, params GameButtons[] more) =>
        new(id, RowKind.Keys, label, hint, Keys: KeyCaps(button, more));

    string[] KeyCaps(GameButtons first, GameButtons[] more)
    {
        var all = new[] { first }.Concat(more);
        return all.Select(b => _bindings is not null && _bindings.Keyboard.TryGetValue(b, out var ks) && ks.Length > 0 ? KeyName(ks[0]) : Text(Str.ValNotSet)).ToArray();
    }

    string PadName(GameButtons b) =>
        _bindings is not null && _bindings.Gamepad.TryGetValue(b, out var bs) && bs.Length > 0 ? bs[0].ToString() : Text(Str.ValNotSet);

    OptionRow PadRow(RowId id, GameButtons b) => new(id, RowKind.Keys, Label(id), Str.HintPadButton, Keys: [PadName(b)]);

    static Str Label(RowId id) => id switch
    {
        RowId.PadShot => Str.RowPadShot, RowId.PadBomb => Str.RowPadBomb, _ => Str.RowPadPause,
    };

    List<OptionRow> BuildRows(OptionsTab tab)
    {
        var s = _settings;
        var rows = new List<OptionRow>();
        switch (tab)
        {
            case OptionsTab.Accessibility:
                rows.Add(Toggle(RowId.ReduceAll, Str.RowReduceAll, Str.HintReduceAll, s.ReduceAll));
                rows.Add(Toggle(RowId.SoftFlash, Str.RowSoftFlash, Str.HintSoftFlash, s.SoftenBombFlash));
                rows.Add(Toggle(RowId.ReduceShake, Str.RowReduceShake, Str.HintReduceShake, s.ReduceShake));
                rows.Add(Toggle(RowId.NoLines, Str.RowNoLines, Str.HintNoLines, s.RemoveTitleLines));
                rows.Add(Toggle(RowId.Warning, Str.RowWarning, Str.HintWarning, s.PhotosensitivityWarning));
                break;
            case OptionsTab.Video:
                rows.Add(Toggle(RowId.Fullscreen, Str.RowFullscreen, Str.HintFullscreen, s.Fullscreen));
                rows.Add(Choice(RowId.WindowSize, Str.RowWindowSize, Str.HintWindowSize, Text(s.WindowScale switch { 0 => Str.ValSize1, 1 => Str.ValSize2, _ => Str.ValSize3 })));
                rows.Add(Toggle(RowId.IntegerScale, Str.RowIntegerScale, Str.HintIntegerScale, s.IntegerScale));
                rows.Add(Choice(RowId.Filter, Str.RowFilter, Str.HintFilter, Text(s.Filter switch { 0 => Str.ValFilterSharp, 1 => Str.ValFilterSmooth, _ => Str.ValFilterScan })));
                break;
            case OptionsTab.Audio:
                rows.Add(new OptionRow(RowId.Music, RowKind.Bar, Str.RowMusic, Str.HintMusic, s.MusicVolume * 10 + "%", Level: s.MusicVolume));
                rows.Add(new OptionRow(RowId.Effects, RowKind.Bar, Str.RowEffects, Str.HintEffects, s.EffectsVolume * 10 + "%", Level: s.EffectsVolume));
                break;
            case OptionsTab.Font:
                rows.Add(Choice(RowId.Language, Str.RowLanguage, Str.HintLanguage, Language == LauncherLanguage.English ? "English" : "日本語"));
                rows.Add(Choice(RowId.Font, Str.RowFont, Str.HintFont, Text(s.PreferFontRom ? Str.ValFontRom : Str.ValFontFree)));
                var rom = _services?.FontRomStatus ?? FontRomState.NotFound;
                rows.Add(new OptionRow(RowId.FontRom, RowKind.Info, Str.RowFontRom, Str.HintFontRom,
                    Text(rom switch { FontRomState.Found => Str.ValFound, FontRomState.Unsupported => Str.ValRomUnsupported, _ => Str.ValNotFound }),
                    Tag: Text(Str.TagAutomatic), Good: rom == FontRomState.Found));
                rows.Add(new OptionRow(RowId.LocateFontRom, RowKind.Button, Str.RowLocateFontRom, Str.HintLocateFontRom, Button: Text(Str.BtnBrowse)));
                break;
            case OptionsTab.Controls:
                rows.Add(KeysRowFor(RowId.Move, Str.RowMove, Str.HintMove, GameButtons.Left, GameButtons.Up, GameButtons.Right, GameButtons.Down));
                rows.Add(KeysRowFor(RowId.Shot, Str.RowShot, Str.HintShot, GameButtons.Shot));
                rows.Add(KeysRowFor(RowId.Bomb, Str.RowBomb, Str.HintBomb, GameButtons.Bomb));
                rows.Add(KeysRowFor(RowId.Pause, Str.RowPause, Str.HintPause, GameButtons.Pause));
                rows.Add(Toggle(RowId.Numpad, Str.RowNumpad, Str.HintNumpad, s.Numpad));
                rows.Add(PadRowFor(RowId.PadShot, GameButtons.Shot));
                rows.Add(PadRowFor(RowId.PadBomb, GameButtons.Bomb));
                rows.Add(PadRowFor(RowId.PadPause, GameButtons.Pause));
                bool pad = _services?.GamepadConnected ?? false;
                string padText = !pad ? Text(Str.ValPadNone) : string.IsNullOrWhiteSpace(_services?.GamepadName) ? Text(Str.ValPadConnected) : _services!.GamepadName!.Trim();
                rows.Add(new OptionRow(RowId.Gamepad, RowKind.Info, Str.RowGamepad, Str.HintGamepad, padText, Tag: Text(Str.TagOptional), Good: pad));
                break;
            case OptionsTab.Game:
                var g = _gameCache ??= _gameSettings?.Invoke() ?? new GameSettings();
                var rank = g.Rank switch { Th01.Core.Game.Rank.Easy => Str.ValRankEasy, Th01.Core.Game.Rank.Normal => Str.ValRankNormal, Th01.Core.Game.Rank.Hard => Str.ValRankHard, _ => Str.ValRankLunatic };
                rows.Add(new OptionRow(RowId.Rank, RowKind.Info, Str.RowRank, Str.HintGameReadOnly, Text(rank), Tag: Text(Str.TagReadOnly)));
                rows.Add(new OptionRow(RowId.Lives, RowKind.Info, Str.RowLives, Str.HintGameReadOnly, (3 + g.LivesExtra).ToString(), Tag: Text(Str.TagReadOnly)));
                rows.Add(new OptionRow(RowId.Bombs, RowKind.Info, Str.RowBombs, Str.HintGameReadOnly, g.Bombs.ToString(), Tag: Text(Str.TagReadOnly)));
                break;
            case OptionsTab.Data:
                rows.Add(new OptionRow(RowId.OpenFolder, RowKind.Button, Str.RowOpenFolder, Str.HintOpenFolder, Button: Text(Str.BtnOpenFolder)));
                rows.Add(new OptionRow(RowId.Restore, RowKind.Button, Str.RowRestore, Str.HintRestore, Button: Text(_confirmRestore ? Str.BtnConfirm : Str.BtnRestore), Danger: true));
                break;
            case OptionsTab.Diagnostics:
                rows.Add(Toggle(RowId.RecordArt, Str.RowRecordArt, Str.HintRecordArt, s.RecordMissingArt));
                rows.Add(new OptionRow(RowId.Report, RowKind.Button, Str.RowReport, Str.HintReport, Button: Text(Str.BtnViewReport)));
                var sum = _services?.Summary() ?? ReportSummary.Empty;
                rows.Add(new OptionRow(RowId.LastReport, RowKind.Info, Str.RowLastReport, Str.HintLastReport, LastReportText(sum),
                    Tag: Text(sum.IsEmpty ? Str.TagOk : Str.TagAttention), Good: sum.IsEmpty));
                break;
        }
        return rows;
    }

    string LastReportText(ReportSummary sum)
    {
        if (sum.IsEmpty) return Text(Str.ValNoErrors);
        var parts = new List<string>();
        if (sum.CrashLog) parts.Add(Text(Str.ValCrashLog));
        if (sum.MissingArt > 0) parts.Add(Language == LauncherLanguage.English ? $"{sum.MissingArt} {Text(Str.ValMissingArt)}" : $"{sum.MissingArt}{Text(Str.ValMissingArt)}");
        return string.Join(Language == LauncherLanguage.English ? " + " : "、", parts);
    }

    OptionRow KeysRowFor(RowId id, Str label, Str hint, GameButtons first, params GameButtons[] more)
    {
        var row = KeysRow(id, label, hint, first, more);
        return IsListeningRow(id) ? row with { Listening = true, Value = Text(Str.ValPressKey) } : row;
    }

    OptionRow PadRowFor(RowId id, GameButtons b)
    {
        var row = PadRow(id, b);
        return IsListeningRow(id) ? row with { Listening = true, Value = Text(Str.ValPressButton) } : row;
    }

    bool IsListeningRow(RowId id)
    {
        if (!Listening) return false;
        return _capture.Peek() switch
        {
            CaptureTarget.Up or CaptureTarget.Down or CaptureTarget.Left or CaptureTarget.Right => id == RowId.Move,
            CaptureTarget.Shot => id == RowId.Shot,
            CaptureTarget.Bomb => id == RowId.Bomb,
            CaptureTarget.Pause => id == RowId.Pause,
            CaptureTarget.PadShot => id == RowId.PadShot,
            CaptureTarget.PadBomb => id == RowId.PadBomb,
            _ => id == RowId.PadPause,
        };
    }
}
