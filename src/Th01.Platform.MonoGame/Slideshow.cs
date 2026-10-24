using Th01.Core.Graphics;

namespace Th01.Platform.MonoGame;

/// <summary>One instruction of a picture sequence. Durations are in ticks.</summary>
public abstract record SlideStep;
/// <summary>Loads a picture set (a 640x400 .GRP holding four 320x200 pictures) and its palette. Takes no time.</summary>
public sealed record LoadPics(string File) : SlideStep;
/// <summary>Shows picture <c>Quarter</c> (0 1 / 2 3) in the middle of the screen and holds it.</summary>
public sealed record ShowPic(int Quarter, int Hold) : SlideStep;
/// <summary>Moves the tone to <c>Target</c> over <c>Ticks</c>: -15 = black, 0 = normal, 15 = white.</summary>
public sealed record ToneTo(int Target, int Ticks) : SlideStep;
public sealed record Wait(int Ticks) : SlideStep;

/// <summary>
/// Plays a list of <see cref="SlideStep"/>s: the ending-style picture sequences with black/white fades. Pure and tick-driven,
/// so it is testable without a window. Text captions are not drawn (Japanese font undecided).
/// </summary>
public sealed class Slideshow
{
    public const int PicW = 320, PicH = 200;
    const int PicLeft = (Framebuffer.Width - PicW) / 2, PicTop = (Framebuffer.Height - PicH) / 2;

    readonly IScreenImages _images;
    readonly IReadOnlyList<SlideStep> _steps;
    int _index;
    int _remaining;
    int _toneFrom, _toneTarget, _toneTicks;
    int _toneElapsed;
    ArtFrame _set;
    bool _hasSet;
    byte[]? _quarter;
    int _quarterIndex = -1;

    public bool Finished { get; private set; }
    public int Tone { get; set; } = -15;
    public int StepIndex => _index;

    public Slideshow(IScreenImages images, IReadOnlyList<SlideStep> steps)
    {
        _images = images;
        _steps = steps;
        Advance();
    }

    public void Tick()
    {
        if (Finished) return;
        if (_toneTicks > 0)
        {
            _toneElapsed++;
            Tone = _toneFrom + (_toneTarget - _toneFrom) * Math.Min(_toneElapsed, _toneTicks) / _toneTicks;
        }
        if (_remaining > 0) _remaining--;
        if (_remaining == 0) Advance();
    }

    /// <summary>Runs steps until one that takes time (or the end).</summary>
    void Advance()
    {
        while (_remaining == 0)
        {
            if (_index >= _steps.Count) { Finished = true; return; }
            switch (_steps[_index++])
            {
                case LoadPics l:
                    _hasSet = _images.TryLoad(l.File, out _set);
                    _quarterIndex = -1;
                    _quarter = null;
                    break;
                case ShowPic p:
                    _quarterIndex = p.Quarter;
                    _quarter = _hasSet ? ExtractQuarter(p.Quarter) : null;
                    _remaining = Math.Max(1, p.Hold);
                    break;
                case ToneTo t:
                    _toneFrom = Tone;
                    _toneTarget = Math.Clamp(t.Target, -15, 15);
                    _toneTicks = Math.Max(1, t.Ticks);
                    _toneElapsed = 0;
                    _remaining = _toneTicks;
                    break;
                case Wait w:
                    _remaining = Math.Max(1, w.Ticks);
                    break;
            }
        }
    }

    byte[] ExtractQuarter(int q)
    {
        var px = _set.Pixels.Span;
        var dst = new byte[PicW * PicH];
        int ox = (q & 1) * PicW, oy = (q >> 1) * PicH;
        if (_set.Width < ox + PicW || _set.Height < oy + PicH) return dst;
        for (int y = 0; y < PicH; y++) px.Slice((oy + y) * _set.Width + ox, PicW).CopyTo(dst.AsSpan(y * PicW));
        return dst;
    }

    public void Render(Framebuffer fb)
    {
        fb.Clear(0);
        if (!_hasSet)
        {
            for (int i = 0; i < Framebuffer.ColorCount; i++) fb.SetPalette4(i, i, i, i);
            return;
        }
        fb.ApplyPalette(_set);
        if (_quarter is not null) fb.Blit(_quarter, PicW, PicH, PicLeft, PicTop);
        ApplyTone(fb, Tone);
    }

    static void ApplyTone(Framebuffer fb, int tone)
    {
        if (tone == 0) return;
        for (int i = 0; i < Framebuffer.ColorCount; i++)
        {
            fb.GetPalette4(i, out int r, out int g, out int b);
            fb.SetPalette4(i, Scale(r, tone), Scale(g, tone), Scale(b, tone));
        }
    }

    static int Scale(int c, int tone) => tone < 0 ? c * (15 + tone) / 15 : c + (15 - c) * tone / 15;
}

/// <summary>
/// Picture sequences built from the screen map in docs/FORMATS.md. Pictures and their order follow the original's ending code;
/// the timings are written down from it but not measured (and the fades are plain linear tones), so treat them as approximate.
/// </summary>
public static class Cutscenes
{
    /// <summary>ED1A..ED1E, the first part of the ending (barn door, seal, sweeping, ...).</summary>
    public static IReadOnlyList<SlideStep> EndingOne()
    {
        var s = new List<SlideStep> { new LoadPics("ED1A.GRP"), new ShowPic(0, 1), new ToneTo(0, 16), new Wait(100), new ToneTo(15, 30), new Wait(100) };
        s.AddRange([new ToneTo(0, 1), new ShowPic(1, 13), new ShowPic(2, 13), new ShowPic(3, 50)]);
        s.AddRange([new LoadPics("ED1B.GRP"), new ShowPic(0, 10), new ShowPic(1, 10), new ShowPic(2, 100), new ToneTo(-15, 30)]);
        s.Add(new ToneTo(0, 1));
        s.Add(new LoadPics("ED1C.GRP"));
        for (int i = 0; i < 3; i++) { s.Add(new ShowPic(0, 20)); s.Add(new ShowPic(1, 20)); }
        s.AddRange([new ShowPic(0, 40), new ShowPic(2, 40), new LoadPics("ED1D.GRP"), new ShowPic(3, 70)]);
        for (int i = 0; i < 3; i++) { s.Add(new ShowPic(1, 4)); s.Add(new ShowPic(2, 4)); s.Add(new ShowPic(0, 4)); }
        s.AddRange([new ShowPic(3, 60), new ShowPic(0, 20), new ShowPic(3, 1), new ToneTo(5, 1)]);
        s.AddRange([new LoadPics("ED1E.GRP"), new ShowPic(0, 1)]);
        for (int i = 0; i < 6; i++) { s.Add(new ToneTo(i * 3, 16)); s.Add(new ShowPic(1, 8)); s.Add(new ShowPic(2, 8)); }
        s.Add(new ToneTo(15, 20));
        return s;
    }

    /// <summary>ENDB_A (Makai) / ENDB_B (Jigoku): one picture per boss of stages 5, 10, 15 and 20, with black fades in between.</summary>
    public static IReadOnlyList<SlideStep> BossSlides(int route)
    {
        var s = new List<SlideStep> { new LoadPics(route == 0 ? "ENDB_A.GRP" : "ENDB_B.GRP") };
        for (int q = 0; q < 4; q++)
        {
            s.Add(new ShowPic(q, 1));
            s.Add(new ToneTo(0, 10));
            s.Add(new Wait(160));
            s.Add(new ToneTo(-15, 10));
        }
        return s;
    }
}
