using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Microsoft.Xna.Framework.Input;
using Th01.Core.Game;
using Th01.Core.Graphics;
using Th01.Core.Input;
using Th01.Core.Sim;
using Th01.Core.Timing;
using Th01.Platform.MonoGame.Launcher;

namespace Th01.Platform.MonoGame;

/// <summary>
/// Window, input, timing and presentation. All screens and gameplay come from a <see cref="GameFlow"/> created by an
/// injectable factory after the graphics device exists; with no factory (or when it fails) a test pattern is shown.
/// Hotkeys: F11 fullscreen, F10 scale mode (integer / fit / stretch), F5 pause, F6 step one tick (pauses),
/// F3 debug read-out in the title, F12 screenshot (PNG, user data folder), F9 missing-art list, F7 record inputs (press again to save), F8 replay the saved inputs.
/// </summary>
public sealed class Th01Game : Game
{
    /// <summary>Shown when no game data is loaded (test pattern): it must not look like the original game.</summary>
    const string BaseTitle = "Th01 (unofficial fan port)";
    /// <summary>Window title once the player's own copy of the game is loaded.</summary>
    const string GameTitle = "Touhou Reiiden ~ Highly Responsive to Prayers";

    readonly GraphicsDeviceManager _graphics;
    readonly Func<GameFlow?>? _flowFactory;
    readonly Framebuffer _fb = new();
    readonly uint[] _rgba = new uint[Framebuffer.Width * Framebuffer.Height];
    readonly FixedTimestep _timestep = new();
    readonly InputBindings _bindings = InputBindings.Load();
    readonly RateCounter _tickRate = new();
    readonly string _scriptPath = Path.Combine(Directory.GetCurrentDirectory(), InputScript.FileName);
    GameFlow? _flow;
    string _status = "test pattern";
    ScaleMode _scaleMode = ScaleMode.Integer;
    bool _showDebug;
    KeyboardState _prevKb;
    InputScript? _recording;
    InputScript? _replay;
    int _flowTick;
    Texture2D? _tex;
    SpriteBatch? _batch;

    /// <summary>Where F12 puts PNG screenshots (outside the repository: the user data folder). Null disables F12.</summary>
    public string? ScreenshotDirectory { get; init; }

    /// <summary>Sends the mixer to the sound card; pumped once per frame. Disposed with the game.</summary>
    public MonoGameAudioOutput? AudioOutput { get; init; }

    /// <summary>Failed art lookups (F9 writes them to <see cref="MissingArtPath"/>, as does closing the game if there are any).</summary>
    public MissingArtLog? MissingArt { get; init; }
    public string? MissingArtPath { get; init; }

    bool _screenshotRequested;

    /// <summary>The launcher shown before the game; null starts the game at once (--stage, --cutscene, tests).</summary>
    public LauncherModel? Launcher { get; init; }
    /// <summary>Show the launcher first (false for --stage / --cutscene: the model still carries the player's settings).</summary>
    public bool ShowLauncher { get; init; } = true;
    /// <summary>Sound mixer whose two volumes the launcher sets.</summary>
    public Th01.Audio.Playback.AudioMixer? Mixer { get; init; }
    /// <summary>The font in use; it is told about the FONT.ROM settings whenever they change.</summary>
    public LauncherFonts? Fonts { get; init; }
    /// <summary>Folder of the chosen game (a FONT.ROM may be there).</summary>
    public Func<string?>? GameFolder { get; init; }
    /// <summary>Gamepad status and report files for the options screens.</summary>
    public LauncherServices? LauncherSvc { get; init; }
    /// <summary>Picks the game when the launcher asks (DIRECTORY / Change / Select game).</summary>
    public IGameChooser? GameChooser { get; init; }
    /// <summary>Ask for the game as soon as the launcher opens (--select).</summary>
    public bool OpenChooserAtStart { get; init; }
    /// <summary>Font for the launcher text (kana and kanji included).</summary>
    public IFontSource? LauncherFont { get; init; }
    readonly LauncherCanvas _lcanvas = new();
    readonly uint[] _lrgba = new uint[LauncherCanvas.Width * LauncherCanvas.Height];
    readonly LauncherInputState _linput = new();
    Texture2D? _ltex;
    ImagePresenter? _presenter;
    int _filter;
    LauncherApplier _applier = null!;
    bool _inLauncher;

    /// <summary>Set when an unhandled error stopped the game; Program turns it into the exit code.</summary>
    public Exception? Fatal { get; private set; }

    public Th01Game(Func<GameFlow?>? flowFactory = null)
    {
        _flowFactory = flowFactory;
        _applier = new LauncherApplier(new Surface(this));
        _graphics = new GraphicsDeviceManager(this)
        {
            PreferredBackBufferWidth = Framebuffer.Width * 2,
            PreferredBackBufferHeight = Framebuffer.Height * 2,
            SynchronizeWithVerticalRetrace = true,
            HardwareModeSwitch = false, // borderless fullscreen: no display mode change
        };
        // We drive ticks ourselves from TickRate; MonoGame's fixed step is not used.
        IsFixedTimeStep = false;
        Window.AllowUserResizing = true;
        UpdateTitle();
    }

    protected override void LoadContent()
    {
        _batch = new SpriteBatch(GraphicsDevice);
        _tex = new Texture2D(GraphicsDevice, Framebuffer.Width, Framebuffer.Height, false, SurfaceFormat.Color);
        _ltex = new Texture2D(GraphicsDevice, LauncherCanvas.Width, LauncherCanvas.Height, false, SurfaceFormat.Color);
        _presenter = new ImagePresenter(GraphicsDevice);
        Launcher?.Attach(LauncherSvc, _bindings, () => GameSettings.Load(null, GameFolder?.Invoke()));
        ApplyLauncherSettings();
        _inLauncher = ShowLauncher && Launcher is not null && LauncherFont is not null;
        if (_inLauncher)
        {
            _status = "launcher";
            UpdateTitle();
            if (OpenChooserAtStart) Launcher!.ApplyChoice(GameChooser?.Choose());
        }
        else StartFlow();
    }

    /// <summary>(Re)creates the flow from the factory. Without a factory, or if it fails, the test pattern is shown.</summary>
    void StartFlow()
    {
        _flowTick = 0;
        _flow = null;
        if (_flowFactory is not null)
        {
            try { _flow = _flowFactory(); }
            catch (Exception e)
            {
                Console.Error.WriteLine($"Could not start the game, showing the test pattern instead: {e.Message}");
                _status = "test pattern (could not start the game: see console)";
            }
        }
        if (_flow is null)
        {
            TestPattern.Draw(_fb);
            if (_flowFactory is null) _status = "test pattern";
        }
        else _status = "running";
        UpdateTitle();
    }

    protected override void Update(GameTime gameTime)
    {
        try
        {
            long elapsedUs = gameTime.ElapsedGameTime.Ticks / 10; // 100 ns -> microseconds
            var kb = Keyboard.GetState();
            HandleHotkeys(kb);
            _prevKb = kb;

            var pad = GamePad.GetState(PlayerIndex.One, GamePadDeadZone.None); // deadzone is ours (InputBindings)
            IsMouseVisible = _inLauncher;   // the pointer is for the launcher only
            if (_inLauncher)
            {
                UpdateLauncher(kb, pad, elapsedUs);
                AudioOutput?.Update();
                base.Update(gameTime);
                return;
            }
            var live = _bindings.Poll(kb, pad);
            // Esc quits from the logo and the main title menu; on other screens it is just "cancel" (the bindings handle it).
            if (kb.IsKeyDown(Keys.Escape) && (_flow is null || _flow.EscapeQuits)) { Exit(); return; }

            int ticks = _timestep.Advance(elapsedUs);
            if (_flow is not null)
                for (int i = 0; i < ticks && !_flow.QuitRequested; i++)
                    _flow.Tick(NextInput(live));
            if (_flow?.QuitRequested == true) { Exit(); return; }
            _tickRate.Add(ticks, elapsedUs);
            if (_showDebug) UpdateTitle();
        }
        catch (Exception e) { Fail(e); }
        AudioOutput?.Update();
        base.Update(gameTime);
    }

    /// <summary>Applies the launcher's video, audio and input settings whenever they changed (draw-only options never touch the simulation).</summary>
    void ApplyLauncherSettings()
    {
        if (Launcher is null) return;
        _applier.Apply(Launcher.Settings, Launcher.Revision, GameFolder?.Invoke());
    }

    /// <summary>The window, mixer, key map and font as the launcher's settings see them.</summary>
    sealed class Surface(Th01Game g) : ILauncherSurface
    {
        public bool IsFullscreen => g._graphics.IsFullScreen;
        public void SetFullscreen(bool on) { if (g._graphics.IsFullScreen != on) g._graphics.ToggleFullScreen(); }
        public void SetWindowClientSize(int width, int height)
        {
            g._graphics.PreferredBackBufferWidth = width;
            g._graphics.PreferredBackBufferHeight = height;
            g._graphics.ApplyChanges();
        }
        public void SetIntegerScale(bool on) => g._scaleMode = on ? ScaleMode.Integer : ScaleMode.Fit;
        public void SetFilter(int filter) => g._filter = filter;
        public void SetVolumes(float music, float effects)
        {
            if (g.Mixer is null) return;
            g.Mixer.MusicLevel = music;
            g.Mixer.EffectLevel = effects;
        }
        public void SetNumpad(bool on) => g._bindings.Numpad = on;
        public void ConfigureFonts(string? romPath, bool preferRom, string? gameFolder) => g.Fonts?.Configure(romPath, preferRom, gameFolder);
        public void SetRecordMissingArt(bool on) { if (g.MissingArt is not null) g.MissingArt.Recording = on; }
    }

    readonly LauncherHitMap _hits = new();
    MouseState _lprevMouse;
    bool _mouseSeen;

    /// <summary>
    /// The mouse in the launcher: hover focuses what is under it (only when it moves, so the keyboard keeps its say), left click is
    /// Select, right click is Back, the wheel scrolls the options list. The game itself never reads the mouse.
    /// Returns true when the launcher was left (Play / Quit) and the rest of the update must be skipped.
    /// </summary>
    bool HandleLauncherMouse(long elapsedUs)
    {
        var ms = Mouse.GetState();
        var prev = _lprevMouse;
        bool had = _mouseSeen;
        _lprevMouse = ms;
        _mouseSeen = true;
        if (!had || !IsActive) return false;
        var vp = GraphicsDevice.Viewport;
        var pos = LauncherViewport.ToCanvas(ms.X, ms.Y, vp.Width, vp.Height);
        HitTarget? target = pos is { } p ? _hits.At(p.X, p.Y) : null;
        if (ms.X != prev.X || ms.Y != prev.Y) Launcher!.Hover(target);
        int wheel = ms.ScrollWheelValue - prev.ScrollWheelValue;
        if (wheel != 0) Launcher!.Scroll(wheel > 0 ? -1 : 1);   // wheel up moves the list up
        var result = LauncherResult.None;
        if (ms.LeftButton == ButtonState.Pressed && prev.LeftButton == ButtonState.Released) result = Launcher!.Click(target, false);
        if (ms.RightButton == ButtonState.Pressed && prev.RightButton == ButtonState.Released)
        {
            var back = Launcher!.Click(target, true);
            if (result == LauncherResult.None) result = back;
        }
        switch (result)
        {
            case LauncherResult.Play: _inLauncher = false; StartFlow(); return true;
            case LauncherResult.Quit: Exit(); return true;
            case LauncherResult.ChooseGame: Launcher!.ApplyChoice(GameChooser?.Choose()); break;
        }
        return false;
    }

    KeyboardState _lprevKb;
    GamePadState _lprevPad;
    bool _lwasListening, _lwaitRelease;

    /// <summary>Launcher: the keys and pad become presses, once per timestep tick, and the model runs the screens.</summary>
    void UpdateLauncher(KeyboardState kb, GamePadState pad, long elapsedUs)
    {
        bool K(params Keys[] keys) => keys.Any(kb.IsKeyDown);
        bool P(Buttons b) => pad.IsButtonDown(b);
        if (LauncherSvc is not null)
        {
            LauncherSvc.GamepadConnected = pad.IsConnected;
            LauncherSvc.GamepadName = pad.IsConnected ? GamePad.GetCapabilities(PlayerIndex.One).DisplayName : null;
        }
        ApplyLauncherSettings();
        if (HandleLauncherMouse(elapsedUs)) return;
        var held = LauncherKey.None;
        if (Launcher!.Listening)
        {
            // Remapping: every key (or pad button) but Esc is the new binding; Esc cancels.
            _lwasListening = true;
            if (K(Keys.Escape)) held |= LauncherKey.Back;
            else if (Launcher.ListeningForButton)
            {
                foreach (Buttons b in Enum.GetValues<Buttons>())
                    if (b != Buttons.None && pad.IsButtonDown(b) && !_lprevPad.IsButtonDown(b)) { Launcher.OfferButton(b); break; }
            }
            else
            {
                foreach (var k in kb.GetPressedKeys())
                    if (!_lprevKb.IsKeyDown(k)) { Launcher.OfferKey(k); break; }
            }
            _lprevKb = kb; _lprevPad = pad;
            if (held == LauncherKey.None) { _linput.Step(LauncherKey.None); _timestep.Advance(elapsedUs); return; }
        }
        if (K(Keys.Up) || P(Buttons.DPadUp) || pad.ThumbSticks.Left.Y > 0.5f) held |= LauncherKey.Up;
        if (K(Keys.Down) || P(Buttons.DPadDown) || pad.ThumbSticks.Left.Y < -0.5f) held |= LauncherKey.Down;
        if (K(Keys.Left) || P(Buttons.DPadLeft) || pad.ThumbSticks.Left.X < -0.5f) held |= LauncherKey.Left;
        if (K(Keys.Right) || P(Buttons.DPadRight) || pad.ThumbSticks.Left.X > 0.5f) held |= LauncherKey.Right;
        if (K(Keys.Z, Keys.Enter) || P(Buttons.A)) held |= LauncherKey.Select;
        if (K(Keys.X, Keys.Escape) || P(Buttons.B)) held |= LauncherKey.Back;
        if (K(Keys.Q) || P(Buttons.LeftShoulder)) held |= LauncherKey.TabPrev;
        if (K(Keys.E) || P(Buttons.RightShoulder)) held |= LauncherKey.TabNext;

        _lprevKb = kb; _lprevPad = pad;
        if (_lwasListening && !Launcher.Listening) { _lwasListening = false; _lwaitRelease = true; }
        if (_lwaitRelease) { if (held != LauncherKey.None) held = LauncherKey.None; else _lwaitRelease = false; }

        int ticks = _timestep.Advance(elapsedUs);
        for (int i = 0; i < ticks; i++)
        {
            Launcher!.Step();
            var pressed = _linput.Step(held);
            held = LauncherKey.None; // a long frame must not press twice
            foreach (LauncherKey one in Enum.GetValues<LauncherKey>())
            {
                if (one == LauncherKey.None || !pressed.HasFlag(one)) continue;
                switch (Launcher.Press(one))
                {
                    case LauncherResult.Play: _inLauncher = false; StartFlow(); return;
                    case LauncherResult.Quit: Exit(); return;
                    case LauncherResult.ChooseGame:
                        Launcher.ApplyChoice(GameChooser?.Choose()); // a native dialog: the window waits for it
                        return;
                }
            }
        }
    }

    /// <summary>Input for this tick: the script while replaying, else the live input (recorded if recording).</summary>
    GameButtons NextInput(GameButtons live)
    {
        GameButtons input = live;
        if (_replay is not null)
        {
            if (_flowTick < _replay.TickCount) input = _replay.At(_flowTick);
            else { _replay = null; _status = "replay finished, live input"; UpdateTitle(); }
        }
        _recording?.Append(input);
        _flowTick++;
        return input;
    }

    void HandleHotkeys(KeyboardState kb)
    {
        bool Pressed(Keys k) => kb.IsKeyDown(k) && !_prevKb.IsKeyDown(k);
        if (Pressed(Keys.F11))
        {
            _graphics.ToggleFullScreen();
            // The scale mode follows the launcher's "whole-number scaling" setting; F10 still cycles the modes.
            Launcher?.SetFullscreen(_graphics.IsFullScreen);
        }
        if (Pressed(Keys.F10))
        {
            _scaleMode = (ScaleMode)(((int)_scaleMode + 1) % 3);
            _status = $"scale: {_scaleMode}";
            UpdateTitle();
        }
        if (Pressed(Keys.F5)) { _timestep.Paused = !_timestep.Paused; UpdateTitle(); }
        if (Pressed(Keys.F6)) { _timestep.Paused = true; _timestep.RequestStep(); UpdateTitle(); }
        if (Pressed(Keys.F12)) _screenshotRequested = true;
        if (Pressed(Keys.F9)) SaveMissingArt();
        if (Pressed(Keys.F3)) { _showDebug = !_showDebug; UpdateTitle(); }
        if (Pressed(Keys.F7) && _flow is not null) ToggleRecording();
        if (Pressed(Keys.F8) && _flow is not null) StartReplay();
    }

    void ToggleRecording()
    {
        if (_recording is not null)
        {
            try
            {
                _recording.Save(_scriptPath);
                _status = $"saved {_recording.TickCount} ticks to {InputScript.FileName}";
            }
            catch (Exception e) when (e is IOException or UnauthorizedAccessException)
            {
                _status = "could not save the input script";
                Console.Error.WriteLine($"Could not save {_scriptPath}: {e.Message}");
            }
            _recording = null;
        }
        else
        {
            StartFlow(); // a recording must begin at tick 0 of a fresh flow
            _replay = null;
            _recording = new InputScript();
            _status = "RECORDING (F7 to stop and save)";
        }
        UpdateTitle();
    }

    void StartReplay()
    {
        _recording = null;
        var script = InputScript.TryLoad(_scriptPath, out string? error);
        if (script is null)
        {
            _status = $"cannot replay: {error}";
            Console.Error.WriteLine(_status);
            UpdateTitle();
            return;
        }
        StartFlow();
        _replay = script;
        _status = $"replaying {script.TickCount} ticks";
        UpdateTitle();
    }

    protected override void Draw(GameTime gameTime)
    {
        try
        {
            GraphicsDevice.Clear(Color.Black);
            if (_inLauncher)
            {
                LauncherView.Render(Launcher!, _lcanvas, LauncherFont!, _hits);
                _lcanvas.ToRgba(_lrgba);
                _ltex!.SetData(_lrgba);
                var lv = GraphicsDevice.Viewport;
                var (fx, fy, lw, lh) = LauncherViewport.Fit(lv.Width, lv.Height);
                // The same filter path as the game, so a change in the options shows in the launcher at once (read from the live setting).
                _presenter!.Present(_batch!, _ltex, new Rectangle(fx, fy, lw, lh), (ImageFilter)Launcher!.Settings.Filter);
                base.Draw(gameTime);
                return;
            }
            _flow?.Render(_fb);
            _fb.ToRgba(_rgba);
            // Framebuffer holds 0xAARRGGBB; MonoGame's Color layout is R,G,B,A in memory (0xAABBGGRR as uint).
            for (int i = 0; i < _rgba.Length; i++)
            {
                uint p = _rgba[i];
                _rgba[i] = (p & 0xFF00FF00u) | ((p & 0xFF) << 16) | ((p >> 16) & 0xFF);
            }
            _tex!.SetData(_rgba);
            if (_screenshotRequested) { _screenshotRequested = false; SaveScreenshot(); }

            var vp = GraphicsDevice.Viewport;
            var r = ViewportLayout.Compute(vp.Width, vp.Height, _scaleMode);
            var dest = new Rectangle(r.X, r.Y, r.Width, r.Height);
            _presenter!.Present(_batch!, _tex, dest, (ImageFilter)_filter);
        }
        catch (Exception e) { Fail(e); }
        base.Draw(gameTime);
    }

    void SaveMissingArt()
    {
        if (MissingArt is null || MissingArtPath is null) return;
        if (MissingArt.Save(MissingArtPath)) _status = $"{MissingArt.Count} missing art item(s) written to {MissingArtPath}";
        else _status = MissingArt.Count == 0 ? "no missing art so far" : "could not write the missing art list";
        Console.WriteLine(_status);
        UpdateTitle();
    }

    protected override void OnExiting(object sender, ExitingEventArgs args)
    {
        if (Launcher?.Settings.RecordMissingArt != false && MissingArt is { Count: > 0 } && MissingArtPath is not null && MissingArt.Save(MissingArtPath))
            Console.WriteLine($"{MissingArt.Count} missing art item(s) written to {MissingArtPath}");
        base.OnExiting(sender, args);
    }

    void SaveScreenshot()
    {
        if (ScreenshotDirectory is null) return;
        try
        {
            Directory.CreateDirectory(ScreenshotDirectory);
            string path = Path.Combine(ScreenshotDirectory, $"reiiden-{DateTime.Now:yyyyMMdd-HHmmss-fff}.png");
            using (var fs = File.Create(path)) _tex!.SaveAsPng(fs, Framebuffer.Width, Framebuffer.Height);
            _status = $"screenshot saved: {path}";
            Console.WriteLine(_status);
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            _status = "could not save the screenshot";
            Console.Error.WriteLine($"Could not save a screenshot to {ScreenshotDirectory}: {e.Message}");
        }
        UpdateTitle();
    }

    void UpdateTitle()
    {
        string t = _flow is null ? $"{BaseTitle} - {_status}" : GameTitle;
        if (_flow is not null && _status != "running") t += $" - {_status}";
        if (_timestep.Paused) t += " [PAUSED]";
        if (_showDebug) t += $" | {_tickRate.PerSecond} ticks/s (target {TickRate.Hz:F2}) | {_tickRate.Total} ticks";
        Window.Title = t;
    }

    void Fail(Exception e)
    {
        if (Fatal is not null) return;
        Fatal = e;
        Exit();
    }
}
