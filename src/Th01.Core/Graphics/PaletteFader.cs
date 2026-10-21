namespace Th01.Core.Graphics;

/// <summary>
/// Palette fades as the original does them: every tick each 4-bit component moves by one step towards its target,
/// so a full fade takes at most 15 ticks. Drive it with <see cref="Step"/> once per tick.
/// </summary>
// ref: th01/hardware/graph.cpp (z_palette_fade_from), pgtrans.cpp
public sealed class PaletteFader
{
    readonly int[] _cur = new int[Framebuffer.ColorCount * 3];
    readonly int[] _target = new int[Framebuffer.ColorCount * 3];
    Framebuffer? _fb;

    public bool Active { get; private set; }

    /// <summary>
    /// Starts at a solid colour (e.g. 0,0,0 black or 15,15,15 white) and fades up to the framebuffer's current palette.
    /// Colours whose bit is set in <paramref name="keepMask"/> are left alone.
    /// </summary>
    public void FadeFrom(Framebuffer fb, int r4, int g4, int b4, int keepMask = 0)
    {
        Load(fb, _target);
        for (int c = 0; c < Framebuffer.ColorCount; c++)
        {
            bool keep = (keepMask >> c & 1) != 0;
            _cur[c * 3] = keep ? _target[c * 3] : r4;
            _cur[c * 3 + 1] = keep ? _target[c * 3 + 1] : g4;
            _cur[c * 3 + 2] = keep ? _target[c * 3 + 2] : b4;
        }
        Begin(fb);
    }

    /// <summary>Fades the framebuffer's current palette towards a solid colour.</summary>
    public void FadeTo(Framebuffer fb, int r4, int g4, int b4, int keepMask = 0)
    {
        Load(fb, _cur);
        for (int c = 0; c < Framebuffer.ColorCount; c++)
        {
            bool keep = (keepMask >> c & 1) != 0;
            _target[c * 3] = keep ? _cur[c * 3] : r4;
            _target[c * 3 + 1] = keep ? _cur[c * 3 + 1] : g4;
            _target[c * 3 + 2] = keep ? _cur[c * 3 + 2] : b4;
        }
        Begin(fb);
    }

    /// <summary>Fades towards an arbitrary palette (16 entries x R,G,B, each 0..15), e.g. a tinted version of the current one.</summary>
    public void FadeToPalette(Framebuffer fb, ReadOnlySpan<int> rgb4)
    {
        if (rgb4.Length != Framebuffer.ColorCount * 3) throw new ArgumentException("expected 48 components", nameof(rgb4));
        Load(fb, _cur);
        for (int i = 0; i < _target.Length; i++) _target[i] = Math.Clamp(rgb4[i], 0, 15);
        Begin(fb);
    }

    /// <summary>Advances one tick. Returns true while a fade is still running after this step.</summary>
    public bool Step()
    {
        if (!Active) return false;
        bool changed = false;
        for (int i = 0; i < _cur.Length; i++)
        {
            if (_cur[i] == _target[i]) continue;
            _cur[i] += _cur[i] < _target[i] ? 1 : -1;
            changed = true;
        }
        Apply();
        if (!changed) Active = false;
        return Active;
    }

    /// <summary>Jumps to the final palette immediately.</summary>
    public void Finish()
    {
        if (!Active) return;
        Array.Copy(_target, _cur, _cur.Length);
        Apply();
        Active = false;
    }

    void Begin(Framebuffer fb)
    {
        _fb = fb;
        Active = true;
        Apply();
    }

    void Apply()
    {
        for (int c = 0; c < Framebuffer.ColorCount; c++)
            _fb!.SetPalette4(c, _cur[c * 3], _cur[c * 3 + 1], _cur[c * 3 + 2]);
    }

    static void Load(Framebuffer fb, int[] dest)
    {
        for (int c = 0; c < Framebuffer.ColorCount; c++)
        {
            fb.GetPalette4(c, out int r, out int g, out int b);
            dest[c * 3] = r;
            dest[c * 3 + 1] = g;
            dest[c * 3 + 2] = b;
        }
    }
}
