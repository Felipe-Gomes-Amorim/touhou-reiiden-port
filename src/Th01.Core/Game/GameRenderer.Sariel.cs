using Th01.Core.Graphics;

namespace Th01.Core.Game;

/// <summary>
/// Drawing of Sariel's fight (ref: th01/main/boss/b20m.cpp). Drawing only: it reads the boss's public state.
/// The audit of every drawing call of the original is in docs/SARIEL-DRAW-AUDIT.md (private documentation).
/// </summary>
internal sealed partial class GameRenderer
{
    const string SarielLow = "BOSS6_L.GRP", SarielHigh = "BOSS6_H.GRP", InterleaveSheet = "ileave_m";
    const string SarielShield = "BOSS6_1.BOS", SarielWand = "BOSS6_2.BOS", SarielDress = "BOSS6_3.BOS";
    const string SarielVortexSheet = "BOSS6GR1.GRC", SarielBirdSheet = "BOSS6GR2.GRC", SarielCrossSheet = "BOSS6GR3.GRC";
    const byte ColSarielBird = 15, ColSarielLaser = 4, ColDetonationStart = 6, ColDetonation = 5, ColAir = 12;

    static string SarielBg(int index) => $"BOSS6_A{index + 1}.GRP";

    bool Picture(string name, out ArtFrame f)
    {
        f = default;
        return _art != null && _art.TryGetFrame(name, 0, out f) && f.Width == Framebuffer.Width && f.Height == Framebuffer.Height;
    }

    /// <summary>Fills the screen from two pictures: <paramref name="over"/> where the predicate holds, else <paramref name="under"/>.</summary>
    void ComposePictures(Framebuffer fb, ArtFrame? under, ArtFrame? over, Func<int, int, bool> overAt)
    {
        for (int y = 0; y < Framebuffer.Height; y++)
            for (int x = 0; x < Framebuffer.Width; x++)
            {
                byte c;
                if (over is { } o && overAt(x, y)) c = o.Pixels.Span[y * o.Width + x];
                else if (under is { } u) c = u.Pixels.Span[y * u.Width + x];
                else c = fb.GetPixel(x, y);
                fb.SetPixel(x, y, c);
            }
    }

    /// <summary>
    /// pagetrans_diagonal_8x8(): two diagonal waves of 8x8 squares, one from the top-left corner and one from the bottom-right,
    /// each interleaving the new picture into the old one through a checkerboard mask (mask 1 for the first wave, mask 2, its
    /// complement, for the second). Frame f has painted the squares with (column + row) = f for the first wave and
    /// 128 - f for the second; a square is half new after the first wave reaches it and entirely new after the second.
    /// Returns whether the pixel shows the new picture after <paramref name="frame"/> frames.
    /// </summary>
    Func<int, int, bool> DiagonalPageTransition(int frame)
    {
        // The masks come from REIIDEN.EXE ("ileave_m"); a bit set keeps the old dot. Without them: the same checkerboards.
        bool Keep(int mask, int x, int y)
        {
            if (_art != null && _art.TryGetFrame(InterleaveSheet, mask, out var m) && m.Width == 8 && m.Height == 8)
                return m.Pixels.Span[(y & 7) * 8 + (x & 7)] != 0;
            return mask == 1 ? ((x + y) & 1) == 1 : ((x + y) & 1) == 0;
        }
        return (x, y) =>
        {
            int diagonal = (x >> 3) + (y >> 3);
            bool first = diagonal <= frame, second = 128 - diagonal <= frame;
            return first && !Keep(1, x, y) || second && !Keep(2, x, y);
        };
    }

    /// <summary>
    /// The background pictures of the fight: the scroll, diagonal transition and rings of the entrance; one of four pictures in
    /// the first form (changed after three gusts of streaks); the shaking picture and the dithered transition to the dark one
    /// when the first form falls; the second form's picture.
    /// </summary>
    void DrawSarielBackdrop(Framebuffer fb, Sariel s)
    {
        ArtFrame f1, f2;
        if (s.Phase == 0)
        {
            int t = Math.Max(s.EntranceTick, 1);
            if (t <= Sariel.ScrollTicks)
            {
                // The picture rises from the bottom: row by row the high picture replaces the low one, and the view follows.
                int line = Framebuffer.Height - t;
                bool lo = Picture(SarielLow, out f1), hi = Picture(SarielHigh, out f2);
                ComposePictures(fb, lo ? f1 : null, hi ? f2 : null, (x, y) => y >= line);
                if (hi && f2.Palette.Length == 3 * Framebuffer.ColorCount) fb.ApplyPalette(f2);
                fb.ScrollY = line % Framebuffer.Height;
            }
            else
            {
                // The first picture is already on page 1 with its palette, so the old one is seen with the new palette.
                bool hi = Picture(SarielHigh, out f1), a1 = Picture(SarielBg(0), out f2);
                int frame = Sariel.TransitionFrameAt(t - Sariel.ScrollTicks);
                ComposePictures(fb, hi ? f1 : null, a1 ? f2 : null, t - Sariel.ScrollTicks >= Sariel.TransitionTicks ? (x, y) => true : DiagonalPageTransition(frame));
                if (a1 && f2.Palette.Length == 3 * Framebuffer.ColorCount) fb.ApplyPalette(f2);
            }
            return;
        }
        if (s.Phase == Sariel.PhaseForm1Defeated)
        {
            // measured: the hardware shake (alternating every tick, palette entry 15 white on the "down" ticks and black on the
            // "up" ones), the old picture for ~10 more ticks, then the form 2 scene appears at once
            if (s.PhaseFrame < Sariel.Form1DefeatShakeTicks + Sariel.Form1DefeatSwapTicks)
            {
                if (Picture(SarielBg(s.BackgroundId), out f1))
                {
                    ComposePictures(fb, f1, null, (x, y) => false);
                    if (f1.Palette.Length == 3 * Framebuffer.ColorCount) fb.ApplyPalette(f1);
                }
                if (s.PhaseFrame >= Sariel.Form1DefeatShakeStart && s.PhaseFrame < Sariel.Form1DefeatShakeTicks)
                {
                    if (_s.Visuals.ScreenShake && s.Form1DefeatShakeLine != 0) fb.ScrollY = s.Form1DefeatShakeLine;
                    if (!_s.Visuals.SoftFlashes) { int v = s.Form1DefeatFlash ? 15 : 0; fb.SetPalette4(15, v, v, v); }
                }
                return;
            }
            if (Picture(SarielBg(5), out f2))
            {
                ComposePictures(fb, f2, null, (x, y) => false);
                if (f2.Palette.Length == 3 * Framebuffer.ColorCount) fb.ApplyPalette(f2);
            }
            return;
        }
        if (s.Phase >= Sariel.PhaseForm2)
        {
            if (Picture(SarielBg(5), out f1))
            {
                ComposePictures(fb, f1, null, (x, y) => false);
                if (f1.Palette.Length == 3 * Framebuffer.ColorCount) fb.ApplyPalette(f1);
            }
            if (s.Phase == Sariel.PhaseForm2) fb.SetPalette4(6, s.PulseR, s.PulseG, s.PulseB);
            return;
        }
        // first form: the current picture; the next one only appears when the third gust has passed (the phase then changes)
        if (Picture(SarielBg(s.BackgroundId), out f1))
        {
            ComposePictures(fb, f1, null, (x, y) => false);
            if (f1.Palette.Length == 3 * Framebuffer.ColorCount) fb.ApplyPalette(f1);
            if (s.Phase is 2 or 4 or 6 or 8 && s.PhaseFrame >= 50) DrawGustStreaks(fb, s);
        }
        if (_s.Visuals.ScreenShake && s.ShakeLine != 0) fb.ScrollY = s.ShakeLine % Framebuffer.Height;
    }

    /// <summary>
    /// bg_transition(): a gust of streaks sweeps the picture left to right, three times. Each call (20 per gust) moves the cell
    /// 32 pixels and draws, on every 4th row, a comet of five 8-pixel stripes in palette entries 2, 3, 4, 3, 2 (16 pixels
    /// apart, trailing to the left), only over the empty background (colour 12), shifted by a random 0..7 pixels per row.
    /// APPROXIMATION: the shift is a fixed pseudo-random function of the row (the original draws random numbers).
    /// </summary>
    void DrawGustStreaks(Framebuffer fb, Sariel s)
    {
        if (s.GustId >= 3) return;
        int cellX = s.GustCalls * 32;
        ReadOnlySpan<byte> tones = [2, 3, 4, 3, 2];
        for (int y = 0; y < Framebuffer.Height; y += 4)
        {
            int shift = (y / 4 * 5 + s.GustId * 3 + s.GustCalls * 7) % 8;
            for (int id = 0; id < 5; id++)
            {
                int x0 = cellX + shift - id * 16;
                for (int x = x0; x < x0 + 8; x++)
                    if (x >= 0 && x < Framebuffer.Width && fb.GetPixel(x, y) == ColAir) fb.SetPixel(x, y, tones[id]);
            }
        }
    }

    void DrawSariel(Framebuffer fb, Sariel s)
    {
        bool entering = s.Phase == 0 && _s.Phase == SessionPhase.BossEntrance;
        if (entering)
        {
            int ringTicks = s.EntranceTick - Sariel.ScrollTicks - Sariel.TransitionTicks;
            if (ringTicks > 0) DrawEntranceRings(fb, ringTicks * 2, 1, 16);      // two frames of the original per tick
            return;
        }
        if (s.Phase is >= 1 and <= 8 or Sariel.PhaseForm2)
            DrawHpBar(fb, s, s.InitialHpRendered || s.Phase is not (1 or Sariel.PhaseForm2) ? s.Hp : s.HpShown);
        if (s.Phase is Sariel.PhaseForm2Defeated) return;
        bool shaking99 = s.Phase == Sariel.PhaseForm1Defeated;
        bool pictureSprites = s.Phase is >= 1 and <= 8 || shaking99 && s.PhaseFrame < Sariel.Form1DefeatShakeTicks + Sariel.Form1DefeatSwapTicks;
        if (!pictureSprites && s.Phase != Sariel.PhaseForm2) return;

        // the wand, shield and dress (animated sprites on top of the background picture)
        if (pictureSprites)
        {
            if (s.WandStage > 0 && !Draw(fb, SarielWand, s.WandStage - 1, Sariel.WandLeft, Sariel.WandTop))
                fb.FillRect(Sariel.WandLeft + 40, Sariel.WandTop, 24, 64, 12);
            if (s.DressShown && !Draw(fb, SarielDress, s.DressCel, Sariel.DressLeft, Sariel.DressTop))
                fb.FillRect(Sariel.DressLeft, Sariel.DressTop, 96, 32, 13);
            if (s.ShieldShown && !Draw(fb, SarielShield, s.ShieldCel, Sariel.ShieldLeft, Sariel.ShieldTop))
                fb.FillRect(Sariel.ShieldLeft, Sariel.ShieldTop, s.Width, s.Height, (byte)(s.Invincible ? 15 : 12));
        }
        if (s.Phase is >= 1 and <= 8 or Sariel.PhaseForm2) DrawSarielPatterns(fb, s);
    }

    void DrawGrcSprite(Framebuffer fb, string sheet, int cel, int x, int y, byte color, int fallbackSize)
    {
        if (!DrawMono(fb, sheet, cel, x, y, color)) fb.FillRect(x + 16 - fallbackSize / 2, y + 16 - fallbackSize / 2, fallbackSize, fallbackSize, color);
    }

    /// <summary>shape_ellipse_arc_put(): one dot every <paramref name="step"/> of the 256 angles (the shapes of the original are dotted).</summary>
    static void DrawDottedEllipse(Framebuffer fb, int cx, int cy, int rx, int ry, int step, byte color)
    {
        for (int a = 0; a <= 0xFF; a += step)
            fb.SetPixel(AngleMath.PolarX(cx, rx, a), AngleMath.PolarY(cy, ry, a), color);
    }

    void DrawSarielPatterns(Framebuffer fb, Sariel s)
    {
        int f = s.PhaseFrame;
        // decoration: 2x2 particles
        for (int i = 0; i < Sariel.ParticleCount; i++)
        {
            if (s.ParticleAlive[i] && s.Phase is 3 or 7) fb.FillRect(s.ParticleLeft[i], s.ParticleTop[i], 2, 2, ColSarielLaser);
            if (s.WavyAlive[i] && s.Phase == 5) fb.FillRect(AngleMath.PolarY(s.WavyLeft[i], 16, s.WavyAge[i]), s.WavyTop[i], 2, 2, ColSarielLaser);
            if (s.HParticleAlive[i] && s.Phase == Sariel.PhaseForm2) fb.FillRect(s.HParticleLeft[i] / 10, s.HParticleTop[i], 2, 2, ColSarielLaser);
        }

        // birds: hatching eggs and flying birds
        for (int i = 0; i < Sariel.BirdCount; i++)
        {
            if (!s.BirdAlive[i]) continue;
            int cel = s.BirdHatch[i] > 0 ? 1 + (Sariel.BirdHatchTicks - s.BirdHatch[i]) / (Sariel.BirdHatchTicks / 5) : 6 + f / 5 % 2;
            DrawGrcSprite(fb, SarielBirdSheet, cel, s.BirdLeft[i] >> 4, s.BirdTop[i] >> 4, ColSarielBird, 20);
        }

        if (s.Phase == 1 && s.PatternCur == 0 && s.WarningRadius > 0)
        {
            int i = (f - 50) / 10;
            if (i is >= 0 and < 10) DrawDottedEllipse(fb, s.SpawnerX[i], s.SpawnerY[i], s.WarningRadius, s.WarningRadius, 1, ColSarielLaser);
        }
        if (s.VorticesShown && f < 320)
            for (int i = 0; i < Sariel.VortexCount; i++) DrawGrcSprite(fb, SarielVortexSheet, f % 3, s.VortexLeft[i], s.VortexTop[i], ColWhite, 24);
        if (s.Phase == 1 && s.PatternCur == 2 && f is > 50 and < 200)
        {
            for (int i = 0; i < s.EggsAlive; i++) DrawGrcSprite(fb, SarielBirdSheet, 0, s.EggLeft[i], s.EggTop[i], ColSarielBird, 16);
            DrawGrcSprite(fb, SarielCrossSheet, f % 2, s.SpawnerLeft >> 4, s.SpawnerTop >> 4, (byte)(f % 4 + 2), 20);
        }

        // snowflake and its detonation (the ellipses are dotted: every 8th angle for the outer ones, every 6th for the inner)
        if (s.Phase == 3 && s.PatternCur == 0)
        {
            int x = s.SnowflakeLeft >> 4, y = s.SnowflakeTop >> 4;
            if (s.SnowflakeState == Sariel.SnowFlakeMoving)
            {
                if (!DrawMono(fb, Shape8Sheet, 2, x, y, ColWhite)) fb.FillRect(x, y, 8, 8, ColWhite);
            }
            else if (s.SnowflakeState >= Sariel.SnowDetonationStart)
            {
                int cy = Player.Top + Playfield.PlayerH / 2;
                byte outer = s.SnowflakeState <= Sariel.SnowDetonationStart + 1 ? (byte)ColDetonationStart : (byte)ColDetonation;
                DrawDottedEllipse(fb, x, cy, s.SnowRadiusOuter1, s.SnowRadiusOuter2, 8, outer);
                DrawDottedEllipse(fb, x, cy, s.SnowRadiusOuter2, s.SnowRadiusOuter1, 8, outer);
                DrawDottedEllipse(fb, x, cy, s.SnowRadiusInner, s.SnowRadiusInner, 6, ColWhite);
                if (s.SnowflakeState >= Sariel.SnowDetonationActive && !DrawMono(fb, Shape8Sheet, 1, s.SnowStarLeft, s.SnowStarTop, ColWhite))
                    fb.FillRect(s.SnowStarLeft, s.SnowStarTop, 8, 8, ColWhite);
            }
        }

        // rays: phase 5 aimed lasers (the ray moves 16 px per frame from the laser's origin) and radial sweep,
        // phase 7 and form 2 symmetric rays with debris
        if (s.Phase == 5 && s.PatternCur is 0 or 2 or 5 && f > s.RayStartFrame && f is > 50 and < 250 && f != 100 && f != 150 && f != 200)
        {
            int k = f - s.RayStartFrame - 1;
            DrawLine(fb, Playfield.CenterX, Sariel.FaceCenterY, s.RayOriginX + k * s.RayVelX, s.RayOriginY + k * s.RayVelY, ColWhite);
        }
        if (s.Phase == 5 && s.PatternCur == 3 && f is >= 150 and < 200)
        {
            int n = f - 150, angle = n * (n + 1) / 2 & 0xFF;
            DrawLine(fb, Playfield.CenterX, Sariel.ShieldCenterY - 4, AngleMath.PolarX(Playfield.CenterX, Sariel.SealRadius, angle),
                AngleMath.PolarY(Sariel.ShieldCenterY - 4, Sariel.SealRadius, angle), ColWhite);
        }
        bool raysP7 = s.Phase == 7 && (s.PatternCur == 0 && f is > 50 and < 100 || s.PatternCur == 2 && f is > 50 and < 130);
        bool raysF2 = s.Phase == Sariel.PhaseForm2 && s.PatternCur is 2 or 7 && f >= 50 && s.RaysTargetY > Sariel.SealCenterY;
        if (raysP7 || raysF2)
        {
            int ox = Playfield.CenterX, oy = raysP7 ? Sariel.FaceCenterY : Sariel.SealCenterY;
            int lx = s.RaysTargetLeftX, rx = Playfield.Right - 1 - s.RaysTargetLeftX, ty = s.RaysTargetY;
            DrawLine(fb, ox, oy, lx, ty, ColWhite);
            DrawLine(fb, ox, oy, rx, ty, ColWhite);
            int offset = raysP7 ? 8 : 32, cel = 3 + f / 2 % 3;
            DrawGrcSprite(fb, SarielVortexSheet, cel, lx - (32 - offset), ty - 16, ColWhite, 24);
            DrawGrcSprite(fb, SarielVortexSheet, cel, Playfield.Right - offset - lx, ty - 16, ColWhite, 24);
        }

        // form 2: the dotted circle that opens the curved spray, and the leaves
        if (s.Phase == Sariel.PhaseForm2)
        {
            if (s.PatternCur is 1 or 6 && f is >= 50 and < 140)
                DrawDottedEllipse(fb, Sariel.SealCenterX, Sariel.SealCenterY, (f - 49) / 4 * 16 + 32, (f - 49) / 4 * 16 + 32, 1, ColWhite);
            for (int i = 0; i < Sariel.LeafCount; i++)
            {
                int flag = s.LeafFlag[i];
                if (flag == Sariel.LfFree || flag is >= Sariel.LfSplash and <= Sariel.LfSplashDone) continue;   // the splash is never seen in the original
                string sheet = flag == Sariel.LfSpark ? "leaf_s" : s.LeafVelocityX(i) < 0 ? "leaf_l" : "leaf_r";
                int x = s.LeafLeft[i] / 10, y = s.LeafTop[i] / 10;
                if (!DrawMono(fb, sheet, 0, x, y, ColWhite)) fb.FillRect(x, y, 8, 8, ColWhite);
            }
        }
    }

    /// <summary>
    /// The final defeat of Sariel, as measured by T5: from tick 5 the picture shakes by hardware scroll with a period of 4 ticks
    /// (2 ticks 16 lines up, 2 ticks 14 down; no colour flash), the palette fades to black one level every 4 ticks from tick 384, and
    /// from 464 the scroll is back at 0 and the screen stays black. ScreenShake only controls the scroll.
    /// </summary>
    void DrawSarielDefeat(Framebuffer fb, Sariel s)
    {
        int t = _s.DefeatElapsed;
        if (t >= Sariel.DefeatShakeStart && t < Sariel.DefeatShakeEnd && _s.Visuals.ScreenShake)
            fb.ScrollY = ((t / 2) % 2 == 0 ? Framebuffer.Height + 16 : Framebuffer.Height - 14) % Framebuffer.Height;
        int steps = t >= Sariel.DefeatShakeEnd ? 15 : t > Sariel.DefeatFadeStart ? (t - Sariel.DefeatFadeStart) / Sariel.DefeatFadeTicksPerStep : 0;
        if (steps > 0) MapPalette(fb, c => Math.Max(0, c - steps));
    }
}
