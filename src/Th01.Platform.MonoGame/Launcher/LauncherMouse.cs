namespace Th01.Platform.MonoGame.Launcher;

public enum HitKind { HomeItem, Language, Tab, Row, BarCell, Back, Header }

/// <summary>What a screen region means. A and B depend on the kind: HomeItem = item; Language = 0 English / 1 Japanese;
/// Tab = tab index; Row = absolute row; BarCell = row, cell 1..10; Back = nothing.</summary>
public readonly record struct HitTarget(HitKind Kind, int A = 0, int B = 0);

/// <summary>
/// The clickable regions of the screen as last drawn. The view fills it while drawing, so the hit-test can never disagree with
/// what the player sees. Later entries sit on top of earlier ones.
/// </summary>
public sealed class LauncherHitMap
{
    readonly List<(int X, int Y, int W, int H, HitTarget Target)> _items = [];

    public int Count => _items.Count;
    public void Clear() => _items.Clear();
    public void Add(int x, int y, int w, int h, HitTarget target) => _items.Add((x, y, w, h, target));

    public HitTarget? At(int x, int y)
    {
        for (int i = _items.Count - 1; i >= 0; i--)
        {
            var (ix, iy, w, h, t) = _items[i];
            if (x >= ix && x < ix + w && y >= iy && y < iy + h) return t;
        }
        return null;
    }
}

/// <summary>Where the 1280x720 launcher picture sits in the window, and the way back from a window position to the picture.</summary>
public static class LauncherViewport
{
    /// <summary>Largest 16:9 box that fits the window, centred (black bars on the other sides).</summary>
    public static (int X, int Y, int Width, int Height) Fit(int windowW, int windowH)
    {
        double s = Math.Min(windowW / (double)LauncherCanvas.Width, windowH / (double)LauncherCanvas.Height);
        int w = (int)(LauncherCanvas.Width * s), h = (int)(LauncherCanvas.Height * s);
        return ((windowW - w) / 2, (windowH - h) / 2, w, h);
    }

    /// <summary>Window pixel to launcher picture pixel; null over the black bars or outside the window.</summary>
    public static (int X, int Y)? ToCanvas(int mouseX, int mouseY, int windowW, int windowH)
    {
        var (x, y, w, h) = Fit(windowW, windowH);
        if (w <= 0 || h <= 0 || mouseX < x || mouseY < y || mouseX >= x + w || mouseY >= y + h) return null;
        return ((mouseX - x) * LauncherCanvas.Width / w, (mouseY - y) * LauncherCanvas.Height / h);
    }
}

public enum InputDevice { Keyboard, Mouse }

public sealed partial class LauncherModel
{
    /// <summary>Which device moved the focus last: keyboard/gamepad or mouse (one focus, the latest device wins).</summary>
    public InputDevice LastDevice { get; private set; } = InputDevice.Keyboard;

    /// <summary>The mouse is over the screen's header.</summary>
    public bool HeaderHover { get; private set; }

    /// <summary>"Reduce lights and motion" is on: nothing spins.</summary>
    public bool ReduceMotion => _settings.ReduceAll;

    /// <summary>The header orb turns while the mouse is on the header, or while the screen is being driven by keyboard/gamepad.</summary>
    public bool HeaderSpinning => !ReduceMotion && (HeaderHover || LastDevice == InputDevice.Keyboard);

    /// <summary>The orb of the focused menu item turns (focus and hover are the same thing here).</summary>
    public bool OrbSpinning => !ReduceMotion;

    /// <summary>Mouse over something: the focus goes there (home items, options rows). Tabs and the back button have no focus to move.</summary>
    public void Hover(HitTarget? target)
    {
        LastDevice = InputDevice.Mouse;
        HeaderHover = target is { Kind: HitKind.Header };
        if (target is not { } t || Listening || ShowingReport) return;
        if (Screen == LauncherScreen.Home)
        {
            if (t.Kind == HitKind.HomeItem && IsEnabled((HomeItem)t.A)) Focus = Array.IndexOf(HomeOrder, (HomeItem)t.A);
            else if (t.Kind == HitKind.Language) Focus = Array.IndexOf(HomeOrder, HomeItem.Language);
        }
        else if (Screen == LauncherScreen.Options && t.Kind is HitKind.Row or HitKind.BarCell)
        {
            int count = Rows.Count;
            if (t.A >= 0 && t.A < count && t.A != Row) { Row = t.A; _confirmRestore = false; }
        }
    }

    /// <summary>A click: left acts like Select on what is under the pointer, right acts like Back. Returns what the host must do (Play, Quit, ChooseGame).</summary>
    public LauncherResult Click(HitTarget? target, bool right)
    {
        var result = ClickCore(target, right);
        LastDevice = InputDevice.Mouse;   // Press (used inside) marks the keyboard
        return result;
    }

    LauncherResult ClickCore(HitTarget? target, bool right)
    {
        if (right) return Press(LauncherKey.Back);
        if (ShowingReport) { Press(LauncherKey.Back); return LauncherResult.None; }   // any click closes the report
        if (Listening || target is not { } t) return LauncherResult.None;
        if (t.Kind == HitKind.Back) return Press(LauncherKey.Back);

        switch (Screen)
        {
            case LauncherScreen.Home:
                if (t.Kind == HitKind.Language)
                {
                    SetLanguage(t.A == 0 ? LauncherLanguage.English : LauncherLanguage.Japanese);
                    Focus = Array.IndexOf(HomeOrder, HomeItem.Language);
                }
                else if (t.Kind == HitKind.HomeItem && IsEnabled((HomeItem)t.A))
                {
                    Focus = Array.IndexOf(HomeOrder, (HomeItem)t.A);
                    return Press(LauncherKey.Select);
                }
                break;
            case LauncherScreen.Options:
                if (t.Kind == HitKind.Tab) SelectTab((OptionsTab)t.A);
                else if (t.Kind == HitKind.Row && t.A < Rows.Count) { Row = t.A; Press(LauncherKey.Select); }
                else if (t.Kind == HitKind.BarCell && t.A < Rows.Count)
                {
                    Row = t.A;
                    var id = Rows[t.A].Id;
                    if (id == RowId.Music) _settings.MusicVolume = Math.Clamp(t.B, 0, 10);
                    else if (id == RowId.Effects) _settings.EffectsVolume = Math.Clamp(t.B, 0, 10);
                    else break;
                    Changed();
                }
                break;
            case LauncherScreen.About:
                break;
        }
        return LauncherResult.None;
    }

    /// <summary>Mouse wheel over the options: moves the visible window of the list (positive = down). The focused row stays inside it.</summary>
    public void Scroll(int notches)
    {
        if (Screen != LauncherScreen.Options || ShowingReport || Listening || notches == 0) return;
        int max = Math.Max(0, Rows.Count - VisibleRows);
        RowScroll = Math.Clamp(RowScroll + notches, 0, max);
        if (Row < RowScroll) Row = RowScroll;
        else if (Row >= RowScroll + VisibleRows) Row = RowScroll + VisibleRows - 1;
    }
}
