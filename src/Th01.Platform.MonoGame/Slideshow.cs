using Th01.Core.Graphics;

namespace Th01.Platform.MonoGame;

/// <summary>One instruction of a picture sequence. Durations are in ticks.</summary>
public abstract record SlideStep;
/// <summary>Loads a picture set (a 640x400 .GRP holding four 320x200 pictures) and its palette. Clears the picture and the text; takes no time.</summary>
public sealed record LoadPics(string File) : SlideStep;
/// <summary>Shows picture <c>Quarter</c> (0 1 / 2 3) in the middle of the screen and holds it for <c>Hold</c> ticks.</summary>
public sealed record ShowPic(int Quarter, int Hold) : SlideStep;
/// <summary>Like <see cref="ShowPic"/> but takes no time (the next step runs in the same tick).</summary>
public sealed record SetPic(int Quarter) : SlideStep;
/// <summary>Shows the whole loaded 640x400 picture (the verdict screen background). Takes no time.</summary>
public sealed record ShowFull : SlideStep;
/// <summary>Blanks the picture, the captions and the text (the original's graphics clear). Takes no time.</summary>
public sealed record ClearPic : SlideStep;
/// <summary>Moves the tone to <c>Target</c> over <c>Ticks</c>: -15 = black, 0 = normal, 15 = white.</summary>
public sealed record ToneTo(int Target, int Ticks) : SlideStep;
/// <summary>Sets the tone at once (-15 black, 0 normal, 15 white). Takes no time.</summary>
public sealed record SetTone(int Tone) : SlideStep;
/// <summary>Scrolls the screen down by <c>Y</c> lines (the shake of a bad ending). Takes no time.</summary>
public sealed record Scroll(int Y) : SlideStep;
public sealed record Wait(int Ticks) : SlideStep;
/// <summary>
/// Shows <c>Count</c> lines of text from <c>First</c> on, taken from the story scene of the user's own FUUIN.EXE named
/// <c>Scene</c> (an image file name), under the picture: each line centred and aligned to 8 dots, 32 rows apart, starting at row <c>Y</c>.
/// Stays until <see cref="ClearCaption"/>, <see cref="ClearPic"/> or the next <see cref="LoadPics"/>. Takes no time.
/// Nothing is shown when the text is not available.
/// </summary>
public sealed record Caption(string Scene, int First, int Count, int Y) : SlideStep;
public sealed record ClearCaption : SlideStep;
/// <summary>Puts a line of text at (X, Y) in colour 15, like the original's typed text. Stays until <see cref="ClearPic"/> or <see cref="LoadPics"/>. Takes no time.</summary>
public sealed record PutText(int X, int Y, string Text) : SlideStep;

/// <summary>Text lines of a story scene (an image file name such as "ED2A.GRP"), read from the user's game files at run time.</summary>
public interface ICaptionSource
{
    IReadOnlyList<string> Lines(string scene);
}

/// <summary>Text of the verdict screen, read from the user's game files at run time. Null when it cannot be found.</summary>
public interface IVerdictSource
{
    Th01.Assets.Model.VerdictStrings? Verdict { get; }
}

/// <summary>
/// Plays a list of <see cref="SlideStep"/>s: the ending-style picture sequences with black/white fades. Pure and tick-driven,
/// so it is testable without a window. Text is drawn only when a font is given (captions also need an <see cref="ICaptionSource"/>).
/// </summary>
public sealed class Slideshow
{
    public const int PicW = 320, PicH = 200;
    const int PicLeft = (Framebuffer.Width - PicW) / 2, PicTop = (Framebuffer.Height - PicH) / 2;
    /// <summary>Colour of the typed text and captions: COL_TYPE in ReC98 th01/end/type.hpp.</summary>
    const byte TextColor = 15;
    /// <summary>Rows between the two lines of a caption: ((line * 2) + 1) glyph heights of 16, ReC98 th01/end/end.cpp pic_caption_type_n.</summary>
    const int CaptionLineHeight = 32;

    readonly IScreenImages _images;
    readonly IReadOnlyList<SlideStep> _steps;
    readonly IFontSource? _font;
    readonly ICaptionSource? _captions;
    string[] _captionLines = [];
    int _captionY;
    readonly List<PutText> _texts = new();
    int _scrollY;
    bool _full;
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

    public Slideshow(IScreenImages images, IReadOnlyList<SlideStep> steps, IFontSource? font = null, ICaptionSource? captions = null)
    {
        _images = images;
        _steps = steps;
        _font = font;
        _captions = captions;
        Advance();
    }

    public void Tick()
    {
        if (Finished) return;
        if (_toneTicks > 0)
        {
            _toneElapsed++;
            Tone = _toneFrom + (_toneTarget - _toneFrom) * Math.Min(_toneElapsed, _toneTicks) / _toneTicks;
            if (_toneElapsed >= _toneTicks) _toneTicks = 0;
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
                    Blank();
                    break;
                case Caption c:
                    var all = _captions?.Lines(c.Scene) ?? [];
                    int first = Math.Clamp(c.First, 0, all.Count);
                    _captionLines = all.Skip(first).Take(Math.Max(0, c.Count)).ToArray();
                    _captionY = c.Y;
                    break;
                case ClearCaption:
                    _captionLines = [];
                    break;
                case PutText t:
                    _texts.Add(t);
                    break;
                case ClearPic:
                    Blank();
                    break;
                case ShowPic p:
                    SetQuarter(p.Quarter);
                    _remaining = Math.Max(1, p.Hold);
                    break;
                case SetPic p:
                    SetQuarter(p.Quarter);
                    break;
                case ShowFull:
                    _full = true;
                    _quarter = _hasSet ? _set.Pixels.ToArray() : null;
                    break;
                case ToneTo t:
                    _toneFrom = Tone;
                    _toneTarget = Math.Clamp(t.Target, -15, 15);
                    _toneTicks = Math.Max(1, t.Ticks);
                    _toneElapsed = 0;
                    _remaining = _toneTicks;
                    break;
                case SetTone t:
                    _toneTicks = 0;
                    Tone = Math.Clamp(t.Tone, -15, 15);
                    break;
                case Scroll s:
                    _scrollY = s.Y;
                    break;
                case Wait w:
                    _remaining = Math.Max(1, w.Ticks);
                    break;
            }
        }
    }

    void Blank()
    {
        _quarterIndex = -1;
        _quarter = null;
        _full = false;
        _captionLines = [];
        _texts.Clear();
    }

    void SetQuarter(int q)
    {
        _full = false;
        _quarterIndex = q;
        _quarter = _hasSet ? ExtractQuarter(q) : null;
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
        fb.ScrollY = _scrollY;
        fb.Clear(0);
        if (!_hasSet)
        {
            for (int i = 0; i < Framebuffer.ColorCount; i++) fb.SetPalette4(i, i, i, i);
            return;
        }
        fb.ApplyPalette(_set);
        if (_quarter is not null)
        {
            if (_full) fb.Blit(_quarter, _set.Width, _set.Height, 0, 0);
            else fb.Blit(_quarter, PicW, PicH, PicLeft, PicTop);
        }
        DrawText(fb);
        ApplyTone(fb, Tone);
    }

    /// <summary>
    /// Captions: every line centred on the screen and aligned to a byte (8 dots), as the original does, then the typed text.
    /// ASSUMPTION: the original types them one character at a time (the delay per character is not known); here they appear at once.
    /// </summary>
    void DrawText(Framebuffer fb)
    {
        if (_font is null) return;
        for (int i = 0; i < _captionLines.Length; i++)
        {
            int width = TextRenderer.Measure(_font, _captionLines[i]);
            int x = (Framebuffer.Width / 2 - width / 2 + 7) & ~7;
            TextRenderer.DrawString(fb, _font, _captionLines[i], x, _captionY + i * CaptionLineHeight, TextColor);
        }
        foreach (var t in _texts) TextRenderer.DrawString(fb, _font, t.Text, t.X, t.Y, TextColor);
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
/// The ending sequences, written from the original's ending code (ReC98 th01/end/end.cpp): pictures, order and frame delays are
/// taken from it. ASSUMPTION (not measured against the original): a palette fade of speed N takes N ticks per step and 16 steps
/// (<see cref="FadeTicks"/>), the picture copy is instant (the original's is slow), and the typed text appears at once.
/// </summary>
public static class Cutscenes
{
    /// <summary>Ticks of a master.lib-style fade: <c>frame_delay_per_step</c> times 16 steps. ASSUMPTION: the step count is not confirmed.</summary>
    public static int FadeTicks(int speed) => speed * 16;

    /// <summary>The original's palette tone (0 black, 100 normal, 200 white) as a slideshow tone (-15 .. 15).</summary>
    public static int ToneOf(int tone) => (int)Math.Round((tone - 100) * 0.15);

    /// <summary>First row of the caption text: below the 320x200 picture, one line of 16 rows after it (CUTSCENE_PIC_TOP + CUTSCENE_PIC_H + 16).</summary>
    const int CaptionY = 316;

    /// <summary>The final delay of an ending after its caption (FINAL_DELAY_FRAMES).</summary>
    const int FinalDelay = 300;

    static SlideStep[] Pic(int quarter, int delay) => [new SetPic(quarter), new Wait(delay)];

    static void Add(List<SlideStep> s, params SlideStep[] steps) => s.AddRange(steps);

    /// <summary>ED1A..ED1E, the first part of every ending (barn door, seal, sweeping, the Orb).</summary>
    public static IReadOnlyList<SlideStep> EndingOne()
    {
        var s = new List<SlideStep>();
        Add(s, new SetTone(ToneOf(0)), new LoadPics("ED1A.GRP"), new SetPic(0), new ToneTo(0, FadeTicks(6)), new Wait(100));
        Add(s, new ToneTo(15, FadeTicks(5)), new Wait(100));
        // Closing barn door
        Add(s, new SetPic(1), new SetTone(ToneOf(100)), new Wait(13));
        s.AddRange(Pic(2, 13)); s.AddRange(Pic(3, 50));
        // Seal
        Add(s, new LoadPics("ED1B.GRP"));
        s.AddRange(Pic(0, 10)); s.AddRange(Pic(1, 10)); s.AddRange(Pic(2, 100));
        Add(s, new ToneTo(-15, FadeTicks(6)), new ClearPic(), new SetTone(ToneOf(100)));
        // Reimu sweeping
        Add(s, new LoadPics("ED1C.GRP"));
        for (int i = 0; i < 6; i++) { s.AddRange(Pic(0, 20)); s.AddRange(Pic(1, 20)); }
        s.AddRange(Pic(0, 40)); s.AddRange(Pic(2, 40));
        // Reimu looking at the Orb
        Add(s, new LoadPics("ED1D.GRP"));
        s.AddRange(Pic(3, 70));
        for (int i = 0; i < 100; i++)
        {
            if (i == 0) s.Add(new SetPic(0));
            foreach (int first in new[] { 20, 50, 90 })
            {
                if (i == first) s.Add(new SetPic(1));
                if (i == first + 4) s.Add(new SetPic(2));
                if (i == first + 8) s.Add(new SetPic(0));
            }
            s.Add(new Wait(2));
        }
        s.AddRange(Pic(3, 60)); s.AddRange(Pic(0, 20));
        Add(s, new SetPic(3), new SetTone(ToneOf(105)));
        // Orb hovering and sparkling
        Add(s, new LoadPics("ED1E.GRP"), new SetPic(0));
        for (int i = 0; i < (130 - 110) / 5; i++) Add(s, new SetTone(ToneOf(110 + i * 5)), new Wait(16));
        for (int i = 0; i < (205 - 130) / 5; i++)
        {
            s.AddRange(Pic(1, 8)); s.AddRange(Pic(2, 8));
            s.Add(new SetTone(ToneOf(130 + i * 5)));
        }
        return s;
    }

    /// <summary>
    /// The whole ending of FUUIN.EXE up to the verdict: ED1A..ED1E, then the bad ending when any continue was used, otherwise the
    /// good ending and the boss slides. <paramref name="route"/> 0 = Makai, 1 = Jigoku.
    /// </summary>
    public static IReadOnlyList<SlideStep> Ending(int route, int continuesTotal)
    {
        var s = new List<SlideStep>(EndingOne());
        if (continuesTotal == 0) { s.AddRange(Good(route)); s.AddRange(BossSlides(route, withBlackOutBefore: true)); }
        else { s.AddRange(Bad(route)); s.AddRange(EndDone()); }
        return s;
    }

    /// <summary>end_done(): music out, black out, clear.</summary>
    static SlideStep[] EndDone() => [new ToneTo(-15, FadeTicks(10)), new ClearPic()];

    /// <summary>Bad ending: ED2A, then ED4A (Makai) or the shaking "boom" of ED2A (Jigoku), then the two-line caption.</summary>
    public static IReadOnlyList<SlideStep> Bad(int route)
    {
        var s = new List<SlideStep>();
        Add(s, new LoadPics("ED2A.GRP"), new SetTone(ToneOf(200)), new SetPic(0), new ToneTo(0, FadeTicks(5)), new Wait(120));
        if (route == 1)
        {
            s.AddRange(ShakeThenBoom(64, 16));
            s.AddRange(ShakeThenBoom(32, 32));
            Add(s, new Scroll(0), new Wait(50));
            s.AddRange(Pic(3, 100));
            Add(s, new Caption("ED2A.GRP", 0, 2, CaptionY));
        }
        else
        {
            Add(s, new LoadPics("ED4A.GRP"));
            for (int i = 0; i < 20; i++) { s.AddRange(Pic(0, 3)); s.AddRange(Pic(1, 3)); }
            for (int i = 0; i < (200 - 100) / 5; i++)
            {
                s.AddRange(Pic(0, 3)); s.AddRange(Pic(1, 3));
                s.Add(new SetTone(ToneOf(100 + i * 5)));
            }
            Add(s, new SetPic(2), new ToneTo(0, FadeTicks(5)), new Wait(200));
            s.AddRange(Pic(3, 100));
            Add(s, new Caption("ED4A.GRP", 0, 2, CaptionY));
        }
        s.Add(new Wait(FinalDelay));
        return s;
    }

    static IEnumerable<SlideStep> ShakeThenBoom(int shake, int boom)
    {
        for (int i = 0; i <= shake; i++)
        {
            yield return new Scroll((i & 3) == 0 ? 8 : 0);
            yield return new SetPic(1);
            yield return new Wait(1);
            yield return new SetTone(ToneOf((i & 1) != 0 ? 170 : 100));
        }
        for (int i = 0; i <= boom; i++)
        {
            yield return new Scroll((i & 3) == 0 ? 8 : 0);
            yield return new SetPic(1);
            yield return new Wait(2);
            // The original shows the "boom" picture without any delay, so on real hardware it is only half-visible.
            // ASSUMPTION: show it for one tick, as ZUN clearly intended.
            if ((i & 1) != 0) { yield return new SetPic(2); yield return new Wait(1); }
        }
    }

    /// <summary>Good ending: ED3A, then ED3B (Jigoku) or ED5A/ED5B/ED5C (Makai), then the two-line caption.</summary>
    public static IReadOnlyList<SlideStep> Good(int route)
    {
        var s = new List<SlideStep>();
        Add(s, new LoadPics("ED3A.GRP"), new SetTone(ToneOf(200)));
        for (int i = 0; i < (200 - 100) / 5; i++)
        {
            s.AddRange(Pic(0, 8)); s.AddRange(Pic(1, 8));
            s.Add(new SetTone(ToneOf(200 - i * 5)));
        }
        s.Add(new SetTone(ToneOf(100)));
        for (int i = 0; i < 10; i++) { s.AddRange(Pic(0, 8)); s.AddRange(Pic(1, 8)); }
        for (int i = 0; i < (200 - 100) / 10; i++)
        {
            s.AddRange(Pic(i & 1, 8));
            s.Add(new SetTone(ToneOf(100 + i * 10)));
        }
        s.Add(new SetTone(ToneOf(200)));

        if (route == 1)
        {
            Add(s, new LoadPics("ED3B.GRP"), new SetPic(0), new ToneTo(0, FadeTicks(4)), new Wait(250));
            s.AddRange(Pic(1, 200)); s.AddRange(Pic(2, 150)); s.AddRange(Pic(3, 150));
            Add(s, new Caption("ED3B.GRP", 0, 2, CaptionY), new Wait(FinalDelay));
        }
        else
        {
            Add(s, new LoadPics("ED5A.GRP"), new SetPic(0), new ToneTo(0, FadeTicks(4)), new Wait(300));
            for (int i = 0; i < 15; i++) { s.AddRange(Pic(1, 8)); s.AddRange(Pic(2, 8)); }
            Add(s, new LoadPics("ED5B.GRP"));
            s.AddRange(Pic(0, 150)); s.AddRange(Pic(1, 100)); s.AddRange(Pic(2, 100)); s.AddRange(Pic(3, 100));
            Add(s, new LoadPics("ED5C.GRP"));
            s.AddRange(Pic(0, 100)); s.AddRange(Pic(1, 150)); s.AddRange(Pic(2, 150));
            Add(s, new Caption("ED5C.GRP", 0, 2, CaptionY), new Wait(FinalDelay));
        }
        return s;
    }

    /// <summary>
    /// Boss pairs (stage label + name) are the 7 pairs of lines in the text of the ending program, in stage order: SinGyoku,
    /// YuugenMagan, Mima, Elis, Kikuri, Sariel, Konngara. Makai shows pairs 0 1 3 5, Jigoku 0 2 4 6.
    /// </summary>
    static readonly int[][] BossPairs = [[0, 1, 3, 5], [0, 2, 4, 6]];

    const int BossSlideDelay = 150, BossTextDelay = 50;

    /// <summary>ENDB_A (Makai) / ENDB_B (Jigoku): one picture per boss of stages 5, 10, 15 and 20, each with a black fade and its caption.</summary>
    public static IReadOnlyList<SlideStep> BossSlides(int route) => BossSlides(route, withBlackOutBefore: false);

    static IReadOnlyList<SlideStep> BossSlides(int route, bool withBlackOutBefore)
    {
        var s = new List<SlideStep>();
        if (withBlackOutBefore) s.AddRange(EndDone());
        Add(s, new LoadPics(route == 0 ? "ENDB_A.GRP" : "ENDB_B.GRP"), new SetPic(0), new SetTone(ToneOf(100)), new Wait(BossTextDelay));
        s.Add(new Caption("ENDB_B.GRP", BossPairs[route == 0 ? 0 : 1][0] * 2, 2, CaptionY));
        for (int q = 1; q < 4; q++)
        {
            Add(s, new Wait(BossSlideDelay), new ToneTo(-15, FadeTicks(5)), new ClearPic(), new SetPic(q), new ToneTo(0, FadeTicks(5)), new Wait(BossTextDelay));
            s.Add(new Caption("ENDB_B.GRP", BossPairs[route == 0 ? 0 : 1][q] * 2, 2, CaptionY));
        }
        s.Add(new Wait(BossSlideDelay));
        return s;
    }
}
