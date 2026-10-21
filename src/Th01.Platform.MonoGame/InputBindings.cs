using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.Xna.Framework.Input;
using Th01.Core.Input;

namespace Th01.Platform.MonoGame;

/// <summary>
/// Maps keyboard keys and gamepad buttons/stick to <see cref="GameButtons"/>.
/// Defaults can be overridden (and are persisted by <see cref="Save"/>) in "input.local.json" (gitignored):
/// {"deadzone": 0.3, "keyboard": {"Shot": ["Z"]}, "gamepad": {"Shot": ["A"]}}
/// </summary>
public sealed class InputBindings
{
    public const string FileName = "input.local.json";
    public const float DefaultDeadzone = 0.3f;

    /// <summary>Left-stick deadzone, 0..0.95. Stick directions act as the D-pad once the axis passes it.</summary>
    public float Deadzone { get; set; } = DefaultDeadzone;

    string? _loadedFrom;

    public Dictionary<GameButtons, Keys[]> Keyboard { get; } = new()
    {
        [GameButtons.Up] = [Keys.Up],
        [GameButtons.Down] = [Keys.Down],
        [GameButtons.Left] = [Keys.Left],
        [GameButtons.Right] = [Keys.Right],
        [GameButtons.Shot] = [Keys.Z, Keys.Space],
        [GameButtons.Bomb] = [Keys.X],
        [GameButtons.Pause] = [Keys.P],
        [GameButtons.Ok] = [Keys.Enter, Keys.Z],
        [GameButtons.Cancel] = [Keys.Escape, Keys.X],
    };

    public Dictionary<GameButtons, Buttons[]> Gamepad { get; } = new()
    {
        [GameButtons.Up] = [Buttons.DPadUp],
        [GameButtons.Down] = [Buttons.DPadDown],
        [GameButtons.Left] = [Buttons.DPadLeft],
        [GameButtons.Right] = [Buttons.DPadRight],
        [GameButtons.Shot] = [Buttons.A],
        [GameButtons.Bomb] = [Buttons.B],
        [GameButtons.Pause] = [Buttons.Start],
        [GameButtons.Ok] = [Buttons.A],
        [GameButtons.Cancel] = [Buttons.B],
    };

    public static InputBindings Load(string? directory = null)
    {
        var b = new InputBindings();
        string path = Path.Combine(directory ?? AppContext.BaseDirectory, FileName);
        if (!File.Exists(path)) path = Path.Combine(Directory.GetCurrentDirectory(), FileName);
        if (!File.Exists(path)) return b;
        b._loadedFrom = path;
        try
        {
            using var doc = JsonDocument.Parse(File.ReadAllText(path));
            if (doc.RootElement.TryGetProperty("deadzone", out var dz) && dz.TryGetSingle(out float f))
                b.Deadzone = Math.Clamp(f, 0f, 0.95f);
            if (doc.RootElement.TryGetProperty("keyboard", out var kb)) Apply(kb, b.Keyboard);
            if (doc.RootElement.TryGetProperty("gamepad", out var gp)) Apply(gp, b.Gamepad);
        }
        catch (Exception e) when (e is JsonException or InvalidOperationException) { /* keep defaults */ }
        return b;
    }

    /// <summary>Writes the current bindings to input.local.json (where it was loaded from, else the current directory).</summary>
    public void Save(string? path = null)
    {
        path ??= _loadedFrom ?? Path.Combine(Directory.GetCurrentDirectory(), FileName);
        var model = new SavedFile(
            Deadzone,
            Keyboard.ToDictionary(p => p.Key.ToString(), p => p.Value.Select(k => k.ToString()).ToArray()),
            Gamepad.ToDictionary(p => p.Key.ToString(), p => p.Value.Select(g => g.ToString()).ToArray()));
        File.WriteAllText(path, JsonSerializer.Serialize(model, new JsonSerializerOptions { WriteIndented = true }));
        _loadedFrom = path;
    }

    sealed record SavedFile(
        [property: JsonPropertyName("deadzone")] float Deadzone,
        [property: JsonPropertyName("keyboard")] Dictionary<string, string[]> Keyboard,
        [property: JsonPropertyName("gamepad")] Dictionary<string, string[]> Gamepad);

    static void Apply<T>(JsonElement obj, Dictionary<GameButtons, T[]> target) where T : struct, Enum
    {
        foreach (var prop in obj.EnumerateObject())
        {
            if (!Enum.TryParse(prop.Name, true, out GameButtons btn)) continue;
            var list = new List<T>();
            foreach (var item in prop.Value.EnumerateArray())
                if (Enum.TryParse(item.GetString(), true, out T v)) list.Add(v);
            if (list.Count > 0) target[btn] = list.ToArray();
        }
    }

    /// <summary>Replaces the keyboard binding of a logical button with a single key (for a remap screen).</summary>
    public void RebindKey(GameButtons button, Keys key) => Keyboard[button] = [key];

    public void RebindPadButton(GameButtons button, Buttons pad) => Gamepad[button] = [pad];

    public GameButtons Poll(KeyboardState kb, GamePadState pad)
    {
        var result = GameButtons.None;
        foreach (var (btn, keys) in Keyboard)
            foreach (var k in keys) if (kb.IsKeyDown(k)) result |= btn;
        if (pad.IsConnected)
        {
            foreach (var (btn, buttons) in Gamepad)
                foreach (var g in buttons) if (pad.IsButtonDown(g)) result |= btn;
            result |= StickToDirections(pad.ThumbSticks.Left.X, pad.ThumbSticks.Left.Y, Deadzone);
        }
        return result;
    }

    /// <summary>Radial deadzone, then each axis counts as pressed past the threshold. Stick Y is up-positive.</summary>
    public static GameButtons StickToDirections(float x, float y, float deadzone)
    {
        if (x * x + y * y <= deadzone * deadzone) return GameButtons.None;
        var r = GameButtons.None;
        if (x <= -deadzone) r |= GameButtons.Left;
        if (x >= deadzone) r |= GameButtons.Right;
        if (y >= deadzone) r |= GameButtons.Up;
        if (y <= -deadzone) r |= GameButtons.Down;
        return r;
    }
}
