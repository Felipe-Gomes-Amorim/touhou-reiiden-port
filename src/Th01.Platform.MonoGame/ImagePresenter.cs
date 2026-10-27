using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;

namespace Th01.Platform.MonoGame;

/// <summary>
/// Puts a picture (the game's 640x400 or the launcher's 1280x720) on the window with the chosen <see cref="ImageFilter"/>.
/// The same path serves both, so changing the filter in the options shows at once in the launcher itself.
/// </summary>
public sealed class ImagePresenter(GraphicsDevice device) : IDisposable
{
    static readonly RasterizerState Scissor = new() { ScissorTestEnable = true, CullMode = CullMode.None };
    Texture2D? _pixel;
    IReadOnlyList<(int Offset, int Height)>? _bands;
    (int Lines, int Height) _bandsFor;

    /// <param name="dest">Where the picture goes in the window (letterbox and scale already worked out).</param>
    public void Present(SpriteBatch batch, Texture2D texture, Rectangle dest, ImageFilter filter)
    {
        var plan = FilterPlan.Choose(filter, dest.Width / (double)texture.Width);
        if (plan.TwoTap) DrawSmooth(batch, texture, dest, plan);
        else
        {
            batch.Begin(samplerState: plan.Linear ? SamplerState.LinearClamp : SamplerState.PointClamp);
            batch.Draw(texture, dest, Color.White);
            batch.End();
        }
        if (plan.Scanlines)
        {
            DrawScanlines(batch, texture.Height, dest);
        }
    }

    void DrawSmooth(SpriteBatch batch, Texture2D texture, Rectangle dest, FilterPlan plan)
    {
        // Two copies half a picture pixel apart around the true position, averaged; clipped to the box so nothing leaks onto the bars.
        int a = plan.TapShift / 2, b = plan.TapShift - a;
        var clip = Rectangle.Intersect(dest, device.Viewport.Bounds);
        device.ScissorRectangle = clip;
        batch.Begin(samplerState: SamplerState.LinearClamp, rasterizerState: Scissor);
        batch.Draw(texture, new Rectangle(dest.X - a, dest.Y - a, dest.Width, dest.Height), Color.White);
        batch.Draw(texture, new Rectangle(dest.X + b, dest.Y + b, dest.Width, dest.Height), Color.White * 0.5f);
        batch.End();
    }

    /// <summary>One dark band per picture line, as 1x1 white pixels stretched to the band and tinted black.</summary>
    void DrawScanlines(SpriteBatch batch, int lines, Rectangle dest)
    {
        if (_bands is null || _bandsFor != (lines, dest.Height)) { _bands = Scanlines.Rows(lines, dest.Height); _bandsFor = (lines, dest.Height); }
        if (_pixel is null)
        {
            _pixel = new Texture2D(device, 1, 1, false, SurfaceFormat.Color);
            _pixel.SetData([Color.White]);
        }
        var tint = Color.Black * (Scanlines.Alpha / 255f);   // premultiplied black with some alpha
        batch.Begin(samplerState: SamplerState.PointClamp);
        foreach (var (offset, height) in _bands)
            batch.Draw(_pixel, new Rectangle(dest.X, dest.Y + offset, dest.Width, height), tint);
        batch.End();
    }

    public void Dispose()
    {
        _pixel?.Dispose();
        _pixel = null;
    }
}
