namespace Th01.Core.Graphics;

/// <summary>
/// The two graphics pages of the PC-98. Page 0 is the one that is shown (and what the platform presents);
/// page 1 holds clean backgrounds that get restored onto page 0 with rectangle copies (what the EGC is used for).
/// The palette is a hardware property: the displayed palette is page 0's, so set it there.
/// </summary>
public sealed class VramPages
{
    public Framebuffer Page0 { get; }
    public Framebuffer Page1 { get; }

    public VramPages(Framebuffer? page0 = null, Framebuffer? page1 = null)
    {
        Page0 = page0 ?? new Framebuffer();
        Page1 = page1 ?? new Framebuffer();
    }

    /// <summary>Restores a rectangle of the background from page 1 onto page 0 (EGC word granularity).</summary>
    public void Restore(int x, int y, int w, int h) => Page0.CopyRectEgc16(Page1, x, y, w, h);

    /// <summary>Restores a rectangle exactly (no 16-dot rounding), for callers that do not want the EGC quirk.</summary>
    public void RestoreExact(int x, int y, int w, int h) => Page0.CopyRect(Page1, x, y, w, h);

    /// <summary>Copies the whole of page 0 to page 1 (snapshot a finished background).</summary>
    public void Snapshot() => Page1.CopyFrom(Page0);

    /// <summary>Copies the whole of page 1 onto page 0.</summary>
    public void RestoreAll() => Page0.CopyFrom(Page1);
}
