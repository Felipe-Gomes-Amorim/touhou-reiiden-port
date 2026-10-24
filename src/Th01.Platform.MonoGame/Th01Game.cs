using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Microsoft.Xna.Framework.Input;
using Th01.Core.Game;
using Th01.Core.Graphics;
using Th01.Core.Input;
using Th01.Core.Sim;
using Th01.Core.Timing;

namespace Th01.Platform.MonoGame;

/// <summary>
/// Window, input, timing and presentation. All screens and gameplay come from a <see cref="GameFlow"/> created by an
/// injectable factory after the graphics device exists; with no factory (or when it fails) a test pattern is shown.
/// Hotkeys: F11 fullscreen, F10 scale mode (integer / fit / stretch), F5 pause, F6 step one tick (pauses),
/// F3 debug read-out in the title, F12 screenshot (PNG, user data folder), F9 missing-art list, F7 record inputs (press again to save), F8 replay the saved inputs.
/// </summary>
public sealed class Th01Game : Game
{
    const string BaseTitle = "Th01 (unofficial fan port)";

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
        StartFlow();
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
        if (Pressed(Keys.F11)) _graphics.ToggleFullScreen();
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
        string t = $"{BaseTitle} - {_status}";
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
