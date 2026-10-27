using Th01.Core.Graphics;
using static Th01.Platform.MonoGame.Launcher.LauncherColors;

namespace Th01.Platform.MonoGame.Launcher;

/// <summary>Draws the launcher screens onto a <see cref="LauncherCanvas"/> following the design in docs/launcher-ref (1280x720).</summary>
public static class LauncherView
{
    public static readonly string[] TabKanji = ["視", "映", "音", "字", "操", "遊", "録", "診"];
    static readonly Str[] TabNames = [Str.TabAccessibility, Str.TabVideo, Str.TabAudio, Str.TabFont, Str.TabControls, Str.TabGame, Str.TabData, Str.TabDiagnostics];

    public static void Render(LauncherModel m, LauncherCanvas c, IFontSource font)
    {
        switch (m.Screen)
        {
            case LauncherScreen.Home: RenderHome(m, c, font); break;
            case LauncherScreen.Options: RenderOptions(m, c, font); break;
            case LauncherScreen.About: RenderAbout(m, c, font); break;
        }
        c.Scanlines();
    }

    // ---- Home / first run ----------------------------------------------------------------------------------------------

    static void RenderHome(LauncherModel m, LauncherCanvas c, IFontSource font)
    {
        c.CopyFrom(m.FirstRun ? LauncherBackdrop.Base(80, 42, Yellow, 26, true) : LauncherBackdrop.Base(80, 42, Red, 41, true));
        LauncherBackdrop.DrawBands(c, m.Tick, 96);

        c.DrawTextShadow(font, m.Text(Str.Title), 72, 56, White, Red, 3, 3, 1);
        c.DrawText(font, m.Text(Str.Subtitle), 72 + 56, 56 + 48 + 14, Cyan);
        DrawDither(c, DitherKind.Half, 72, 56 + 48 + 14 + 16 + 22, 520, 8);

        DrawMenu(m, c, font);
        DrawHomeFooter(m, c, font);
    }

    const int MenuX = 96, MenuY = 270, MenuItemH = 56, MenuGap = 14;
    static readonly HomeItem[] MenuItems = [HomeItem.Play, HomeItem.Options, HomeItem.Directory, HomeItem.Quit];

    static void DrawMenu(LauncherModel m, LauncherCanvas c, IFontSource font)
    {
        // Dotted red diagonal with a square at each item's step.
        double x0 = MenuX + 25, y0 = MenuY + 28, x1 = MenuX + 193, y1 = MenuY + 238;
        double len = Math.Sqrt((x1 - x0) * (x1 - x0) + (y1 - y0) * (y1 - y0));
        for (double t = 0; t < len; t++)
            if ((int)t % 12 < 6) c.FillRect((int)Math.Round(x0 + (x1 - x0) * t / len), (int)Math.Round(y0 + (y1 - y0) * t / len), 2, 2, Red, 178);
        int[] sq = [22, 78, 134, 190], sqy = [25, 95, 165, 235];
        for (int i = 0; i < 4; i++)
            c.FillRect(MenuX + sq[i], MenuY + sqy[i], 6, 6, i == 0 && !m.IsEnabled(HomeItem.Play) ? Star : White);

        Str[] names = [Str.MenuPlay, Str.MenuOptions, Str.MenuDirectory, Str.MenuQuit];
        for (int i = 0; i < 4; i++)
        {
            var item = MenuItems[i];
            bool sel = m.FocusedItem == item, enabled = m.IsEnabled(item);
            string text = m.Text(names[i]);
            int x = MenuX + 56 * i, y = MenuY + i * (MenuItemH + MenuGap);
            int w = Math.Max(264, 14 + 26 + 14 + LauncherCanvas.MeasureText(font, text, 2, 3) + 24);
            if (sel && enabled)
            {
                x += 8;
                c.FillRect(x + 5, y + 5, w, MenuItemH, Red);
                c.FillRect(x, y, w, MenuItemH, White);
                c.YinYang(x + 14 + 13, y + MenuItemH / 2, 12, 1);
                c.DrawText(font, text, x + 54, y + 12, Dark, 2, 3);
            }
            else if (!enabled)
            {
                c.DashedRect(x, y, w, MenuItemH, 2, Purple2);
                DrawLock(c, x + 14 + 2, y + MenuItemH / 2 - 13);
                c.DrawText(font, text, x + 54, y + 12, Disabled, 2, 3);
            }
            else c.DrawText(font, text, x + 54, y + 12, White, 2, 3);
        }
    }

    static void DrawLock(LauncherCanvas c, int x, int y)
    {
        c.FillRect(x + 3, y + 3, 3, 10, Disabled);      // shackle
        c.FillRect(x + 14, y + 3, 3, 10, Disabled);
        c.FillRect(x + 3, y, 14, 3, Disabled);
        c.FillRect(x, y + 12, 20, 14, Disabled);        // body
        c.FillRect(x + 8, y + 16, 4, 6, Dark);          // keyhole
    }

    static void DrawHomeFooter(LauncherModel m, LauncherCanvas c, IFontSource font)
    {
        DrawFooterFrame(c, 96);
        int y = 624 + 12;
        string badge = m.Text(Str.Notice);
        int bw = LauncherCanvas.MeasureText(font, badge) + 20;
        c.FillRect(72, y, bw, 20, Yellow);
        c.DrawText(font, badge, 82, y + 2, Dark);
        c.DrawText(font, m.Text(Str.Disclaimer), 72 + bw + 14, y + 2, Yellow);

        int y2 = 624 + 12 + 20 + 12;
        int x = 72;
        x = DrawHint(c, font, "↑↓", false, m.Text(Str.HintNavigate), x, y2) + 26;
        DrawHint(c, font, "Z", true, m.Text(Str.HintSelect), x, y2);

        // Right side, from the right edge: About link, language switch, version.
        string about = m.Text(Str.AboutLink);
        int aw = LauncherCanvas.MeasureText(font, about) + 4;
        int ax = 1208 - aw;
        bool aboutFocus = m.FocusedItem == HomeItem.About;
        DrawLink(c, font, about, ax, y2 + 2, aboutFocus);

        string[] parts = ["EN", "/", "日本語"];
        int sw = parts.Sum(p => LauncherCanvas.MeasureText(font, p) + 12) + 2 * 6;
        int sx = ax - 20 - sw;
        DrawLanguageSwitch(m, c, font, sx, y2 + 2, parts);

        string ver = "v" + m.Version;
        c.DrawText(font, ver, sx - 20 - LauncherCanvas.MeasureText(font, ver), y2 + 2, Purple3);
    }

    static void DrawLanguageSwitch(LauncherModel m, LauncherCanvas c, IFontSource font, int x, int y, string[] parts)
    {
        bool focus = m.FocusedItem == HomeItem.Language;
        int total = parts.Sum(p => LauncherCanvas.MeasureText(font, p) + 12) + 12;
        if (focus) c.StrokeRect(x - 6, y - 6, total + 12, 16 + 12, 3, Cyan);
        int cx = x;
        for (int i = 0; i < parts.Length; i++)
        {
            int w = LauncherCanvas.MeasureText(font, parts[i]);
            bool active = i == 0 ? m.Language == LauncherLanguage.English : i == 2 && m.Language == LauncherLanguage.Japanese;
            if (active) c.FillRect(cx, y - 2, w + 12, 20, Cyan);
            c.DrawText(font, parts[i], cx + 6, y, active ? Dark : Purple3);
            cx += w + 12 + 6;
        }
    }

    static void DrawLink(LauncherCanvas c, IFontSource font, string text, int x, int y, bool focus)
    {
        int w = LauncherCanvas.MeasureText(font, text);
        if (focus)
        {
            c.StrokeRect(x - 8, y - 8, w + 4 + 16, 16 + 4 + 16, 3, Cyan);
            c.FillRect(x - 2, y - 2, w + 8, 22, Cyan);
        }
        c.DrawText(font, text, x + 2, y, focus ? Dark : Cyan);
        c.FillRect(x - 2, y + 18, w + 8, 2, Cyan);
    }

    static void DrawFooterFrame(LauncherCanvas c, int height)
    {
        int top = LauncherCanvas.Height - height;
        c.FillRect(0, top, LauncherCanvas.Width, height, Dark, 235);
        c.FillRect(0, top, LauncherCanvas.Width, 2, Purple2);
    }

    // ---- Options -------------------------------------------------------------------------------------------------------

    static void RenderOptions(LauncherModel m, LauncherCanvas c, IFontSource font)
    {
        c.CopyFrom(LauncherBackdrop.Base(85, 50, Red, 31, false));
        LauncherBackdrop.DrawBands(c, m.Tick, 72);

        c.DrawText(font, m.Text(Str.Title), 72, 30, Purple3);
        c.DrawTextShadow(font, m.Text(Str.OptionsHeading), 72, 50, White, Red, 3, 2, 4);

        DrawKeyCap(c, font, "L", 30, 142, 30, true);
        DrawKeyCap(c, font, "R", 1220, 142, 30, true);

        const int tabW = 135, tabH = 84;
        for (int i = 0; i < 8; i++)
        {
            int x = 72 + i * (tabW + 8), y = 112;
            bool act = i == (int)m.Tab;
            if (act)
            {
                c.FillRect(x + 4, y + 4, tabW, tabH, Red);
                c.FillRect(x, y, tabW, tabH, White);
            }
            else
            {
                c.FillRect(x, y, tabW, tabH, Panel, 217);
                c.StrokeRect(x, y, tabW, tabH, 2, Purple2);
            }
            string label = m.Text(TabNames[i]);
            int kw = LauncherCanvas.MeasureText(font, TabKanji[i], 2);
            c.DrawText(font, TabKanji[i], x + (tabW - kw) / 2, y + 12, act ? Dark : White, 2);
            int lw = LauncherCanvas.MeasureText(font, label);
            c.DrawText(font, label, x + (tabW - lw) / 2, y + 56, act ? Purple2 : Purple3);
        }

        string cat = m.Text(TabNames[(int)m.Tab]);
        if (m.Language == LauncherLanguage.English) cat = cat.ToUpperInvariant();
        int cw = c.DrawText(font, cat, 72, 224, Cyan, 2, 2);
        c.FillRect(cw + 12, 239, 1208 - cw - 12, 2, Purple2);

        DrawFooterFrame(c, 72);
        int fy = 648 + 28;
        int x2 = 72;
        x2 = DrawHint(c, font, "L", true, "", x2, fy - 8) + 4;
        x2 = DrawHint(c, font, "R", true, m.Text(Str.HintCategory), x2, fy - 8) + 26;
        x2 = DrawHint(c, font, "↑↓", false, m.Text(Str.HintNavigate), x2, fy - 8) + 26;
        DrawHint(c, font, "Z", true, m.Text(Str.HintChange), x2, fy - 8);
        DrawBackButton(c, font, m.Text(Str.HintBack));
    }

    static void DrawBackButton(LauncherCanvas c, IFontSource font, string label)
    {
        int w = 22 + 24 + 10 + LauncherCanvas.MeasureText(font, label) + 22;
        int x = 1208 - w, y = 648 + 15;
        c.FillRect(x + 4, y + 4, w, 42, Red);
        c.FillRect(x, y, w, 42, White);
        c.Circle(x + 22 + 12, y + 21, 12, Dark, 2);
        c.DrawText(font, "B", x + 22 + 12 - 4, y + 21 - 8, Dark);
        c.DrawText(font, label, x + 22 + 24 + 10, y + 13, Dark);
    }

    // ---- About ---------------------------------------------------------------------------------------------------------

    static void RenderAbout(LauncherModel m, LauncherCanvas c, IFontSource font)
    {
        c.CopyFrom(LauncherBackdrop.Base(80, 42, Red, 36, false));
        LauncherBackdrop.DrawBands(c, m.Tick, 72);

        c.DrawText(font, m.Text(Str.Title), 72, 44, Purple3);
        c.DrawTextShadow(font, m.Text(Str.AboutHeading), 72, 64, White, Red, 3, 2, 4);

        const int px = 72, py = 150, pw = 760, inner = 760 - 4 - 48;
        var note = Wrap(font, m.Text(Str.AboutNote), pw - 4 - 48 - 28 - 4);
        var basedOn = Wrap(font, m.Text(Str.AboutBasedOnText), inner);
        const int line = 24;
        int body = 22 + (20 + 2 * line) + 20 + (20 + basedOn.Count * line) + 20 + (20 + line) + 20 + (20 + line) + 20 + (24 + note.Count * 26) + 24;
        int ph = 30 + body;
        DrawPanelFrame(c, font, px, py, pw, ph, White, m.Text(Str.AboutProject), "v" + m.Version);

        int x = px + 2 + 24, y = py + 30 + 22;
        void Label(string s) { c.DrawText(font, s, x, y, Purple3, 1, 2); y += 20; }
        Label(m.Text(Str.AboutLauncher));
        c.DrawText(font, m.Text(Str.Title), x, y, White); y += line;
        c.DrawText(font, m.Text(Str.Subtitle), x, y, Cyan); y += line + 20;
        Label(m.Text(Str.AboutBasedOn));
        foreach (var l in basedOn) { c.DrawText(font, l, x, y, White); y += line; }
        y += 20;
        Label(m.Text(Str.AboutOriginal));
        c.DrawText(font, m.Text(Str.AboutOriginalText), x, y, White); y += line + 20;
        Label(m.Text(Str.AboutLink2));
        c.DrawText(font, LauncherStrings.ProjectUrl, x, y, Cyan); y += line + 20;
        int bh = 24 + note.Count * 26;
        c.StrokeRect(x, y, inner, bh, 2, Yellow);
        int ny = y + 12;
        foreach (var l in note) { c.DrawText(font, l, x + 14 + 2, ny + 4, Yellow); ny += 26; }

        DrawFooterFrame(c, 72);
        c.DrawText(font, m.Text(Str.Title) + " · v" + m.Version, 72, 648 + 28, Purple3);
        DrawBackButton(c, font, m.Text(Str.HintBack));
    }

    // ---- Shared pieces -------------------------------------------------------------------------------------------------

    public static void DrawPanelFrame(LauncherCanvas c, IFontSource font, int x, int y, int w, int h, uint headerColor, string left, string right)
    {
        c.FillRect(x + 6, y + 6, w, h, Purple1);
        c.FillRect(x, y, w, h, Panel, 235);
        c.StrokeRect(x, y, w, h, 2, White);
        c.FillRect(x + 2, y + 2, w - 4, 28, headerColor);
        c.DrawText(font, left, x + 14, y + 8, Dark, 1, 2);
        int rw = LauncherCanvas.MeasureText(font, right);
        c.DrawText(font, right, x + w - 14 - rw, y + 8, Dark);
    }

    public static void DrawDither(LauncherCanvas c, DitherKind kind, int x, int y, int w, int h)
    {
        for (int yy = 0; yy < h; yy++)
            for (int xx = 0; xx < w; xx++)
                if (LauncherBackdrop.Dither(kind, xx, yy)) c.Put(x + xx, y + yy, Purple1);
    }

    /// <summary>A key cap with a label after it; returns the x after the label.</summary>
    static int DrawHint(LauncherCanvas c, IFontSource font, string key, bool round, string label, int x, int y)
    {
        int kw = DrawKeyCap(c, font, key, x, y, 26, round);
        int end = x + kw;
        if (label.Length > 0)
        {
            end += 8;
            c.DrawText(font, label, end, y + 5, Purple3);
            end += LauncherCanvas.MeasureText(font, label);
        }
        return end;
    }

    /// <summary>Circle (round) or square key cap showing <paramref name="label"/>; returns its width.</summary>
    static int DrawKeyCap(LauncherCanvas c, IFontSource font, string label, int x, int y, int size, bool round)
    {
        int tw = LauncherCanvas.MeasureText(font, label);
        int w = round ? size : Math.Max(size, tw + 12);
        if (round) c.Circle(x + size / 2, y + size / 2, size / 2, White, 2);
        else c.StrokeRect(x, y, w, size, 2, White);
        c.DrawText(font, label, x + (w - tw) / 2, y + (size - 16) / 2, White);
        return w;
    }

    /// <summary>Greedy line wrapping by pixel width at scale 1: breaks at spaces, and between any two Japanese characters.</summary>
    public static List<string> Wrap(IFontSource font, string text, int maxWidth)
    {
        var lines = new List<string>();
        var cur = new System.Text.StringBuilder();
        int curW = 0;
        foreach (string token in Tokens(text))
        {
            int tw = LauncherCanvas.MeasureText(font, token);
            if (curW + tw > maxWidth && cur.Length > 0 && token != " ")
            {
                lines.Add(cur.ToString().TrimEnd());
                cur.Clear();
                curW = 0;
            }
            if (cur.Length == 0 && token == " ") continue;
            cur.Append(token);
            curW += tw;
        }
        if (cur.Length > 0) lines.Add(cur.ToString().TrimEnd());
        return lines;
    }

    static IEnumerable<string> Tokens(string text)
    {
        var word = new System.Text.StringBuilder();
        foreach (var rune in text.EnumerateRunes())
        {
            if (rune.Value == ' ') { if (word.Length > 0) { yield return word.ToString(); word.Clear(); } yield return " "; }
            else if (rune.Value >= 0x3000) { if (word.Length > 0) { yield return word.ToString(); word.Clear(); } yield return rune.ToString(); }
            else word.Append(rune.ToString());
        }
        if (word.Length > 0) yield return word.ToString();
    }
}
