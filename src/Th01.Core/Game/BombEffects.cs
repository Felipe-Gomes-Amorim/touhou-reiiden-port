using Th01.Core.Graphics;

namespace Th01.Core.Game;

/// <summary>
/// What the bomb looks like (drawing only; the simulation never sees it): the two rings of kuji-in sprites that close in on the
/// screen centre while white fades towards blue (frames 2..49), then the expanding rings of inverted squares and the vertical
/// screen shake (frames 51..139).
/// The original also flashes the whole palette between white and light blue during that second part. By default that flash is
/// NOT reproduced (it is what makes the bomb hard on the eyes); <see cref="BombFlash.Original"/> turns it on.
/// ref: th01/main/player/bomb.cpp bomb_update_and_render()
/// </summary>
public sealed class BombEffects
{
    public const string Kuji1Sheet = "KUZI1.GRC", Kuji2Sheet = "KUZI2.GRC";

    const int KujiCount = 9, KujiRings = 2, ImagesPerSlot = 8;
    const int KujiCenterX = Framebuffer.Width / 2 - 16, KujiCenterY = Framebuffer.Height / 2 - 16;
    const int SquareRing = 16, SquareSize = 32;
    const int SquareCenterX = Framebuffer.Width / 2 - 16, SquareCenterY = Framebuffer.Height / 2 - 16;
    const byte ColWhite = 7;

    // Squares inverted so far, one flag per dot (the original XORs plane B of the page it draws on and never clears it before the end).
    readonly byte[] _inverted = new byte[Framebuffer.Width * Framebuffer.Height];
    int _invertedUpTo;   // last bomb frame whose squares are in _inverted

    /// <summary>Bomb frames are 0..<see cref="Bomb.KeyframeDone"/> while it runs; anything else means no effect.</summary>
    public static bool Covers(uint frame) => frame < Bomb.KeyframeDone;

    /// <summary>The scroll line of the shake at the given frame (what z_vsync_wait_and_scrollup() set most recently).</summary>
    public static int ShakeLine(uint frame)
    {
        if (frame <= Bomb.KeyframeActive || frame >= Bomb.KeyframeDone) return 0;
        int last = (int)frame & ~3;                                      // the shake is set every 4th frame
        if (last <= Bomb.KeyframeActive) return 0;                        // first one comes at frame 52
        return (last % 16) switch
        {
            0 => Framebuffer.Height - 8,
            8 => Framebuffer.Height - 16,
            _ => 0,
        };
    }

    /// <summary>
    /// How far white has been pulled towards blue at the given frame (0 = untouched, positive = both red and green lowered by that much).
    /// The original lowers red and green of colour 7 on every even frame by a speed that first grows, then shrinks again.
    /// </summary>
    public static int WhiteFade(uint frame)
    {
        if (frame < 2 || frame >= Bomb.KeyframeActive) return 0;
        int speed = -1, applied = 0;
        for (int f = 1; f <= (int)frame; f++)
        {
            if (f % 2 == 0) applied = -speed;
            if (f % 4 == 0) speed += (f % 80) / 40 == 0 ? -1 : 1;
        }
        return applied;
    }

    /// <summary>Draws the effect of the given bomb frame over the finished picture.</summary>
    public void Draw(Framebuffer fb, IGameArt? art, uint frame, VisualOptions? visuals = null)
    {
        if (frame >= Bomb.KeyframeDone) { Reset(); return; }

        int fade = WhiteFade(frame);
        if (fade != 0)
        {
            fb.GetPalette4(ColWhite, out int r, out int g, out int b);
            fb.SetPalette4(ColWhite, Math.Max(0, r - fade), Math.Max(0, g - fade), b);
        }
        if (frame >= 2 && frame < Bomb.KeyframeActive) DrawKuji(fb, art, (int)frame & ~1);

        if (frame <= Bomb.KeyframeActive) { Reset(); }
        else
        {
            AdvanceSquares((int)frame);
            fb.InvertPlanesWhere(_inverted, 1);   // plane B only
            if (visuals?.BombFlash == BombFlash.Original) ApplyFlash(fb, frame);
            fb.ScrollY = visuals?.ScreenShake == false ? 0 : ShakeLine(frame);
        }
    }

    /// <summary>
    /// The palette the original last set at the given frame: on frames % 4 == 0 the peak palette, on % 4 == 2 the "bright" one
    /// (the stage palette lifted to 0xD, blue 0xF, or by the component index when dark), which gains one level on every
    /// frame % 20 == 0. Odd frames keep what the even one before set; before frame 52 nothing changes. The picture's palette
    /// at this point is still the stage's, which is what the original snapshots at frame 0.
    /// </summary>
    static void ApplyFlash(Framebuffer fb, uint frame)
    {
        int last = (int)frame & ~1;
        if (last <= Bomb.KeyframeActive + 1) return;
        bool peak = last % 4 == 0;
        int raises = 0;
        for (int f = Bomb.KeyframeActive + 1; f < last; f++) if (f % 20 == 0) raises++;
        for (int c = 0; c < Framebuffer.ColorCount; c++)
        {
            if (peak) { fb.SetPalette4(c, c == 7 ? 15 : c == 15 ? 6 : 13, c == 7 ? 15 : c == 15 ? 6 : 13, 15); continue; }
            fb.GetPalette4(c, out int r, out int g, out int b);
            fb.SetPalette4(c, Bright(r, 0, raises), Bright(g, 1, raises), Bright(b, 2, raises));
        }
        static int Bright(int v, int comp, int raises) =>
            Math.Min(15, (v > 0x0A ? (comp == 2 ? 0x0F : 0x0D) : v + comp) + raises);
    }

    void Reset()
    {
        if (_invertedUpTo != 0) Array.Clear(_inverted);
        _invertedUpTo = 0;
    }

    // The kuji move every second frame and stay where they were put in between.
    static void DrawKuji(Framebuffer fb, IGameArt? art, int frame)
    {
        if (art == null) return;
        int angle = frame & 0xFF;
        for (int ring = 0; ring <= KujiCount * (KujiRings - 1); ring += KujiCount)
        {
            int radius = 500 - ring * 10 - (frame + 60) * 4;
            for (int i = ring; i < ring + KujiCount; i++)
            {
                int left = AngleMath.PolarX(KujiCenterX, radius, angle), top = AngleMath.PolarY(KujiCenterY, radius, angle);
                bool second = i == ring + ImagesPerSlot;
                if (art.TryGetFrame(second ? Kuji2Sheet : Kuji1Sheet, second ? 0 : i - ring, out var f))
                    fb.DrawMono(f, left, top, ColWhite);
                angle = (angle + 0x100 / KujiCount) & 0xFF;
            }
            angle = -frame & 0xFF;
        }
    }

    // Replays the squares of every frame not yet accounted for, so rendering the same frame twice or skipping one gives the same picture.
    void AdvanceSquares(int frame)
    {
        if (frame < _invertedUpTo) { Array.Clear(_inverted); _invertedUpTo = 0; }
        for (int f = Math.Max(_invertedUpTo, Bomb.KeyframeActive) + 1; f <= frame; f++)
        {
            for (int ring = 0; ring < SquareRing; ring++)
            {
                int angle = ring * (0x100 / SquareRing);
                for (int factor = 6; factor <= 8; factor++)
                {
                    int radius = (f - Bomb.KeyframeActive) * factor;
                    InvertSquare(AngleMath.PolarX(SquareCenterX, radius, angle), AngleMath.PolarY(SquareCenterY, radius, angle));
                }
            }
        }
        _invertedUpTo = frame;
    }

    // graph_invert_32x32_8(): left is rounded down to a multiple of 8, and a square not fully inside VRAM is not inverted at all.
    void InvertSquare(int left, int top)
    {
        left = (left >> 3) << 3;
        if (left < 0 || top < 0 || left + SquareSize > Framebuffer.Width || top + SquareSize > Framebuffer.Height) return;
        for (int y = top; y < top + SquareSize; y++)
            for (int x = left; x < left + SquareSize; x++)
                _inverted[y * Framebuffer.Width + x] ^= 1;
    }
}
