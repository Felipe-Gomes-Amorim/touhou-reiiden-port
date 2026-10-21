using Microsoft.Xna.Framework.Input;
using Th01.Core.Input;
using Th01.Platform.MonoGame;

namespace Th01.Platform.Tests;

public class InputTests
{
    [Fact]
    public void Stick_InsideDeadzone_IsNothing_OutsideMapsToDirections()
    {
        Assert.Equal(GameButtons.None, InputBindings.StickToDirections(0.2f, 0.2f, 0.3f));
        Assert.Equal(GameButtons.Left, InputBindings.StickToDirections(-0.9f, 0.1f, 0.3f));
        Assert.Equal(GameButtons.Up | GameButtons.Right, InputBindings.StickToDirections(0.5f, 0.5f, 0.3f));
        Assert.Equal(GameButtons.Down, InputBindings.StickToDirections(0f, -1f, 0.3f));
    }

    [Fact]
    public void Repeater_FiresOnPress_ThenAfterDelay_ThenEveryInterval()
    {
        var r = new ButtonRepeater(delayTicks: 5, intervalTicks: 2);
        var fired = new List<int>();
        for (int t = 0; t < 12; t++)
            if ((r.Update(GameButtons.Down) & GameButtons.Down) != 0) fired.Add(t);
        Assert.Equal([0, 5, 7, 9, 11], fired);

        r.Update(GameButtons.None);
        Assert.Equal(GameButtons.Down, r.Update(GameButtons.Down)); // release resets
    }

    [Fact]
    public void Bindings_RoundTripThroughFile()
    {
        string dir = Path.Combine(Path.GetTempPath(), "th01-input-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        try
        {
            var b = new InputBindings { Deadzone = 0.5f };
            b.RebindKey(GameButtons.Shot, Keys.A);
            b.RebindPadButton(GameButtons.Bomb, Buttons.Y);
            b.Save(Path.Combine(dir, InputBindings.FileName));

            var loaded = InputBindings.Load(dir);
            Assert.Equal(0.5f, loaded.Deadzone);
            Assert.Equal([Keys.A], loaded.Keyboard[GameButtons.Shot]);
            Assert.Equal([Buttons.Y], loaded.Gamepad[GameButtons.Bomb]);
            Assert.Equal([Keys.Up], loaded.Keyboard[GameButtons.Up]);
        }
        finally { Directory.Delete(dir, true); }
    }
}
