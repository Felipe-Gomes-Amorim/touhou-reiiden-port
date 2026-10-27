using Th01.Core.Graphics;
using static Th01.Platform.MonoGame.Launcher.LauncherColors;

namespace Th01.Platform.MonoGame.Launcher;

public static partial class LauncherView
{
    const int RowsX = 72, RowsY = 272, RowsW = 700, RowH = 52, RowPitch = 58;

    static void DrawOptionsBody(LauncherModel m, LauncherCanvas c, IFontSource font)
    {
        DrawOptionRows(m, c, font);
        DrawDescription(m, c, font);
        if (m.ShowingReport) DrawReport(m, c, font);
    }

    static void DrawOptionRows(LauncherModel m, LauncherCanvas c, IFontSource font)
    {
        var rows = m.Rows;
        int first = m.RowScroll;
        for (int i = 0; i < LauncherModel.VisibleRows && first + i < rows.Count; i++)
        {
            var r = rows[first + i];
            int y = RowsY + i * RowPitch;
            bool focus = first + i == m.Row && !m.ShowingReport;
            Hit(RowsX, y, RowsW, RowH, HitKind.Row, first + i);
            if (focus)
            {
                c.FillRect(RowsX + 4, y + 4, RowsW, RowH, Red);
                c.FillRect(RowsX, y, RowsW, RowH, Purple1);
                c.StrokeRect(RowsX, y, RowsW, RowH, 2, White);
            }
            else
            {
                c.FillRect(RowsX, y, RowsW, RowH, Panel, 217);
                c.StrokeRect(RowsX, y, RowsW, RowH, 2, Purple2);
            }
            c.DrawText(font, m.Text(r.Label), RowsX + 16, y + 18, White);
            DrawRowControl(c, font, r, RowsX + RowsW - 16, y, focus, first + i);
        }
        // Arrows when the list goes on above or below the visible rows.
        if (first > 0) Triangle(c, RowsX + RowsW / 2, RowsY - 12, true);
        if (first + LauncherModel.VisibleRows < rows.Count) Triangle(c, RowsX + RowsW / 2, RowsY + LauncherModel.VisibleRows * RowPitch - 2, false);
    }

    static void Triangle(LauncherCanvas c, int cx, int y, bool up)
    {
        for (int i = 0; i < 6; i++)
            c.FillRect(cx - i, up ? y + 5 - i : y + i, 2 * i + 1, 1, Cyan);
    }

    /// <summary>Right-aligned control of a row; <paramref name="rx"/> is the right edge.</summary>
    static void DrawRowControl(LauncherCanvas c, IFontSource font, OptionRow r, int rx, int y, bool focus, int rowIndex)
    {
        switch (r.Kind)
        {
            case RowKind.Toggle:
            {
                int sx = rx - 58;
                c.FillRect(sx, y + 13, 58, 26, r.On ? Red : Dark);
                c.StrokeRect(sx, y + 13, 58, 26, 2, White);
                c.FillRect(sx + (r.On ? 32 : 4), y + 17, 18, 18, r.On ? White : Purple3);
                int vw = LauncherCanvas.MeasureText(font, r.Value);
                c.DrawText(font, r.Value, sx - 14 - vw, y + 18, r.On ? Green : Purple3);
                break;
            }
            case RowKind.Choice:
            {
                int vw = LauncherCanvas.MeasureText(font, r.Value);
                int x = rx - (8 + 12 + vw + 12 + 8);
                c.DrawText(font, "<", x, y + 18, Cyan);
                c.DrawText(font, r.Value, x + 8 + 12, y + 18, White);
                c.DrawText(font, ">", x + 8 + 12 + vw + 12, y + 18, Cyan);
                break;
            }
            case RowKind.Bar:
            {
                int x0 = rx - (10 * 16 + 9 * 3);
                for (int i = 0; i < 10; i++)
                {
                    int cx = x0 + i * 19;
                    Hit(cx, y + 14, 19, 24, HitKind.BarCell, rowIndex, i + 1);
                    c.FillRect(cx, y + 14, 16, 24, i < r.Level ? Cyan : Dark);
                    c.StrokeRect(cx, y + 14, 16, 24, 2, White);
                }
                int vw = LauncherCanvas.MeasureText(font, r.Value);
                c.DrawText(font, r.Value, x0 - 16 - vw, y + 18, Purple3);
                break;
            }
            case RowKind.Keys:
            {
                if (r.Listening)
                {
                    int tw = LauncherCanvas.MeasureText(font, r.Value);
                    c.DrawText(font, r.Value, rx - tw, y + 18, Yellow);
                    break;
                }
                int x = rx;
                for (int i = r.Keys!.Length - 1; i >= 0; i--)
                {
                    int tw = LauncherCanvas.MeasureText(font, r.Keys[i]);
                    int w = Math.Max(30, tw + 16);
                    x -= w;
                    c.StrokeRect(x, y + 11, w, 30, 2, White);
                    c.DrawText(font, r.Keys[i], x + (w - tw) / 2, y + 18, White);
                    x -= 6;
                }
                break;
            }
            case RowKind.Button:
            {
                int tw = LauncherCanvas.MeasureText(font, r.Button!, 1, 1);
                int w = tw + 32;
                uint edge = r.Danger ? Red : White;
                if (focus) c.FillRect(rx - w, y + 10, w, 32, edge);
                c.StrokeRect(rx - w, y + 10, w, 32, 2, edge);
                c.DrawText(font, r.Button!, rx - w + 16, y + 18, focus ? Dark : r.Danger ? PinkText : White, 1, 1);
                break;
            }
            case RowKind.Info:
            {
                int x = rx;
                if (r.Tag is { Length: > 0 })
                {
                    int tw = LauncherCanvas.MeasureText(font, r.Tag);
                    x -= tw + 16;
                    c.StrokeRect(x, y + 16, tw + 16, 22, 2, Purple2);
                    c.DrawText(font, r.Tag, x + 8, y + 19, Purple3);
                    x -= 14;
                }
                int vw = LauncherCanvas.MeasureText(font, r.Value);
                c.DrawText(font, r.Value, x - vw, y + 18, r.Good ? Green : Purple3);
                break;
            }
        }
    }


    static void DrawDescription(LauncherModel m, LauncherCanvas c, IFontSource font)
    {
        const int x = 800, y = 272, w = 408, h = 340;
        c.FillRect(x + 6, y + 6, w, h, Purple1);
        c.FillRect(x, y, w, h, Panel, 235);
        c.StrokeRect(x, y, w, h, 2, White);
        c.DrawText(font, TabKanji[(int)m.Tab], x + w - 14 - 192, y + h - 14 - 192, Purple1, 12);
        var row = m.FocusedRow;
        c.DrawText(font, m.Text(Str.DescHeading), x + 20, y + 20, Purple3, 1, 2);
        string title = m.Text(row.Label);
        int ty = y + 46;
        if (LauncherCanvas.MeasureText(font, title, 2) <= w - 40) { c.DrawText(font, title, x + 20, ty, Cyan, 2); ty += 44; }
        else
        {
            foreach (var l in Wrap(font, title, w - 40)) { c.DrawText(font, l, x + 20, ty, Cyan); ty += 26; }
            ty += 10;
        }
        foreach (var l in Wrap(font, m.Text(row.Hint), w - 40)) { c.DrawText(font, l, x + 20, ty, White); ty += 28; }
    }

    static void DrawReport(LauncherModel m, LauncherCanvas c, IFontSource font)
    {
        const int x = 72, y = 224, w = 1136, h = 380;
        c.FillRect(x + 6, y + 6, w, h, Purple1);
        c.FillRect(x, y, w, h, Dark, 248);
        c.StrokeRect(x, y, w, h, 2, White);
        c.FillRect(x + 2, y + 2, w - 4, 28, White);
        c.DrawText(font, m.Text(Str.ReportHeading), x + 14, y + 8, Dark, 1, 2);
        var lines = m.ReportLines;
        if (lines.Count == 0) c.DrawText(font, m.Text(Str.ReportEmpty), x + 24, y + 52, Green);
        for (int i = 0; i < lines.Count && i < 12; i++)
            c.DrawText(font, Ellipsize(font, lines[i], w - 48), x + 24, y + 48 + i * 24, i < 11 || lines.Count == 12 ? White : Purple3);
        if (lines.Count > 12) c.DrawText(font, $"... (+{lines.Count - 11})", x + 24, y + 48 + 11 * 24, Purple3);
        c.DrawText(font, m.Text(Str.ReportClose) + "  [X]", x + 24, y + h - 36, Cyan);
    }
}
