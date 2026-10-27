using Th01.Core.Audio;
using Th01.Core.Game;
using Th01.Core.Graphics;
using Th01.Core.Input;
using Th01.Core.Rng;
using Th01.Audio.Playback;

namespace Th01.Platform.MonoGame;

/// <summary>What the title screen wants the flow to do once the player has chosen.</summary>
public enum TitleAction { None, Start, Continue, Quit }

/// <summary>
/// The title screen of OP.EXE: fade in, a white-lines reveal, the blinking "hit key" prompt, then the window with the
/// main menu, the options and the music test. Pure state plus drawing on two <see cref="VramPages"/> (page 0 shown, page 1
/// the clean copy that rectangles are restored from, as the original does with the EGC). One <see cref="Tick"/> = one VSync.
/// ref: th01/op_01.cpp (title_init, whitelines_animate, title_hit_key_put, title_window_put, the menus, main)
/// </summary>
public sealed class TitleScene
{
    // Timeline of the intro, in ticks. ref: title_init (z_palette_black_in = 16 steps, frame_delay(100)), whitelines_animate (RES_Y / 4 steps)
    // Measured on the original (T5): the white lines are ticks 101-200 and the prompt first shows at tick 202. frame_delay(100) counts
    // from the last reset of the VSync counter, which the fade-in does not reset, so fade-in + pause add up to 100 ticks.
    public const int FadeInTicks = 16, PauseTicks = 100 - FadeInTicks, WhiteLineSteps = Framebuffer.Height / 4;
    /// <summary>Tick on which the prompt first shows (tick 1 is the first call to <see cref="Tick"/>).</summary>
    public const int FirstHitKeyTick = FadeInTicks + PauseTicks + WhiteLineSteps + 1 + 1;
    public const int HitKeyPeriod = 70, HitKeyVisible = 50;
    /// <summary>Wait when a sub-menu is left or entered (frame_delay(15) in the original; it keeps the menu from re-triggering).</summary>
    public const int MenuSwitchDelay = 15;

    // Menu geometry. ref: op_01.cpp "Coordinates"
    public const int MenuLeft = 220, MenuCenterX = 316, MenuCenterY = 316, MenuW = 176, ChoicePaddedH = 20, ChoiceCountMax = 5;
    public const int MainChoiceW = 9 * 16, GlyphH = 16;
    public const byte ColActive = 15, ColInactive = 5;
    const int TrackCount = TitleTexts.MusicTrackCount;

    public enum Phase { FadeIn, Pause, WhiteLines, Restore, HitKey, Menu }
    public enum Menu { Main, Option, Music, LeaveOptionDelay, LeaveMusicDelay }

    readonly IScreenImages _images;
    readonly IFontSource _font;
    readonly GameSettings _settings;
    readonly TitleTexts _texts;
    readonly ISoundSink? _sound;
    readonly Action<GameSettings>? _save;
    readonly VramPages _pages = new();
    readonly PaletteFader _fader = new();
    readonly bool[] _drawnAt = new bool[Framebuffer.Height];
    IRand _rng = new(0); // a struct: must not be readonly // the original's irand state at this point is not known to be 0 (see docs); unverified
    ArtFrame _windowImage;
    bool _hasWindow;

    GameButtons _prev;
    int _phaseTick;
    int _wait;
    int _y1, _y2;
    int _hitKeyFrame;
    int _menuSel;
    int _musicSel;
    bool _inThisMenu;
    int _bgmModeAtEntry;

    public Phase CurrentPhase { get; private set; } = Phase.FadeIn;
    public Menu CurrentMenu { get; private set; } = Menu.Main;
    public TitleAction Action { get; private set; }
    /// <summary>The title screen's frame counter: +1 per menu iteration. The game receives it as frame_rand.</summary>
    public uint FrameRand { get; private set; }
    /// <summary>The flow could not act on the choice (e.g. CONTINUE with nothing to continue): stay in the menu.</summary>
    public void ClearAction() => Action = TitleAction.None;
    readonly bool _whiteLines;
    public int MenuSelection => _menuSel;
    public int MusicSelection => _musicSel;
    /// <summary>Whether the prompt was drawn (not erased) on the last tick; false until the first frame has been put.</summary>
    public bool HitKeyVisibleNow => CurrentPhase == Phase.HitKey && _hitKeyFrame > 0 && (_hitKeyFrame - 1) % HitKeyPeriod < HitKeyVisible;

    public TitleScene(IScreenImages images, IFontSource font, GameSettings settings, TitleTexts texts,
        ISoundSink? sound = null, Action<GameSettings>? saveSettings = null, bool whiteLines = true)
    {
        _whiteLines = whiteLines;
        _images = images;
        _font = font;
        _settings = settings;
        _texts = texts;
        _sound = sound;
        _save = saveSettings;
        _bgmModeAtEntry = settings.Music ? 1 : 0;
        Begin();
    }

    // title_init: music, REIIDEN2 + palette on page 1 then copied to page 0; REIIDEN3 only on page 1.
    void Begin()
    {
        if (_settings.Music) _sound?.PlayBgm(BgmTracks.Title);
        var p0 = _pages.Page0;
        var p1 = _pages.Page1;
        if (_images.TryLoad("REIIDEN2.GRP", out var baseImage))
        {
            p1.Clear();
            p1.ApplyPalette(baseImage);
            p1.Draw(baseImage, 0, 0);
            p0.CopyFrom(p1);
            if (_images.TryLoad("REIIDEN3.GRP", out var top)) p1.Draw(top, 0, 0);
        }
        else
        {
            for (int i = 0; i < Framebuffer.ColorCount; i++) { p0.SetPalette4(i, i, i, i); p1.SetPalette4(i, i, i, i); }
        }
        _hasWindow = _images.TryLoad("OP_WIN.GRP", out _windowImage);
        _fader.FadeFrom(p0, 0, 0, 0);
    }

    public void Tick(GameButtons input)
    {
        var pressed = input & ~_prev;
        _prev = input;
        _phaseTick++;
        switch (CurrentPhase)
        {
            case Phase.FadeIn:
                _fader.Step();
                if (_phaseTick >= FadeInTicks) { _fader.Finish(); Next(Phase.Pause); }
                break;
            case Phase.Pause:
                if (_phaseTick >= PauseTicks) Next(_whiteLines ? Phase.WhiteLines : Phase.Restore);   // the launcher can switch the lines off
                break;
            case Phase.WhiteLines:
                WhiteLineStep();
                if (_phaseTick >= WhiteLineSteps) Next(Phase.Restore);
                break;
            case Phase.Restore:
                // graph_copy_accessed_page_to_other: page 1 over page 0 (pixels only; the palette is hardware). The screen is back without
                // lines one tick after the last step; the prompt follows on the next.
                _pages.RestoreAllPixels();
                Next(Phase.HitKey);
                break;
            case Phase.HitKey:
                // while(!key_sense_bios()) { frame_delay(1); title_hit_key_put(frame++); }
                if (pressed != GameButtons.None) { OpenWindow(); break; }
                HitKeyPut(_hitKeyFrame);
                _hitKeyFrame++;
                break;
            case Phase.Menu:
                MenuIteration(pressed, input);
                break;
        }
    }

    void Next(Phase p)
    {
        CurrentPhase = p;
        _phaseTick = 0;
    }

    // ---- whitelines_animate: 2 new horizontal lines per tick, the previous pair is put back from page 1
    void WhiteLineStep()
    {
        _pages.Restore(0, _y1, Framebuffer.Width, 1);
        _pages.Restore(0, _y2, Framebuffer.Width, 1);
        do _y1 = _rng.Next() % Framebuffer.Height; while (_drawnAt[_y1]);
        _drawnAt[_y1] = true;
        do _y2 = _rng.Next() % Framebuffer.Height; while (_drawnAt[_y2]);
        _drawnAt[_y2] = true;
        _pages.Page0.FillRect(0, _y1, Framebuffer.Width, 1, 15);
        _pages.Page0.FillRect(0, _y2, Framebuffer.Width, 1, 15);
    }

    // ---- title_hit_key_put
    int HitKeyLeft => Framebuffer.Width / 2 - 16 - TextRenderer.Measure(_font, _texts.HitKey) / 2;
    static int ChoiceTop(int choice, int count) => MenuCenterY - count * ChoicePaddedH / 2 + choice * ChoicePaddedH;

    void HitKeyPut(int frame)
    {
        int left = HitKeyLeft, top = ChoiceTop(0, 1);
        if (frame % HitKeyPeriod < HitKeyVisible) DrawText(_pages.Page0, _texts.HitKey, left, top, ColActive, Weight.Bold);
        else _pages.Restore(left, top, TextRenderer.Measure(_font, _texts.HitKey), GlyphH);
    }

    // ---- title_window_put: page 1 over page 0 (drops the prompt), OP_WIN with colour 15 transparent, then page 0 back to page 1
    void OpenWindow()
    {
        _pages.RestoreAllPixels();
        if (_hasWindow) _pages.Page0.BlitColorKey(_windowImage.Pixels.Span, _windowImage.Width, _windowImage.Height, 0, 0, 15);
        _pages.Page1.CopyPixelsFrom(_pages.Page0);
        FrameRand = 0;
        CurrentPhase = Phase.Menu;
        CurrentMenu = Menu.Main;
        _inThisMenu = false;
        _phaseTick = 0;
    }

    // ---- the menu loop. One pass = one iteration of the original's while(!quit): sense, update, frame_rand++, frame_delay(1)
    void MenuIteration(GameButtons pressed, GameButtons held)
    {
        if (_wait > 0) { _wait--; return; }
        // Presses made during a switch delay were swallowed (the original clears the key buffer after every pass).
        switch (CurrentMenu)
        {
            case Menu.Main: MainMenu(pressed); break;
            case Menu.Option: OptionMenu(pressed); break;
            case Menu.Music: MusicMenu(pressed); break;
            case Menu.LeaveOptionDelay:
                // Leaving the options: if the music mode changed, stop or restart the title music; then wait before the main menu reacts again.
                int mode = _settings.Music ? 1 : 0;
                if (mode != _bgmModeAtEntry)
                {
                    if (mode == 0) _sound?.StopBgm();
                    else { _sound?.StopBgm(); _sound?.PlayBgm(BgmTracks.Title); }
                    _bgmModeAtEntry = mode;
                }
                _wait = MenuSwitchDelay;
                CurrentMenu = Menu.Main;
                _inThisMenu = false;
                break;
            case Menu.LeaveMusicDelay:
                _wait = MenuSwitchDelay;
                CurrentMenu = Menu.Option;
                _inThisMenu = false;
                break;
        }
        FrameRand++;
    }

    static bool IsOk(GameButtons pressed) => (pressed & (GameButtons.Ok | GameButtons.Shot)) != 0;
    static bool IsCancel(GameButtons pressed) => (pressed & GameButtons.Cancel) != 0;

    void Ring(GameButtons pressed, int max)
    {
        // each direction once per press; up wraps from 0 to max, down from max to 0
        if ((pressed & GameButtons.Up) != 0) _menuSel = _menuSel - 1 < 0 ? max : _menuSel - 1;
        if ((pressed & GameButtons.Down) != 0) _menuSel = _menuSel + 1 > max ? 0 : _menuSel + 1;
    }

    void MainMenu(GameButtons pressed)
    {
        int before = _menuSel;
        if (!_inThisMenu)
        {
            _inThisMenu = true;
            DrawMain();
        }
        Ring(pressed, _texts.MainChoices.Length - 1);
        if (_menuSel != before) DrawMain();

        if (IsOk(pressed))
        {
            switch (_menuSel)
            {
                case 0: Action = TitleAction.Start; break;
                case 1: Action = TitleAction.Continue; break;
                case 2: CurrentMenu = Menu.Option; _inThisMenu = false; break;
                case 3: Action = TitleAction.Quit; break;
            }
        }
        if (IsCancel(pressed)) Action = TitleAction.Quit;
    }

    void OptionMenu(GameButtons pressed)
    {
        int before = _menuSel;
        if (!_inThisMenu)
        {
            _inThisMenu = true;
            _menuSel = 0;
            before = 0;
            DrawOptions();
        }
        Ring(pressed, _texts.OptionChoices.Length - 1);
        int step = (pressed & GameButtons.Right) != 0 ? 1 : (pressed & GameButtons.Left) != 0 ? -1 : 0;
        bool changed = false;
        if (step != 0)
        {
            switch (_menuSel)
            {
                case 0: _settings.Rank = (Rank)(((int)_settings.Rank + step + 4) % 4); changed = true; break;
                case 1: _settings.Music = !_settings.Music; changed = true; break;
                case 2:
                    _settings.LivesExtra = (_settings.LivesExtra + step + GameSettings.LivesExtraMax + 1) % (GameSettings.LivesExtraMax + 1);
                    changed = true;
                    break;
            }
        }
        if (_menuSel != before || changed) DrawOptions();

        if ((IsOk(pressed) && _menuSel == 4) || IsCancel(pressed))
        {
            _save?.Invoke(_settings);
            CurrentMenu = Menu.LeaveOptionDelay;
            _inThisMenu = false;
            _menuSel = 2; // OPTION in the main menu
        }
        else if (IsOk(pressed) && _menuSel == 3)
        {
            CurrentMenu = Menu.Music;
            _inThisMenu = false;
            _menuSel = 0;
        }
    }

    void MusicMenu(GameButtons pressed)
    {
        int before = _menuSel;
        bool redraw = false;
        if (!_inThisMenu)
        {
            _inThisMenu = true;
            _menuSel = 0;
            before = 0;
            redraw = true;
            pressed &= ~(GameButtons.Ok | GameButtons.Shot); // the original clears OK/Shot on entry
        }
        Ring(pressed, _texts.MusicChoices.Length - 1);
        if (_menuSel == 0)
        {
            if ((pressed & GameButtons.Left) != 0) { _musicSel = _musicSel - 1 < 0 ? TrackCount - 1 : _musicSel - 1; redraw = true; }
            if ((pressed & GameButtons.Right) != 0) { _musicSel = _musicSel + 1 >= TrackCount ? 0 : _musicSel + 1; redraw = true; }
        }
        if (_menuSel != before || redraw) DrawMusic();

        if ((IsOk(pressed) && _menuSel == 1) || IsCancel(pressed))
        {
            CurrentMenu = Menu.LeaveMusicDelay;
            _inThisMenu = false;
            _menuSel = 3; // M.TEST in the option menu
        }
        else if (IsOk(pressed) && _menuSel == 0)
        {
            _sound?.StopBgm();
            _sound?.PlayBgm(_texts.MusicFiles[_musicSel]);
        }
    }

    // ---- drawing. The original draws each entry over a restored rectangle; redrawing the whole menu from page 1 looks the same.
    enum Weight { Normal, Heavy, Bold, Black }

    /// <summary>Text with the original's weights: the glyph is smeared 1 (heavy, bold) or 2 (black) pixels to the left. Bold's "keep the holes" detail is not reproduced.</summary>
    void DrawText(Framebuffer fb, string text, int x, int y, byte color, Weight w)
    {
        int smear = w switch { Weight.Normal => 0, Weight.Black => 2, _ => 1 };
        for (int d = smear; d >= 0; d--) TextRenderer.DrawString(fb, _font, text, x - d, y, color);
    }

    void UnputMenu(int rows)
    {
        // menu_unput: the area of `rows` entries, centred on the menu, put back from page 1
        _pages.Restore(MenuLeft, ChoiceTop(0, rows), MenuW, rows * ChoicePaddedH);
    }

    void DrawMain()
    {
        UnputMenu(ChoiceCountMax);
        int n = _texts.MainChoices.Length;
        for (int i = 0; i < n; i++)
            DrawText(_pages.Page0, _texts.MainChoices[i], MenuCenterX - MainChoiceW / 2, ChoiceTop(i, n), i == _menuSel ? ColActive : ColInactive, Weight.Black);
    }

    void DrawOptions()
    {
        UnputMenu(ChoiceCountMax);
        int n = _texts.OptionChoices.Length;
        string[] lives = ["   3  ", "   4  ", "   5  ", "   6  ", "   7  "];
        for (int i = 0; i < n; i++)
        {
            string value = i switch
            {
                0 => _texts.Ranks[(int)_settings.Rank],
                1 => _texts.BgmModes[_settings.Music ? 1 : 0],
                2 => lives[Math.Clamp(_settings.LivesExtra, 0, lives.Length - 1)],
                _ => "",
            };
            DrawText(_pages.Page0, _texts.OptionChoices[i] + value, MenuCenterX - MenuW / 2, ChoiceTop(i, n), i == _menuSel ? ColActive : ColInactive, Weight.Black);
        }
    }

    void DrawMusic()
    {
        UnputMenu(ChoiceCountMax);
        // music_choice_top: entries are 40 px apart; the first one has the track title on the line below it
        int top0 = ChoiceTop(0, 3), left = MenuCenterX - MenuW / 2;
        byte c0 = _menuSel == 0 ? ColActive : ColInactive, c1 = _menuSel == 1 ? ColActive : ColInactive;
        DrawText(_pages.Page0, _texts.MusicChoices[0] + _musicSel.ToString("00"), left, top0, c0, Weight.Black);
        DrawText(_pages.Page0, _texts.MusicTitles[_musicSel], left, top0 + ChoicePaddedH, c0, Weight.Black);
        DrawText(_pages.Page0, _texts.MusicChoices[1], left, top0 + 2 * ChoicePaddedH, c1, Weight.Black);
    }

    public void Render(Framebuffer fb) => fb.CopyFrom(_pages.Page0);
}
