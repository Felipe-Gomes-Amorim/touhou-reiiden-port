using System.Globalization;

namespace Th01.Core.Graphics;

/// <summary>
/// Bitmap font loaded from a BDF file. Glyph boxes are placed in a fixed 16-row cell using the font's
/// bounding box, so any 8x16 or 16x16 BDF works. The bundled font is Spleen 8x16 (BSD-2, see Fonts/LICENSE-Spleen.txt).
/// </summary>
public sealed class BdfFont : IFontSource
{
    readonly Dictionary<int, Glyph> _glyphs = new();

    public int GlyphCount => _glyphs.Count;

    public bool TryGetGlyph(int codePoint, out Glyph glyph) => _glyphs.TryGetValue(codePoint, out glyph);

    /// <summary>The Spleen 8x16 font embedded in this assembly (ASCII/Latin and more; no Japanese).</summary>
    public static BdfFont LoadSpleen8x16()
    {
        using var s = typeof(BdfFont).Assembly.GetManifestResourceStream("Th01.Core.Fonts.spleen-8x16.bdf")
            ?? throw new InvalidOperationException("Embedded Spleen font resource is missing.");
        return Parse(new StreamReader(s));
    }

    /// <summary>The Shinonome 16 dot font embedded in this assembly (JIS X 0208: kana, kanji, symbols; Unicode-encoded, 16 px cells).</summary>
    public static BdfFont LoadShinonome16()
    {
        using var s = typeof(BdfFont).Assembly.GetManifestResourceStream("Th01.Core.Fonts.shinonome-16.bdf")
            ?? throw new InvalidOperationException("Embedded Shinonome font resource is missing.");
        return Parse(new StreamReader(s));
    }

    public static BdfFont Parse(TextReader reader)
    {
        var font = new BdfFont();
        int fbbW = 8, fbbH = Glyph.Height, fbbX = 0, fbbY = -4;
        int encoding = -1, bbxW = 0, bbxH = 0, bbxX = 0, bbxY = 0;
        List<byte[]>? rows = null;

        string? line;
        while ((line = reader.ReadLine()) is not null)
        {
            if (rows is not null && line != "ENDCHAR")
            {
                if (line == "BITMAP") continue;
                rows.Add(Convert.FromHexString(line.Trim()));
                continue;
            }
            var p = line.Split(' ', StringSplitOptions.RemoveEmptyEntries);
            if (p.Length == 0) continue;
            switch (p[0])
            {
                case "FONTBOUNDINGBOX":
                    fbbW = Int(p[1]); fbbH = Int(p[2]); fbbX = Int(p[3]); fbbY = Int(p[4]);
                    if (fbbH != Glyph.Height || (fbbW != 8 && fbbW != 16))
                        throw new FormatException($"Unsupported font size {fbbW}x{fbbH}; need 8x16 or 16x16.");
                    break;
                case "STARTCHAR": encoding = -1; break;
                case "ENCODING": encoding = Int(p[1]); break;
                case "BBX": bbxW = Int(p[1]); bbxH = Int(p[2]); bbxX = Int(p[3]); bbxY = Int(p[4]); break;
                case "BITMAP": rows = new List<byte[]>(); break;
                case "ENDCHAR":
                    if (rows is not null && encoding >= 0) font._glyphs[encoding] = Build(rows, fbbW, fbbH, fbbX, fbbY, bbxW, bbxH, bbxX, bbxY);
                    rows = null;
                    break;
            }
        }
        return font;
    }

    static int Int(string s) => int.Parse(s, CultureInfo.InvariantCulture);

    static Glyph Build(List<byte[]> src, int cellW, int cellH, int fbbX, int fbbY, int w, int h, int x, int y)
    {
        int bytesPerRow = cellW / 8;
        var cell = new byte[cellH * bytesPerRow];
        // Row 0 of the glyph box sits at (top of cell) + (cell ascent - (y + h)); ascent = fbbH + fbbY.
        int top = (cellH + fbbY) - (y + h);
        for (int r = 0; r < h && r < src.Count; r++)
        {
            int dr = top + r;
            if ((uint)dr >= (uint)cellH) continue;
            for (int c = 0; c < w; c++)
            {
                if ((src[r][c / 8] & (0x80 >> (c & 7))) == 0) continue;
                int dc = (x - fbbX) + c;
                if ((uint)dc >= (uint)cellW) continue;
                cell[dr * bytesPerRow + dc / 8] |= (byte)(0x80 >> (dc & 7));
            }
        }
        return new Glyph(cellW, cell);
    }
}
