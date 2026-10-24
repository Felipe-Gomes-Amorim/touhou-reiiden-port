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
    const int LivesLeft = 128, LivesTop = 0, BombsLeft = 128, BombsTop = 16, LivesPerRow = 6;
    const int ScoreLeft = 256, ScoreTop = 16, MaxTop = 0, ComboLeft = 256 + 9 * 16, RankCenterX = 600, RankTop = 48;
    const string HudMask = "mask";
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
        if (_s.Phase == SessionPhase.RouteSelect)
        {
            DrawRouteMenu(fb);
            return;
        }
        // The 640x66 mask (MASK.GRF) holds the HUD frame and captions; the original draws it in V_RED, under everything else.
        DrawMono(fb, HudMask, 0, 0, 0, ColRed);
        fb.FillRect(Playfield.Left, Playfield.Top - 2, Playfield.Width, 2, 1);

        if (w.Stage != null)
        {
            DrawCards(fb, w.Stage);
            DrawObstacles(fb, w.Stage);
        }
        bool bossGone = _s.Phase == SessionPhase.BossDefeat && _s.DefeatElapsed >= DefeatLineTicks(w.Boss!);
        if (w.Boss != null && !bossGone) DrawBoss(fb, w.Boss);
        DrawItems(fb, w.Items);
        DrawPellets(fb, w.Pellets);
        DrawMissiles(fb, w.Missiles);
        DrawLasers(fb, w.Lasers);
        DrawParticles(fb, w.Particles);
        DrawSparkles(fb, w.Sparkles);
        DrawShots(fb, w.Shots);
        DrawOrb(fb, w);
        DrawPlayer(fb, w);
        DrawHud(fb, w);
        if (_s.Phase == SessionPhase.BossDefeat) DrawDefeat(fb, w.Boss!);
        else if (w.Boss is { FlashOn: true } flashing) FlashColors(fb, flashing.FlashColors);
        if (_s.Phase == SessionPhase.StageBonus) DrawBonus(fb);
    }

    // ---- palette effects (drawing only; the simulation never sees them) ----

    const int PaletteMax = 15, ColWhiteIdx = 7;

    static int Comp(uint rgb, int shift) => (int)((rgb >> shift) & 0xFF) / 17;

    /// <summary>Rewrites every palette entry from its own 4-bit components.</summary>
    static void MapPalette(Framebuffer fb, Func<int, int> f)
    {
        Span<uint> old = stackalloc uint[Framebuffer.ColorCount];
        fb.Palette.CopyTo(old);
        for (int i = 0; i < old.Length; i++)
            fb.SetPalette4(i, f(Comp(old[i], 16)), f(Comp(old[i], 8)), f(Comp(old[i], 0)));
    }

    /// <summary>z_palette_flash_colors(): the listed hardware colours go to #FFF.</summary>
    static void FlashColors(Framebuffer fb, IReadOnlyList<int> colors)
    {
        foreach (int c in colors) fb.SetPalette4(c, PaletteMax, PaletteMax, PaletteMax);
    }

    /// <summary>Ticks the white lines take to cover the screen (defeat_animate's first loop).</summary>
    int DefeatLineTicks(Boss b)
    {
        int d = b.DefeatLineDistance, y = _s.DefeatStartY;
        return Math.Max(y / d + 1, (Playfield.Bottom - 1 - y) / d + 1);
    }

    /// <summary>
    /// Boss defeat. Rules (ref: th01/main/boss/defeat.cpp): every tick two full-width white lines are added, moving
    /// away from the start row by the line distance; the palette whitens one step per whiteout interval; when the
    /// lines have covered the screen everything turns white for a moment, the lines vanish with the boss, and the
    /// palette fades back one step per whitein interval.
    /// </summary>
    void DrawDefeat(Framebuffer fb, Boss boss)
    {
        int e = _s.DefeatElapsed, lines = DefeatLineTicks(boss), d = boss.DefeatLineDistance;
        if (e < lines)
        {
            int steps = e / boss.DefeatWhiteoutInterval;
            MapPalette(fb, c => Math.Min(PaletteMax, c + steps));
            int start = _s.DefeatStartY;
            for (int j = 0; j < e; j++)
            {
                fb.FillRect(0, start - d * j, Framebuffer.Width, 1, ColWhiteIdx);
                fb.FillRect(0, start + d * j, Framebuffer.Width, 1, ColWhiteIdx);
            }
            return;
        }
        // Fade: back from #FFF to the stage palette, one level per whitein interval.
        int level = PaletteMax - (e - lines) / boss.DefeatWhiteinInterval;
        MapPalette(fb, c => Math.Max(c, Math.Max(level, 0)));
    }

    // ---- route menu (ref: singyoku_defeat_animate_and_select_route) ----

    const int MakaiTop = 250, JigokuTop = 300, CursorLeft = 128, ColMakai = 1, ColJigoku = 3;

    void DrawRouteMenu(Framebuffer fb)
    {
        // The original repurposes colours 1 and 3 for the two route captions: the selected one white, the other 9/15.
        // The rest of the palette is the stage's, each component lowered by 4.
        MapPalette(fb, c => Math.Max(0, c - 4));
        fb.SetPalette4(ColWhiteIdx, PaletteMax, PaletteMax, PaletteMax);
        bool makai = _s.World.RouteChosen == 0;
        fb.SetPalette4(ColMakai, makai ? PaletteMax : 9, makai ? PaletteMax : 9, makai ? PaletteMax : 9);
        fb.SetPalette4(ColJigoku, makai ? 9 : PaletteMax, makai ? 9 : PaletteMax, makai ? 9 : PaletteMax);
        fb.Clear(0);
        if (_font != null)
        {
            // TODO(fonte japonesa): the original captions are Shift-JIS (ROUTE_SEL_1..5); provisional Latin text.
            TextRenderer.DrawString(fb, _font, "ROUND " + (_s.World.StageId + 1) + " CLEAR", 64, 64, ColWhiteIdx);
            TextRenderer.DrawString(fb, _font, "CHOOSE YOUR ROUTE", 32, 96, ColWhiteIdx);
            TextRenderer.DrawString(fb, _font, "UP / DOWN + SHOT", 32, 180, ColWhiteIdx);
            TextRenderer.DrawString(fb, _font, "MAKAI", 256, MakaiTop, ColMakai);
            TextRenderer.DrawString(fb, _font, "JIGOKU", 256, JigokuTop, ColJigoku);
        }
        int cy = makai ? MakaiTop : JigokuTop;
        if (!Draw(fb, Stg, 3, CursorLeft, cy)) fb.FillRect(CursorLeft, cy, Playfield.OrbW, Playfield.OrbH, ColWhiteIdx);
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

    // Embedded 1-bit sprites of the executable, registered by the platform under these sheet names. Frames are
    // drawn in a colour chosen here: every lit pixel (alpha if present, else a non-zero index) becomes <c>color</c>.
    const string PelletSheet = "pellet", PelletCloudSheet = "pellet_c";

    bool DrawMono(Framebuffer fb, string sheet, int frame, int x, int y, byte color)
    {
        if (_art == null || !_art.TryGetFrame(sheet, frame, out var f)) return false;
        var px = f.Pixels.Span;
        var al = f.Alpha.Span;
        for (int r = 0; r < f.Height; r++)
            for (int c = 0; c < f.Width; c++)
            {
                int k = r * f.Width + c;
                bool on = al.Length != 0 ? al[k] != 0 : px[k] != 0;
                if (on) fb.SetPixel(x + c, y + r, color);
            }
        return true;
    }

    void DrawPellets(Framebuffer fb, Pellets p)
    {
        for (int i = 0; i < Pellets.Count; i++)
        {
            int d = p.DecayFrame[i];
            if (p.Moving[i])
            {
                if (d >= 20) continue;
                int x = p.CurLeft[i] >> 4, y = p.CurTop[i] >> 4;
                int cel = (d + 9) / 10;                    // solid, then ring, then dotted ring while it decays
                if (!DrawMono(fb, PelletSheet, cel, x, y, ColWhite))
                    fb.FillRect(x, y, Pellets.W, Pellets.H, (byte)(d == 0 ? 15 : 13));
            }
            else if (p.CloudFrame[i] != 0)
            {
                // spawn cloud: frames 2..4 first cel, 6..8 second
                int cf = p.CloudFrame[i];
                int cel = cf is >= 2 and <= 4 ? 0 : cf is >= 6 and <= 8 ? 1 : -1;
                if (cel < 0) continue;
                int x = p.CloudLeft[i] - 4, y = p.CloudTop[i] - 4;
                if (!DrawMono(fb, PelletCloudSheet, cel, x, y, ColWhite))
                    fb.FillRect(p.CloudLeft[i], p.CloudTop[i], Pellets.W, Pellets.H, 13);
            }
        }
    }

    void DrawParticles(Framebuffer fb, ParticleField pf)
    {
        for (int i = 0; i < ParticleField.Count; i++)
        {
            if (!pf.Alive(i)) continue;
            var (x, y) = pf.PixelPos(i);
            fb.SetPixel(x, y, ColWhite);
        }
    }

    // THVRAM palette indices the original uses (th01/v_colors.hpp): gray 1, white 7, red 10.
    const byte ColWhite = 7, ColRed = 10;
    const string Shape8Sheet = "shape8x8";
    const int Shape8Invincibility = 3, Shape8InvincibilityCels = 4;

    /// <summary>
    /// The sparkle around Reimu: the invincibility cels of the embedded shape8x8 sheet in red. The sparkle's frame
    /// counter has already advanced past the frame that was drawn this tick; it plays cels 0..3 and back (1..4, then 4..1).
    /// </summary>
    void DrawSparkles(Framebuffer fb, InvincibilitySparkles sp)
    {
        if (!sp.Active) return;
        for (int i = 0; i < InvincibilitySparkles.Count; i++)
        {
            int f = sp.Frame[i] - 1;                       // the value the original draws with
            if (f is < 1 or > 8) continue;
            int x = sp.Left[i], y = sp.Top[i];
            if (x < 0) continue;                           // the original skips the left edge
            int cel = f <= Shape8InvincibilityCels ? f - 1 : 8 - f;
            if (!DrawMono(fb, Shape8Sheet, Shape8Invincibility + cel, x, y, ColRed))
                fb.FillRect(x, y, 8, 8, 14);
        }
    }

    void DrawLasers(Framebuffer fb, ShootoutLasers lasers)
    {
        // The original ORs a horizontal bar of (width_cel + 1) pixels, pre-shifted to the pixel's column, into every
        // cell of the ray (the 1-bit "laser_s" table). Drawing that bar directly needs no sheet.
        foreach (var l in lasers.Slots)
        {
            if (!l.Alive) continue;
            int x = l.RayStartLeft, y = l.RayStartY;
            for (int i = 0; i < l.RayLength; i++, x += l.StepX, y += l.StepY)
            {
                int px = x >> 8, py = y >> 8;
                if (px < Playfield.Left || px > Playfield.Right - 2 || py < Playfield.Top || py > Playfield.Bottom - 1) break;
                for (int k = 0; k <= l.WidthCel; k++) fb.SetPixel(px + k, py, (byte)l.Color);
            }
        }
    }

    void DrawMissiles(Framebuffer fb, Missiles m)
    {
        for (int i = 0; i < Missiles.Count; i++)
        {
            if (m.Flag[i] == (int)MissileState.Free) continue;
            bool moving = m.Flag[i] == (int)MissileState.Moving;
            int x = (moving ? m.CurLeft[i] : m.PrevLeft[i]) >> 4, y = (moving ? m.CurTop[i] : m.PrevTop[i]) >> 4;
            int cel = m.DirectionCel(i);
            bool ok = moving
                ? DrawQuarter(fb, "BOSS3_M.PTN", cel / 4, cel % 4, x, y)
                : DrawQuarter(fb, "BOSS3_M.PTN", 4, m.Flag[i] - (int)MissileState.Hit, x, y);
            if (!ok) fb.FillRect(x, y, Missiles.W, Missiles.H, (byte)(moving ? 10 : 12));
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

    void DrawBoss(Framebuffer fb, Boss boss)
    {
        if (boss is SinGyoku s) DrawSinGyoku(fb, s);
        else if (boss is Mima m) DrawMima(fb, m);
        else if (boss is YuugenMagan y) DrawYuugenMagan(fb, y);
    }

    void DrawSinGyoku(Framebuffer fb, SinGyoku b)
    {
        if (b.Phase == 0 && _s.Phase != SessionPhase.BossEntrance) return;
        int x = b.Left, y = b.Top;
        bool drawn;
        if (b.Flashing) drawn = Draw(fb, "BOSS1_2.BOS", b.Form + 1, x, y);
        else if (b.PersonVisible) drawn = Draw(fb, "BOSS1_3.BOS", b.PersonImage, x, y);
        else drawn = Draw(fb, "BOSS1.BOS", b.SphereImage, x, y);
        if (!drawn) fb.FillRect(x, y, SinGyoku.W, SinGyoku.H, (byte)(b.Invincible ? 15 : 12));
        // HP bar (provisional position: the original's hud/hp.cpp layout is not ported)
        DrawHpBar(fb, b);
    }

    void DrawYuugenMagan(Framebuffer fb, YuugenMagan y)
    {
        foreach (var e in y.Eyes)
        {
            if (e.Image == YuugenMagan.CHidden) continue;
            if (!Draw(fb, "BOSS2.BOS", e.Image, e.Left, e.Top))
                fb.FillRect(e.Left + 4, e.Top + 4, YuugenMagan.EyeW - 8, YuugenMagan.EyeH - 8, (byte)(e.HitboxInactive ? 13 : 12));
        }
        foreach (var l in y.Lines()) DrawLine(fb, l.x1, l.y1, l.x2, l.y2, ColWhite);
        DrawHpBar(fb, y);
    }

    void DrawMima(Framebuffer fb, Mima m)
    {
        if (m.Phase == 0 && _s.Phase != SessionPhase.BossEntrance) return;
        int x = m.Left, y = m.Top;
        if (!Draw(fb, "BOSS3_1.BOS", 0, x, y)) fb.FillRect(x, y, Mima.W, Mima.H, (byte)(m.Invincible ? 15 : 12));
        // animated part (meteor or cast pose), 48 px below the top edge
        int cel = m.CastVisible ? 0 : 1 + m.PhaseFrame / 8 % 4;
        if (m.CastVisible || m.MeteorActive) Draw(fb, "BOSS3_2.BOS", cel, x, y + 48);
        if (m.SquareFlag != 0)
        {
            for (int k = 0; k < 2; k++)
                if ((m.SquareFlag >> k & 1) != 0)
                    for (int i = 0; i < 4; i++)
                    {
                        var a = m.SquareCorner(k, i); var b = m.SquareCorner(k, (i + 1) % 4);
                        DrawLine(fb, a.x, a.y, b.x, b.y, 15);
                    }
        }
        DrawHpBar(fb, m);
        // Standing pillars (V_RED boxes: the embedded "pillar" sprite is a 2-row 1-bit pattern, not needed for a box)
        for (int i = 0; i < 8; i++)
        {
            var (time, cx) = m.Pillar(i);
            if (time is > 16 and <= 32 && m.Phase == 3)
            {
                int h = 8 * 2 * (32 + 2 - time);
                fb.FillRect(cx - 16, Playfield.Bottom - h, 32, h, ColRed);
            }
        }
    }

    /// <summary>
    /// Boss HP: one 7x15 block per point from (128, 48), 8 px apart. The first 2 points are red, up to the
    /// phase-1 limit (6) red/white dithered, the rest white. ref: th01/main/hud/hp.cpp
    /// </summary>
    static void DrawHpBar(Framebuffer fb, Boss b)
    {
        const int left = 128, top = 48, pointW = 8, h = 15, firstRedWhite = 2, firstWhite = 6;
        for (int p = 0; p < b.Hp; p++)
            for (int y = 0; y < h; y++)
                for (int x = 0; x < pointW - 1; x++)
                {
                    byte c = p < firstRedWhite ? ColRed : p < firstWhite ? (byte)((x & 1) == 0 ? ColRed : ColWhite) : ColWhite;
                    fb.SetPixel(left + p * pointW + x, top + y, c);
                }
    }

    static void DrawLine(Framebuffer fb, int x0, int y0, int x1, int y1, byte c)
    {
        int dx = Math.Abs(x1 - x0), dy = -Math.Abs(y1 - y0), sx = x0 < x1 ? 1 : -1, sy = y0 < y1 ? 1 : -1, err = dx + dy;
        while (true)
        {
            fb.SetPixel(x0, y0, c);
            if (x0 == x1 && y0 == y1) break;
            int e2 = 2 * err;
            if (e2 >= dy) { err += dy; x0 += sx; }
            if (e2 <= dx) { err += dx; y0 += sy; }
        }
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
        {
            int x = LivesLeft + i % LivesPerRow * ColW, y = LivesTop + i / LivesPerRow * ColW;
            if (!DrawQuarter(fb, Stg, 0, 0, x, y)) fb.FillRect(x + 2, y + 2, 12, 12, 9);
        }
        for (int i = 0; i < w.RemBombs; i++)
            if (!DrawQuarter(fb, Stg, 0, 1, BombsLeft + i * ColW, BombsTop)) fb.FillRect(BombsLeft + i * ColW + 2, BombsTop + 2, 12, 12, 11);
        if (_font == null) return;
        Digits(fb, ScoreLeft, MaxTop, (int)Math.Min(w.HudHiScore, 9_999_999u), 7);
        Digits(fb, ComboLeft, MaxTop, w.CardComboMax, 2);
        Digits(fb, ScoreLeft, ScoreTop, (int)Math.Min(w.Score, 9_999_999u), 7);
        Digits(fb, ComboLeft, ScoreTop, w.CardComboCur, 2);
        // Stage number: two half-width glyphs, right aligned (graph_putsa_fx at STAGE_LEFT).
        TextRenderer.DrawString(fb, _font, (w.StageId + 1).ToString().PadLeft(2), StageLeft, StageTop, ColWhite);
        // TODO(fonte japonesa): the original names are Shift-JIS (RANKS_CAPS_CENTERED); provisional Latin text.
        string rank = w.Rank.ToString().ToUpperInvariant();
        TextRenderer.DrawString(fb, _font, rank, RankCenterX - TextRenderer.Measure(_font, rank) / 2, RankTop, ColWhite);
        if (w.Stage != null) Digits(fb, TimerLeft, 0, w.Timer.Value, 4);
    }

    void Digits(Framebuffer fb, int x, int y, int value, int digits)
    {
        // Right-aligned, zero-padded like the HUD's fullwidth numbers, one cell of 16 px per digit.
        for (int i = digits - 1; i >= 0; i--, value /= 10)
            TextRenderer.DrawString(fb, _font!, ((char)('0' + value % 10)).ToString(), x + i * ColW + 4, y, ColWhite);
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
