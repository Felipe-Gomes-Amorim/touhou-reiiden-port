using Th01.Assets;
using Th01.Assets.Config;
using Th01.Assets.Formats;
using Th01.Assets.Model;

namespace Th01.Tools.Commands;

/// <summary>
/// contact-sheets &lt;out-dir&gt;: renders PNGs for a human to check colours and frame order.
/// The output contains game graphics, so it must stay outside the repo (game-data/out/ is gitignored).
/// </summary>
public static class AssetsSheets
{
    private const int Columns = 8;

    private sealed class Canvas
    {
        public readonly int Width, Height;
        public readonly byte[] Rgba;

        public Canvas(int w, int h, (byte r, byte g, byte b) bg)
        {
            Width = w; Height = h; Rgba = new byte[w * h * 4];
            for (int i = 0; i < w * h; i++) { Rgba[i * 4] = bg.r; Rgba[i * 4 + 1] = bg.g; Rgba[i * 4 + 2] = bg.b; Rgba[i * 4 + 3] = 255; }
        }

        /// <summary>Draws an image at integer scale; transparent pixels are skipped.</summary>
        public void Draw(IndexedImage img, Palette4? fallback, int x0, int y0, int scale)
        {
            byte[] px = ImageConvert.ToRgba(img, fallback);
            for (int y = 0; y < img.Height; y++)
                for (int x = 0; x < img.Width; x++)
                {
                    int s = (y * img.Width + x) * 4;
                    if (px[s + 3] == 0) continue;
                    for (int dy = 0; dy < scale; dy++)
                        for (int dx = 0; dx < scale; dx++)
                        {
                            int X = x0 + x * scale + dx, Y = y0 + y * scale + dy;
                            if ((uint)X >= (uint)Width || (uint)Y >= (uint)Height) continue;
                            Array.Copy(px, s, Rgba, (Y * Width + X) * 4, 4);
                        }
                }
        }

        /// <summary>Draws every second pixel in both directions (a 2:1 thumbnail).</summary>
        public void DrawHalf(IndexedImage img, int x0, int y0)
        {
            byte[] px = ImageConvert.ToRgba(img);
            for (int y = 0; y < img.Height / 2; y++)
                for (int x = 0; x < img.Width / 2; x++)
                    Array.Copy(px, ((y * 2) * img.Width + x * 2) * 4, Rgba, ((y0 + y) * Width + x0 + x) * 4, 4);
        }
    }

    private static Palette4 White()
    {
        var p = new Palette4();
        p.Set(1, 15, 15, 15);
        return p;
    }

    public static int Run(string[] args)
    {
        string? folder = GameDataConfig.Resolve();
        if (args.Length < 2 || folder is null || !Directory.Exists(folder))
        {
            Console.Error.WriteLine("Usage: Th01.Tools contact-sheets <out-dir>   (game data must be configured)");
            return 2;
        }
        string outDir = args[1];
        Directory.CreateDirectory(outDir);
        var assets = new GameAssets(new FolderAssetSource(folder));

        foreach (string name in new[] { "STG.PTN", "STG_B.PTN", "MIKO.PTN", "NUMB.PTN", "BOSS3_M.PTN", "TAMAYEN.PTN" })
            Save(outDir, $"ptn_{Path.GetFileNameWithoutExtension(name)}.png", Sheet(assets.GetPtn(name), null, 3));

        Save(outDir, "grc_KUZI.png", Join(Sheet(assets.GetGrc("KUZI1.GRC"), White(), 3), Sheet(assets.GetGrc("KUZI2.GRC"), White(), 3)));
        Save(outDir, "bos_MIKO_AC.png", Sheet(assets.GetBos("MIKO_AC.BOS"), null, 2));
        Save(outDir, "bos_MIKO_AC2.png", Sheet(assets.GetBos("MIKO_AC2.BOS"), null, 2));
        Save(outDir, "bos_BOSS1.png", Sheet(assets.GetBos("BOSS1.BOS"), null, 1));

        var emb = assets.GetEmbeddedSprites();
        foreach (string n in emb.Found)
            Save(outDir, $"embedded_{n}.png", Sheet(emb.Get(n), White(), 6));

        for (int scene = 0; scene <= 7; scene++)
            Save(outDir, $"stages_scene{scene}.png", Stages(assets, scene));

        Save(outDir, "grp_stages.png", Thumbs(assets, Enumerable.Range(0, 7).Select(i => $"ST{i}.GRP").ToList()));
        Save(outDir, "grp_bosses.png", Thumbs(assets, ["BOSS1.GRP", "BOSS2.GRP", "BOSS3.GRP", "BOSS4.GRP", "BOSS5.GRP", "BOSS6_A.GRP", "BOSS7_D1.GRP", "BOSS8_A1.GRP"]));
        Save(outDir, "grp_screens.png", Thumbs(assets, ["OP_WIN.GRP", "REIIDEN2.GRP", "REIIDEN3.GRP", "GAME_O.GRP", "CLEAR3.GRP", "ED1A.GRP", "ED5C.GRP", "ENDB_A.GRP"]));

        var mask = assets.GetHudMask();
        var c = new Canvas(mask.Width, mask.Height, (96, 0, 96));
        c.Draw(mask, White(), 0, 0, 1);
        Save(outDir, "hud_mask.png", c);

        var grz = assets.GetGrz("BOSS8.GRZ");
        var g = new Canvas(640, 400 * 2, (96, 0, 96));
        g.DrawHalf(Flatten(grz[0].Frames[0]), 0, 0);
        Save(outDir, "grz_BOSS8_image0.png", g);

        Screens(assets, outDir, folder);

        Console.WriteLine($"Wrote contact sheets to {Path.GetFullPath(outDir)}");
        return 0;
    }

    private static readonly string[] ScreenFiles =
    [
        "REIIDEN2.GRP", "REIIDEN3.GRP", "OP_WIN.GRP", "GAME_O.GRP", "CLEAR3.GRP", "ENDM_A.GRP",
        "ED1A.GRP", "ED1B.GRP", "ED1C.GRP", "ED1D.GRP", "ED1E.GRP", "ED2A.GRP", "ED3A.GRP", "ED3B.GRP", "ED4A.GRP",
        "ED5A.GRP", "ED5B.GRP", "ED5C.GRP", "ENDB_A.GRP", "ENDB_B.GRP",
    ];

    private static string PaletteKey(Palette4 p) => string.Join("", Enumerable.Range(0, 16).Select(i => { var (r, g, b) = p[i]; return $"{r:X}{g:X}{b:X}"; }));

    /// <summary>Per-screen sheets: each image at full size (cutscene images are 4 quadrants of 320x200) plus the title composite.</summary>
    private static void Screens(GameAssets assets, string outDir, string folder)
    {
        var groups = new Dictionary<string, List<string>>();
        foreach (string f in ScreenFiles)
        {
            if (!File.Exists(Path.Combine(folder, f))) continue;
            var img = assets.GetGrp(f);
            var c = new Canvas(640, 400, (96, 0, 96));
            c.Draw(img, null, 0, 0, 1);
            Save(outDir, $"screen_{Path.GetFileNameWithoutExtension(f)}.png", c);
            string key = PaletteKey(img.Palette!);
            if (!groups.TryGetValue(key, out var list)) groups[key] = list = [];
            list.Add(f);
        }
        Console.WriteLine("Palette groups (files that carry the same palette):");
        foreach (var g in groups.Values) Console.WriteLine("  " + string.Join(", ", g));

        // Title: REIIDEN2 is shown first with its own palette; REIIDEN3 is drawn without changing the palette;
        // OP_WIN is drawn over it with colour 15 transparent.
        var bg = assets.GetGrp("REIIDEN2.GRP");
        var fg = assets.GetGrp("REIIDEN3.GRP");
        var win = assets.GetGrp("OP_WIN.GRP");
        var title = new Canvas(640, 400, (0, 0, 0));
        title.Draw(new IndexedImage(640, 400, fg.Pixels, bg.Palette), null, 0, 0, 1);
        title.Draw(new IndexedImageA(640, 400, win.Pixels, win.Pixels.Select(p => (byte)(p == 15 ? 0 : 1)).ToArray(), bg.Palette), null, 0, 0, 1);
        Save(outDir, "screen_title_composite.png", title);
    }

    private static IndexedImage Flatten(IndexedImageA a) => new(a.Width, a.Height, a.Pixels, a.Palette);

    private static void Save(string dir, string file, Canvas c)
    {
        PngWriter.Write(Path.Combine(dir, file), c.Width, c.Height, c.Rgba);
        Console.WriteLine($"  {file} ({c.Width}x{c.Height})");
    }

    /// <summary>Frames in reading order, 8 per row, so frame N sits at row N/8, column N%8.</summary>
    private static Canvas Sheet(SpriteSheet sheet, Palette4? fallback, int scale)
    {
        int n = sheet.Frames.Count, cols = Math.Min(Columns, n), rows = (n + cols - 1) / cols;
        int cw = sheet.FrameWidth * scale + 4, ch = sheet.FrameHeight * scale + 4;
        var c = new Canvas(cols * cw + 4, rows * ch + 4, (64, 0, 64));
        for (int i = 0; i < n; i++)
            c.Draw(sheet.Frames[i], fallback, 4 + (i % cols) * cw, 4 + (i / cols) * ch, scale);
        return c;
    }

    private static Canvas Join(Canvas a, Canvas b)
    {
        var c = new Canvas(Math.Max(a.Width, b.Width), a.Height + b.Height, (64, 0, 64));
        Array.Copy(a.Rgba, c.Rgba, a.Rgba.Length);
        Array.Copy(b.Rgba, 0, c.Rgba, a.Rgba.Length, b.Rgba.Length);
        return c;
    }

    private static Canvas Thumbs(GameAssets assets, List<string> names)
    {
        var c = new Canvas(4 * 324 + 4, ((names.Count + 3) / 4) * 204 + 4, (64, 0, 64));
        for (int i = 0; i < names.Count; i++)
        {
            try { c.DrawHalf(assets.GetGrp(names[i]), 4 + (i % 4) * 324, 4 + (i / 4) * 204); }
            catch (FileNotFoundException) { /* leave the cell empty */ }
        }
        return c;
    }

    // Placeholder mapping from stage data to STG.PTN frames, only for eyeballing the layouts (see docs/FORMATS.md).
    private static int FrameFor(bool card, byte obstacle) => obstacle switch
    {
        1 => 22,
        >= 2 and <= 7 => 23,
        >= 8 and <= 13 => 24,
        14 => 11,
        15 => 8,
        16 => 8,
        17 => 29,
        18 => 25,
        19 => 27,
        20 => 26,
        21 => 28,
        _ => card ? 14 : -1,
    };

    private static Canvas Stages(GameAssets assets, int scene)
    {
        var stg = assets.GetPtn("STG.PTN");
        var layouts = assets.GetScene(scene);
        var c = new Canvas(640, layouts.Count * 324, (0, 0, 0));
        for (int s = 0; s < layouts.Count; s++)
            for (int row = 0; row < StageLayout.Rows; row++)
                for (int col = 0; col < StageLayout.Columns; col++)
                {
                    bool card = layouts[s].Cards[row, col];
                    byte ob = layouts[s].Obstacles[row, col];
                    int f = FrameFor(card, ob);
                    if (f >= 0) c.Draw(stg.Frames[f], null, col * 32, s * 324 + row * 32, 1);
                }
        return c;
    }
}
