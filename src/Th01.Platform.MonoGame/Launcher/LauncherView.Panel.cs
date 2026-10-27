using Th01.Assets;
using Th01.Core.Graphics;
using static Th01.Platform.MonoGame.Launcher.LauncherColors;

namespace Th01.Platform.MonoGame.Launcher;

public static partial class LauncherView
{
    /// <summary>Text for a refused game choice, in the launcher's language.</summary>
    public static string ErrorText(LauncherModel m, GameError e)
    {
        switch (e.Code)
        {
            case GameProblemCode.NotFound: return m.Text(Str.ErrNotFound);
            case GameProblemCode.DiskImageUnrecognized or GameProblemCode.DiskImageTooLarge: return m.Text(Str.ErrDiskImage);
            case GameProblemCode.MissingFile or GameProblemCode.ArchiveEntryMissing:
                return m.Text(Str.ErrMissingFile) + (e.File is null ? "" : " " + e.File);
            default: return m.Text(Str.ErrDamaged) + (e.File is null ? "" : " " + e.File);
        }
    }

    /// <summary>Cuts a string to fit a width, ending in an ellipsis.</summary>
    public static string Ellipsize(IFontSource font, string text, int maxWidth)
    {
        if (LauncherCanvas.MeasureText(font, text) <= maxWidth) return text;
        string cut = text;
        while (cut.Length > 1 && LauncherCanvas.MeasureText(font, cut + "…") > maxWidth) cut = cut[..^1];
        return cut + "…";
    }

    const int PanelX = 800, PanelW = 408, PanelInner = PanelW - 4 - 32;

    /// <summary>The panel on the right: GAME IN USE on the home screen, FIRST RUN while no valid game is chosen.</summary>
    static void DrawGamePanel(LauncherModel m, LauncherCanvas c, IFontSource font)
    {
        bool first = m.FirstRun;
        int py = first ? 190 : 250;
        int x = PanelX + 18;
        var errLines = m.Error is null ? null : Wrap(font, ErrorText(m, m.Error), PanelInner - 28);
        var lead = first ? Wrap(font, m.Text(Str.FirstRunLead), PanelInner) : null;
        var body = first ? Wrap(font, m.Text(Str.FirstRunBody), PanelInner) : null;

        int Run(bool draw)
        {
            int y = py + 30 + 18;
            if (first)
            {
                foreach (var l in lead!) { if (draw) c.DrawText(font, l, x, y, White); y += 24; }
                y += 8;
                foreach (var l in body!) { if (draw) c.DrawText(font, l, x, y, Purple3); y += 22; }
                y += 16;
            }
            if (errLines is not null)
            {
                int bh = errLines.Count * 22 + 20;
                if (draw)
                {
                    c.StrokeRect(x, y, PanelInner, bh, 2, Red);
                    for (int i = 0; i < errLines.Count; i++) c.DrawText(font, errLines[i], x + 14, y + 10 + i * 22, PinkText);
                }
                y += bh + 16;
            }
            if (first)
            {
                if (draw)
                {
                    c.FillRect(x, y + 2, 12, 12, Red);
                    c.DrawText(font, m.Text(Str.FirstRunStateNone), x + 22, y, PinkText);
                }
                y += 16 + 16;
            }
            else
            {
                if (draw)
                {
                    c.DrawText(font, m.Text(Str.PanelOrigin), x, y, Purple3, 1, 2);
                    c.DrawText(font, Ellipsize(font, m.Game.Origin ?? "", PanelInner), x, y + 20, White);
                }
                y += 40 + 16;
                if (draw)
                {
                    c.DrawText(font, m.Text(Str.PanelState), x, y, Purple3, 1, 2);
                    c.FillRect(x, y + 24, 12, 12, Green);
                    c.DrawText(font, m.Text(Str.StateValid), x + 22, y + 22, Green);
                }
                y += 40 + 16;
                if (draw) c.DrawText(font, m.Text(Str.PanelVersion), x, y, Purple3, 1, 2);
                y += 24;
                if (draw)
                {
                    int cx = x;
                    foreach (var (ver, key) in new[] { (GameVersion.Japanese, Str.VerJapanese), (GameVersion.Translated, Str.VerTranslated) })
                    {
                        string t = m.Text(key);
                        int cw = LauncherCanvas.MeasureText(font, t) + 24;
                        if (m.Game.Version == ver) { c.FillRect(cx, y, cw, 24, Cyan); c.DrawText(font, t, cx + 12, y + 4, Dark); }
                        else { c.StrokeRect(cx, y, cw, 24, 2, Purple2); c.DrawText(font, t, cx + 12, y + 4, Purple3); }
                        cx += cw + 8;
                    }
                    if (m.Game.Version == GameVersion.Unknown)
                        c.DrawText(font, m.Text(Str.VerUnknown), cx + 4, y + 4, Yellow);
                }
                y += 24 + 16;
            }
            // The button.
            bool focus = m.FocusedItem == HomeItem.Change;
            string label = m.Text(first ? Str.ButtonSelect : Str.ButtonChange);
            if (draw)
            {
                if (focus) c.FillRect(x + 4, y + 4, PanelInner, 46, Red);
                if (focus) c.FillRect(x, y, PanelInner, 46, White);
                else if (first) c.FillRect(x, y, PanelInner, 46, Red);
                else c.StrokeRect(x, y, PanelInner, 46, 2, White);
                int lw = LauncherCanvas.MeasureText(font, label, 1, 1);
                c.DrawText(font, label, x + (PanelInner - lw) / 2, y + 15, focus || first ? Dark : White, 1, 1);
            }
            y += 46 + 20;
            return y - py;
        }

        int h = Run(false);
        DrawPanelFrame(c, font, PanelX, py, PanelW, h, first ? Yellow : White,
            m.Text(first ? Str.FirstRunHeader : Str.PanelGameInUse), first ? "1 / 1" : PanelFileName);
        Run(true);
    }

    const string PanelFileName = "launcher.json";

    /// <summary>"PLAY is off" note under the menu on the first-run screen.</summary>
    static void DrawPlayOffNote(LauncherModel m, LauncherCanvas c, IFontSource font)
    {
        if (!m.FirstRun) return;
        var lines = Wrap(font, m.Text(Str.PlayOff), 460 - 16);
        c.FillRect(96, 550, 4, lines.Count * 24, Yellow);
        for (int i = 0; i < lines.Count; i++) c.DrawText(font, lines[i], 96 + 16, 550 + i * 24 + 4, Yellow);
    }
}
