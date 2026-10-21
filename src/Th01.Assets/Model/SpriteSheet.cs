namespace Th01.Assets.Model;

/// <summary>Ordered frames that all share the same size (PTN / BOS / GRC).</summary>
public sealed class SpriteSheet
{
    public IReadOnlyList<IndexedImageA> Frames { get; }
    public int FrameWidth { get; }
    public int FrameHeight { get; }

    public SpriteSheet(IReadOnlyList<IndexedImageA> frames)
    {
        ArgumentNullException.ThrowIfNull(frames);
        if (frames.Count == 0) throw new ArgumentException("A sprite sheet needs at least one frame.", nameof(frames));
        FrameWidth = frames[0].Width;
        FrameHeight = frames[0].Height;
        foreach (var f in frames)
            if (f.Width != FrameWidth || f.Height != FrameHeight)
                throw new ArgumentException("All frames must have the same size.", nameof(frames));
        Frames = frames;
    }
}
