using Th01.Core.Graphics;

namespace Th01.Core.Game;

/// <summary>
/// Draws a <see cref="GameSession"/> into a framebuffer. Reads state only. With an <see cref="IGameArt"/> it uses the
/// game's sprites (sheet names and frame indices: docs/FORMATS.md, "Mapa de papéis"); every lookup that fails falls
/// back to the flat boxes, so the session also renders with no art at all. The original redraws only dirty regions
/// and leaves one-frame artifacts (unblit-before-blit bugs); this draws each frame from scratch and does not
/// reproduce those artifacts (the logic that causes them is kept, see GAMEPLAY-NOTES.md section 7 and 19).
/// </summary>
internal sealed class GameRenderer
{
    // ref: th01/main/stage/stageobj.cpp CARD_ANIM -- STG.PTN frame per [remaining flips][animation cel]
    static readonly int[][] CardAnim =
    {
        new[] { 17, 18, 19, 20, 21 },
        new[] { 14, 15, 16, 18, 17 },
        new[] { 11, 12, 13, 15, 14 },
        new[] { 8, 9, 10, 12, 11 },
        new[] { 8, 9, 10, 9, 8 },
    };

    // ref: th01/main/hud/hud.cpp
    const int LivesLeft = 128, LivesTop = 0, BombsLeft = 128, BombsTop = 16;
    const int ScoreLeft = 256, ScoreTop = 16, ComboLeft = 256 + 9 * 16;
    const int TimerLeft = 512, StageLeft = 608, StageTop = 32, ColW = 16;

    const string Stg = "STG.PTN", Miko = "MIKO.PTN", MikoAc = "MIKO_AC.BOS", MikoAc2 = "MIKO_AC2.BOS";

    readonly GameSession _s;
    readonly IGameArt? _art;
    readonly IFontSource? _font;
    bool _paletteFromArt;

    public GameRenderer(GameSession s, IGameArt? art, IFontSource? font)
    {
        _s = s;
        _art = art;
        _font = font;
    }

    public void Render(Framebuffer fb)
    {
        var w = _s.World;
        _paletteFromArt = false;
        fb.Clear(0);
        if (_art != null) DrawBackground(fb);
        if (!_paletteFromArt) FallbackPalette(fb);
        fb.FillRect(Playfield.Left, Playfield.Top - 2, Playfield.Width, 2, 1);

        if (w.Stage != null)
        {
            DrawCards(fb, w.Stage);
            DrawObstacles(fb, w.Stage);
        }
        if (w.Boss != null) DrawBoss(fb, w.Boss);
        DrawItems(fb, w.Items);
        DrawPellets(fb, w.Pellets);
        DrawShots(fb, w.Shots);
        DrawOrb(fb, w);
        DrawPlayer(fb, w);
        DrawHud(fb, w);
        if (_s.Phase == SessionPhase.StageBonus) DrawBonus(fb);
    }

    // ---- helpers ----

    static void FallbackPalette(Framebuffer fb)
    {
        fb.SetPalette4(1, 6, 6, 6);
        fb.SetPalette4(9, 15, 8, 8);
        fb.SetPalette4(10, 8, 8, 15);
        fb.SetPalette4(11, 15, 15, 8);
        fb.SetPalette4(12, 8, 15, 8);
        fb.SetPalette4(13, 12, 12, 12);
        fb.SetPalette4(14, 15, 8, 15);
        fb.SetPalette4(15, 15, 15, 15);
    }

    void DrawBackground(Framebuffer fb)
    {
        if (_art!.TryGetBackground(out var bg))
        {
            if (bg.Palette.Length == Framebuffer.ColorCount * 3)
            {
                fb.ApplyPalette(bg);
                _paletteFromArt = true;
            }
            fb.Draw(bg, 0, 0);
        }
        else if (_art.TryGetFrame(Stg, 0, out var f) && f.Palette.Length == Framebuffer.ColorCount * 3)
        {
            fb.ApplyPalette(f);   // sprites of one file share the palette
            _paletteFromArt = true;
        }
    }

    bool Draw(Framebuffer fb, string sheet, int frame, int x, int y)
    {
        if (_art == null || !_art.TryGetFrame(sheet, frame, out var f)) return false;
        fb.Draw(f, x, y);
        return true;
    }

    /// <summary>One 16x16 quarter of a 32x32 sprite (the shots and HUD icons use these).</summary>
    bool DrawQuarter(Framebuffer fb, string sheet, int frame, int quarter, int x, int y)
    {
        if (_art == null || !_art.TryGetFrame(sheet, frame, out var f) || f.Width < 32 || f.Height < 32) return false;
        Span<byte> px = stackalloc byte[256];
        Span<byte> al = stackalloc byte[256];
        var src = f.Pixels.Span;
        var sa = f.Alpha.Span;
        int ox = (quarter & 1) * 16, oy = (quarter >> 1) * 16;
        for (int r = 0; r < 16; r++)
            for (int c = 0; c < 16; c++)
            {
                int si = (oy + r) * f.Width + ox + c;
                px[r * 16 + c] = src[si];
                al[r * 16 + c] = sa.Length == 0 ? (byte)1 : sa[si];
            }
        fb.Blit(px, 16, 16, x, y, al);
        return true;
    }

    // ---- stage objects ----

    void DrawCards(Framebuffer fb, StageObjects st)
    {
        for (int i = 0; i < st.CardCount; i++)
        {
            if (st.CardFlagArr[i] == CardFlag.Removed) continue;
            int hp = Math.Min(st.CardHp[i], 4);
            int cel = st.CardFlagArr[i] == CardFlag.Flipping ? Math.Min(st.CardFlipFrame[i] / StageObjects.CardFramesPerCel, 4) : 0;
            if (!Draw(fb, Stg, CardAnim[hp][cel], st.CardLeft[i], st.CardTop[i]))
                fb.FillRect(st.CardLeft[i] + 2, st.CardTop[i] + 2, 28, 28, (byte)(st.CardFlagArr[i] == CardFlag.Alive ? 14 : 13));
            if (_font != null && st.CardScore[i] != 0 && st.CardFlipFrame[i] >= StageObjects.CardScoreFirst)
                TextRenderer.DrawString(fb, _font, st.CardScore[i].ToString(), st.CardLeft[i], st.CardTop[i] + 8 -
                    Math.Max(0, st.CardFlipFrame[i] - (3 * StageObjects.CardFramesPerCel + 3)), 15);
        }
    }

    void DrawObstacles(Framebuffer fb, StageObjects st)
    {
        for (int i = 0; i < st.ObstacleCount; i++)
        {
            int t = st.ObsType[i], x = st.ObsLeft[i], y = st.ObsTop[i];
            int frame = t switch
            {
                ObstacleType.Bumper => 22,
                >= ObstacleType.TurretFirst and <= ObstacleType.TurretLast => st.TurretFlag(i) == 0 ? 23 : 24,
                ObstacleType.Portal => PortalFrame(st, i),
                ObstacleType.BarTop => 25,
                ObstacleType.BarLeft => 26,
                ObstacleType.BarBottom => 27,
                ObstacleType.BarRight => 28,
                _ => -1,
            };
            if (frame >= 0 && !Draw(fb, Stg, frame, x, y)) fb.FillRect(x + 4, y + 4, 24, 24, 12);
        }
    }

    // Entered portal: animation cels at 1..19 frames; the destination glows from frame 20 to 39.
    static int PortalFrame(StageObjects st, int i)
    {
        int f = st.ObsFrame[i];
        if (f is >= 1 and < 10) return 30;
        if (f is >= 10 and < 20) return 31;
        return 29;
    }

    void DrawItems(Framebuffer fb, Items items)
    {
        foreach (var s in items.Bombs)
            if (s.Flag is ItemFlag.Fall or ItemFlag.Bounce && !Draw(fb, Stg, 32, s.Left, s.Top)) fb.FillRect(s.Left + 8, s.Top + 8, 16, 16, 10);
        foreach (var s in items.Points)
            if (s.Flag is ItemFlag.Fall or ItemFlag.Bounce && !Draw(fb, Stg, 33, s.Left, s.Top)) fb.FillRect(s.Left + 8, s.Top + 8, 16, 16, 11);
    }

    void DrawPellets(Framebuffer fb, Pellets p)
    {
        // The pellet sprites live inside the executable (not in the data files), so they are always boxes.
        for (int i = 0; i < Pellets.Count; i++)
        {
            if (!p.Moving[i] || p.DecayFrame[i] > 20) continue;
            int x = p.CurLeft[i] >> 4, y = p.CurTop[i] >> 4;
            fb.FillRect(x, y, Pellets.W, Pellets.H, (byte)(p.DecayFrame[i] == 0 ? 15 : 13));
        }
    }

    void DrawShots(Framebuffer fb, Shots s)
    {
        for (int i = 0; i < Shots.Count; i++)
        {
            if (s.Moving[i])
            {
                if (!DrawQuarter(fb, Stg, 1, 0, s.Left[i], s.Top[i])) fb.FillRect(s.Left[i], s.Top[i], Shots.W, Shots.H, 11);
            }
            else if (s.DecayFrame[i] != 0)
            {
                if (!DrawQuarter(fb, Stg, 1, Shots.DecayQuarterFor(s.DecayFrame[i]), s.Left[i], s.Top[i]))
                    fb.FillRect(s.Left[i], s.Top[i], Shots.W, Shots.H, 13);
            }
        }
    }

    void DrawOrb(Framebuffer fb, GameWorld w)
    {
        if (w.Orb.InPortal || _s.Phase == SessionPhase.GameOver) return;
        int cel = Math.Min(w.Orb.AnimCel, Orb.Cels - 1);
        if (!Draw(fb, Stg, 3 + cel, w.Orb.Left, w.Orb.Top)) fb.FillRect(w.Orb.Left, w.Orb.Top, Playfield.OrbW, Playfield.OrbH, 10);
    }

    // ---- boss ----

    void DrawBoss(Framebuffer fb, SinGyoku b)
    {
        if (b.Phase == 0 && _s.Phase != SessionPhase.BossEntrance) return;
        int x = b.Left, y = b.Top;
        bool drawn;
        if (b.Flashing) drawn = Draw(fb, "BOSS1_2.BOS", b.Form + 1, x, y);
        else if (b.PersonVisible) drawn = Draw(fb, "BOSS1_3.BOS", b.PersonImage, x, y);
        else drawn = Draw(fb, "BOSS1.BOS", b.SphereImage, x, y);
        if (!drawn) fb.FillRect(x, y, SinGyoku.W, SinGyoku.H, (byte)(b.Invincible ? 15 : 12));
        // HP bar (provisional position: the original's hud/hp.cpp layout is not ported)
        for (int i = 0; i < b.Hp; i++) fb.FillRect(128 + i * 12, 40, 10, 8, 9);
    }

    // ---- player ----

    void DrawPlayer(Framebuffer fb, GameWorld w)
    {
        var p = w.Player;
        if (_s.Phase == SessionPhase.MissAnimation && _s.MissStage == 1)
        {
            int cel = _s.MissFrame % 2;
            DrawMissEffect(fb, w.Player.Left, cel);
            DrawMissEffect(fb, _s.EffectLeft, cel);
            return;
        }
        switch (p.SpriteKind)
        {
            case PlayerSpriteKind.Ptn32:
                if (!Draw(fb, Miko, p.SpriteCel - 64, p.SpriteLeft, Player.Top))
                    fb.FillRect(p.SpriteLeft, Player.Top, Playfield.PlayerW, Playfield.PlayerH, 9);
                break;
            case PlayerSpriteKind.Bos48x48:
                if (!Draw(fb, MikoAc, p.SpriteCel, p.SpriteLeft, Playfield.Bottom - 48))
                    fb.FillRect(p.SpriteLeft, Playfield.Bottom - 48, 48, 48, 9);
                break;
            case PlayerSpriteKind.Bos48x32:
                if (!Draw(fb, MikoAc2, p.SpriteCel, p.SpriteLeft, Player.Top))
                    fb.FillRect(p.SpriteLeft, Player.Top, 48, 32, 9);
                break;
        }
    }

    void DrawMissEffect(Framebuffer fb, int left, int cel)
    {
        if (!Draw(fb, Miko, 20 + cel, left, Player.Top))
            fb.FillRect(left, Player.Top, Playfield.PlayerW, Playfield.PlayerH, 12);
    }

    // ---- HUD ----

    void DrawHud(Framebuffer fb, GameWorld w)
    {
        for (int i = 0; i < w.RemLives; i++)
            if (!DrawQuarter(fb, Stg, 0, 0, LivesLeft + i * ColW, LivesTop)) fb.FillRect(LivesLeft + i * ColW + 2, LivesTop + 2, 12, 12, 9);
        for (int i = 0; i < w.RemBombs; i++)
            if (!DrawQuarter(fb, Stg, 0, 1, BombsLeft + i * ColW, BombsTop)) fb.FillRect(BombsLeft + i * ColW + 2, BombsTop + 2, 12, 12, 11);
        if (_font == null) return;
        Digits(fb, ScoreLeft, ScoreTop, (int)Math.Min(w.Score, 9_999_999u), 7);
        Digits(fb, ComboLeft, ScoreTop, w.CardComboCur, 2);
        Digits(fb, StageLeft - ColW, StageTop, w.StageId + 1, 2);
        if (w.Stage != null) Digits(fb, TimerLeft, 0, w.Timer.Value, 4);
    }

    void Digits(Framebuffer fb, int x, int y, int value, int digits)
    {
        // Right-aligned, zero-padded like the HUD's fullwidth numbers, one cell of 16 px per digit.
        for (int i = digits - 1; i >= 0; i--, value /= 10)
            TextRenderer.DrawString(fb, _font!, ((char)('0' + value % 10)).ToString(), x + i * ColW + 4, y, 15);
    }

    void DrawBonus(Framebuffer fb)
    {
        if (_font == null || _s.Bonus is not { } b) return;
        fb.FillRect(48, 80, 544, 224, 0);
        int y = 96;
        void Line(string label, long v)
        {
            TextRenderer.DrawString(fb, _font, label, 80, y, 15);
            TextRenderer.DrawString(fb, _font, (v * 10).ToString().PadLeft(7), 400, y, 15);
            y += 32;
        }
        Line("TIME", b.Time);
        Line("CARD COMBO", b.CardCombo);
        Line("MIKO + BOMB", b.Resources);
        Line("STAGE", b.StageNumber);
        TextRenderer.DrawString(fb, _font, "TOTAL", 80, y, 14);
        TextRenderer.DrawString(fb, _font, b.Total.ToString().PadLeft(7), 400, y, 14);
    }
}
