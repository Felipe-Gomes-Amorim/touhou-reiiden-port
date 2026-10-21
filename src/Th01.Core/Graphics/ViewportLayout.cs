namespace Th01.Core.Graphics;

public enum ScaleMode
{
    /// <summary>Largest whole-number scale that fits, centred with black bars (falls back to Fit if the window is smaller than 640x400).</summary>
    Integer,
    /// <summary>Largest scale that fits keeping 16:10, centred.</summary>
    Fit,
    /// <summary>Fill the whole window, ignoring aspect ratio.</summary>
    Stretch,
}

public readonly record struct PixelRect(int X, int Y, int Width, int Height);

/// <summary>Where the 640x400 picture goes inside a window. Pure maths so it is testable without a window.</summary>
public static class ViewportLayout
{
    public static PixelRect Compute(int windowWidth, int windowHeight, ScaleMode mode)
    {
        if (windowWidth <= 0 || windowHeight <= 0) return new PixelRect(0, 0, 0, 0);
        if (mode == ScaleMode.Stretch) return new PixelRect(0, 0, windowWidth, windowHeight);

        int w, h;
        int whole = Math.Min(windowWidth / Framebuffer.Width, windowHeight / Framebuffer.Height);
        if (mode == ScaleMode.Integer && whole >= 1)
        {
            w = Framebuffer.Width * whole;
            h = Framebuffer.Height * whole;
        }
        else if ((long)windowWidth * Framebuffer.Height <= (long)windowHeight * Framebuffer.Width)
        {
            w = windowWidth;
            h = Math.Max(1, windowWidth * Framebuffer.Height / Framebuffer.Width);
        }
        else
        {
            h = windowHeight;
            w = Math.Max(1, windowHeight * Framebuffer.Width / Framebuffer.Height);
        }
        return new PixelRect((windowWidth - w) / 2, (windowHeight - h) / 2, w, h);
    }
}
