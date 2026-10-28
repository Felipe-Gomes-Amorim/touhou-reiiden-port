using Th01.Core.Graphics;

namespace Th01.Core.Game;

/// <summary>Drawing of Sariel's fight (ref: th01/main/boss/b20m.cpp). Drawing only: it reads the boss's public state.</summary>
internal sealed partial class GameRenderer
{
    const string SarielLow = "BOSS6_L.GRP", SarielHigh = "BOSS6_H.GRP", SarielDefeatPicture = "BOSS6_A.GRP";
    const string SarielShield = "BOSS6_1.BOS", SarielWand = "BOSS6_2.BOS", SarielDress = "BOSS6_3.BOS";
    const string SarielVortexSheet = "BOSS6GR1.GRC", SarielBirdSheet = "BOSS6GR2.GRC", SarielCrossSheet = "BOSS6GR3.GRC";
    const byte ColSarielBird = 15, ColSarielLaser = 4, ColDetonationStart = 6, ColDetonation = 5;

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
    /// The background pictures of the fight: the scroll, diagonal transition and rings of the entrance; one of four pictures in
    /// the first form with a three-gust wipe between them; the defeat picture; the second form's picture.
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
                bool hi = Picture(SarielHigh, out f1), a1 = Picture(SarielBg(0), out f2);
                int progress = (t - Sariel.ScrollTicks) / Sariel.TransitionTicksPerFrame;   // diagonal 8x8 blocks, one diagonal per frame
                ComposePictures(fb, hi ? f1 : null, a1 ? f2 : null, (x, y) => (x >> 3) + (y >> 3) <= progress);
                if (a1 && f2.Palette.Length == 3 * Framebuffer.ColorCount) fb.ApplyPalette(f2);
            }
            return;
        }
        if (s.Phase == Sariel.PhaseForm1Defeated)
        {
            bool shaking = s.PhaseFrame <= Sariel.Form1DefeatShakeTicks;
            bool a = Picture(SarielDefeatPicture, out f1) || Picture(SarielBg(4), out f1);
            bool a6 = Picture(SarielBg(5), out f2);
            int progress = Math.Max(0, s.PhaseFrame - Sariel.Form1DefeatShakeTicks) / Sariel.TransitionTicksPerFrame;
            ComposePictures(fb, a ? f1 : null, a6 && !shaking ? f2 : null, (x, y) => (x >> 3) + (y >> 3) <= progress);
            if (a && f1.Palette.Length == 3 * Framebuffer.ColorCount) fb.ApplyPalette(f1);
            if (_s.Visuals.ScreenShake && s.Form1DefeatShakeLine != 0) fb.ScrollY = s.Form1DefeatShakeLine;
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
        bool cur = Picture(SarielBg(s.BackgroundId), out f1);
        if (s.TransitionProgress > 0 && Picture(SarielBg(s.NextBackground), out f2))
        {
            int wipeX = (int)(s.TransitionProgress * Framebuffer.Width);
            ComposePictures(fb, cur ? f1 : null, f2, (x, y) => x < wipeX);
            if (cur && f1.Palette.Length == 3 * Framebuffer.ColorCount && s.TransitionProgress < 1) fb.ApplyPalette(f1);
        }
        else if (cur)
        {
            ComposePictures(fb, f1, null, (x, y) => false);
            if (f1.Palette.Length == 3 * Framebuffer.ColorCount) fb.ApplyPalette(f1);
        }
        if (_s.Visuals.ScreenShake && s.ShakeLine != 0) fb.ScrollY = s.ShakeLine % Framebuffer.Height;
    }

    void DrawSariel(Framebuffer fb, Sariel s)
    {
        bool entering = s.Phase == 0 && _s.Phase == SessionPhase.BossEntrance;
        if (entering)
        {
            if (s.EntranceTick > Sariel.ScrollTicks + Sariel.TransitionFrames * Sariel.TransitionTicksPerFrame)
                DrawEntranceRings(fb, s.EntranceTick - Sariel.ScrollTicks - Sariel.TransitionFrames * Sariel.TransitionTicksPerFrame, 1, 16);
            return;
        }
        if (s.Phase is >= 1 and <= 8 or Sariel.PhaseForm2) DrawHpBar(fb, s);
        if (s.Phase == Sariel.PhaseForm1Defeated || s.Phase == Sariel.PhaseForm2Defeated) return;

        // the wand, shield and dress (animated sprites on top of the background picture)
        if (s.WandStage > 0 && !Draw(fb, SarielWand, s.WandStage - 1, Sariel.WandLeft, Sariel.WandTop))
            fb.FillRect(Sariel.WandLeft + 40, Sariel.WandTop, 24, 64, 12);
        bool form1 = s.Phase is >= 1 and <= 8;
        if (form1 && !Draw(fb, SarielDress, s.PhaseFrame % 60 / 15, Sariel.DressLeft, Sariel.DressTop))
            fb.FillRect(Sariel.DressLeft, Sariel.DressTop, 96, 32, 13);
        if (s.Phase is 1 or 3 or 5 or 7 && !Draw(fb, SarielShield, s.PhaseFrame % 40 / 10, Sariel.ShieldLeft, Sariel.ShieldTop))
            fb.FillRect(Sariel.ShieldLeft, Sariel.ShieldTop, s.Width, s.Height, (byte)(s.Invincible ? 15 : 12));

        DrawSarielPatterns(fb, s);
    }

    void DrawGrcSprite(Framebuffer fb, string sheet, int cel, int x, int y, byte color, int fallbackSize)
    {
        if (!DrawMono(fb, sheet, cel, x, y, color)) fb.FillRect(x + 16 - fallbackSize / 2, y + 16 - fallbackSize / 2, fallbackSize, fallbackSize, color);
    }

    static void DrawEllipse(Framebuffer fb, int cx, int cy, int rx, int ry, byte color)
    {
        int px = cx + rx, py = cy;
        for (int a = 1; a <= 64; a++)
        {
            int x = cx + (int)Math.Round(rx * Math.Cos(a * Math.PI / 32)), y = cy + (int)Math.Round(ry * Math.Sin(a * Math.PI / 32));
            DrawLine(fb, px, py, x, y, color);
            px = x; py = y;
        }
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
            if (i is >= 0 and < 10) DrawArc(fb, s.SpawnerX[i], s.SpawnerY[i], s.WarningRadius, 0, 0xFF, ColSarielLaser);
        }
        if (s.VorticesShown && f < 320)
            for (int i = 0; i < Sariel.VortexCount; i++) DrawGrcSprite(fb, SarielVortexSheet, f % 3, s.VortexLeft[i], s.VortexTop[i], ColWhite, 24);
        if (s.Phase == 1 && s.PatternCur == 2 && f is > 50 and < 200)
        {
            for (int i = 0; i < s.EggsAlive; i++) DrawGrcSprite(fb, SarielBirdSheet, 0, s.EggLeft[i], s.EggTop[i], ColSarielBird, 16);
            DrawGrcSprite(fb, SarielCrossSheet, f % 2, s.SpawnerLeft >> 4, s.SpawnerTop >> 4, (byte)(f % 4 + 2), 20);
        }

        // snowflake and its detonation
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
                DrawEllipse(fb, x, cy, s.SnowRadiusOuter1, s.SnowRadiusOuter2, outer);
                DrawEllipse(fb, x, cy, s.SnowRadiusOuter2, s.SnowRadiusOuter1, outer);
                DrawEllipse(fb, x, cy, s.SnowRadiusInner, s.SnowRadiusInner, ColWhite);
                if (s.SnowflakeState >= Sariel.SnowDetonationActive && !DrawMono(fb, Shape8Sheet, 1, s.SnowStarLeft, s.SnowStarTop, ColWhite))
                    fb.FillRect(s.SnowStarLeft, s.SnowStarTop, 8, 8, ColWhite);
            }
        }

        // rays: phase 5 aimed lasers and radial sweep, phase 7 and form 2 symmetric rays with debris
        if (s.Phase == 5 && s.PatternCur is 0 or 2 or 5 && f is > 50 and <= 250)
            DrawLine(fb, Playfield.CenterX, Sariel.FaceCenterY, s.RayOriginX, s.RayOriginY, ColWhite);
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
                DrawArc(fb, Sariel.SealCenterX, Sariel.SealCenterY, (f - 49) / 4 * 16 + 32, 0, 0xFF, ColWhite);
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

    /// <summary>The final defeat of Sariel: the picture shakes for 230 frames and the palette fades to black after frame 190.</summary>
    void DrawSarielDefeat(Framebuffer fb, Sariel s)
    {
        int frame = _s.DefeatElapsed / 2 + 1;
        if (frame < 230 && _s.Visuals.ScreenShake) fb.ScrollY = (frame % 2 == 0 ? Framebuffer.Height + 16 : Framebuffer.Height - 14) % Framebuffer.Height;
        if (frame > 190)
        {
            int steps = (frame - 190) / 2;
            MapPalette(fb, c => Math.Max(0, c - steps));
        }
    }
}
