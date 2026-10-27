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
    bool _inLauncher;

    /// <summary>Set when an unhandled error stopped the game; Program turns it into the exit code.</summary>
    public Exception? Fatal { get; private set; }

    public Th01Game(Func<GameFlow?>? flowFactory = null)
    {
        _flowFactory = flowFactory;
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
        _inLauncher = Launcher is not null && LauncherFont is not null;
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

    /// <summary>Launcher: the keys and pad become presses, once per timestep tick, and the model runs the screens.</summary>
    void UpdateLauncher(KeyboardState kb, GamePadState pad, long elapsedUs)
    {
        bool K(params Keys[] keys) => keys.Any(kb.IsKeyDown);
        bool P(Buttons b) => pad.IsButtonDown(b);
        var held = LauncherKey.None;
        if (K(Keys.Up) || P(Buttons.DPadUp) || pad.ThumbSticks.Left.Y > 0.5f) held |= LauncherKey.Up;
        if (K(Keys.Down) || P(Buttons.DPadDown) || pad.ThumbSticks.Left.Y < -0.5f) held |= LauncherKey.Down;
        if (K(Keys.Left) || P(Buttons.DPadLeft) || pad.ThumbSticks.Left.X < -0.5f) held |= LauncherKey.Left;
        if (K(Keys.Right) || P(Buttons.DPadRight) || pad.ThumbSticks.Left.X > 0.5f) held |= LauncherKey.Right;
        if (K(Keys.Z, Keys.Enter) || P(Buttons.A)) held |= LauncherKey.Select;
        if (K(Keys.X, Keys.Escape) || P(Buttons.B)) held |= LauncherKey.Back;
        if (K(Keys.Q) || P(Buttons.LeftShoulder)) held |= LauncherKey.TabPrev;
        if (K(Keys.E) || P(Buttons.RightShoulder)) held |= LauncherKey.TabNext;

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
            // Fullscreen fills the screen height (16:10 picture, black bars at the sides if the screen is wider);
            // windowed keeps whole-number scales. F10 still cycles the modes.
            _scaleMode = _graphics.IsFullScreen ? ScaleMode.Fit : ScaleMode.Integer;
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
                LauncherView.Render(Launcher!, _lcanvas, LauncherFont!);
                _lcanvas.ToRgba(_lrgba);
                _ltex!.SetData(_lrgba);
                var lv = GraphicsDevice.Viewport;
                double ls = Math.Min(lv.Width / (double)LauncherCanvas.Width, lv.Height / (double)LauncherCanvas.Height);
                int lw = (int)(LauncherCanvas.Width * ls), lh = (int)(LauncherCanvas.Height * ls);
                _batch!.Begin(samplerState: ls == Math.Floor(ls) ? SamplerState.PointClamp : SamplerState.LinearClamp);
                _batch.Draw(_ltex, new Rectangle((lv.Width - lw) / 2, (lv.Height - lh) / 2, lw, lh), Color.White);
                _batch.End();
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
            _batch!.Begin(samplerState: SamplerState.PointClamp); // filter off
            _batch.Draw(_tex, new Rectangle(r.X, r.Y, r.Width, r.Height), Color.White);
            _batch.End();
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
        if (MissingArt is { Count: > 0 } && MissingArtPath is not null && MissingArt.Save(MissingArtPath))
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
