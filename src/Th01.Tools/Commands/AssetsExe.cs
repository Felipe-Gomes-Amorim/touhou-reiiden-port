using Th01.Assets.Config;

namespace Th01.Tools.Commands;

/// <summary>
/// inspect-exe: finds the monochrome sprite tables that the original build embedded in the executables,
/// by searching for the bytes of the reference 1-bit BMPs. The BMPs are read from a local folder
/// (th01/sprites of the ReC98 clone); nothing from them is stored in this repo and only offsets/sizes are printed.
/// </summary>
public static class AssetsExe
{
    // Cel sizes from the original build rules (name, cel width, cel height).
    private static readonly (string File, int W, int H)[] Sprites =
    [
        ("leaf_s", 8, 8), ("leaf_l", 8, 8), ("leaf_r", 8, 8), ("ileave_m", 8, 8), ("laser_s", 16, 8),
        ("mousecur", 16, 16), ("pellet", 8, 8), ("pellet_c", 16, 16), ("pillar", 32, 8), ("shape8x8", 8, 8), ("bonusbox", 8, 4),
    ];

    public static int Run(string[] args)
    {
        // inspect-exe <exe> <bmp-folder>
        if (args.Length < 3) { Console.Error.WriteLine("Usage: Th01.Tools inspect-exe <file.exe> <folder with the reference .bmp files>"); return 2; }
        byte[] exe = File.ReadAllBytes(args[1]);
        Console.WriteLine($"{args[1]}: {exe.Length:N0} bytes");
        foreach (var (file, w, h) in Sprites)
        {
            string path = Path.Combine(args[2], file + ".bmp");
            if (!File.Exists(path)) { Console.WriteLine($"  {file,-10} (no reference bmp)"); continue; }
            var (bw, bh, rows) = ReadMono(File.ReadAllBytes(path));
            var cels = Cels(rows, bw, bh, w, h);
            bool found = false;
            foreach (bool invert in new[] { false, true })
            {
                byte[] needle = cels.SelectMany(c => c).Select(b => (byte)(invert ? ~b : b)).ToArray();
                var hits = FindAll(exe, needle);
                if (hits.Count > 0)
                {
                    found = true;
                    Console.WriteLine($"  {file,-10} {w}x{h} x{cels.Count} cel(s), {needle.Length} bytes{(invert ? ", inverted bits" : "")}: " +
                                      string.Join(", ", hits.Take(4).Select(o => $"0x{o:X}")) + (hits.Count > 4 ? ", ..." : ""));
                }
            }
            if (!found)
            {
                // Fall back to single cels (e.g. when the original arranges or preshifts them differently).
                var perCel = cels.Select((c, i) => (i, hits: FindAll(exe, c).Count + FindAll(exe, c.Select(b => (byte)~b).ToArray()).Count)).ToList();
                Console.WriteLine($"  {file,-10} {w}x{h} x{cels.Count} cel(s): whole table not found; single-cel matches: " +
                                  string.Join(" ", perCel.Select(x => $"#{x.i}={x.hits}")));
            }
        }
        return 0;
    }

    // Returns image rows top to bottom, 1 bit per pixel, row-padded to whole bytes (width must be a multiple of 8).
    private static (int W, int H, byte[][] Rows) ReadMono(byte[] bmp)
    {
        if (bmp.Length < 62 || bmp[0] != 'B' || bmp[1] != 'M') throw new InvalidDataException("Not a BMP file.");
        int offset = BitConverter.ToInt32(bmp, 10), w = BitConverter.ToInt32(bmp, 18), h = BitConverter.ToInt32(bmp, 22);
        if (BitConverter.ToUInt16(bmp, 28) != 1) throw new InvalidDataException("Only 1-bit BMPs are supported.");
        int stride = (((w + 31) / 32) * 4), rowBytes = (w + 7) / 8;
        var rows = new byte[Math.Abs(h)][];
        for (int y = 0; y < rows.Length; y++)
        {
            int srcRow = h > 0 ? rows.Length - 1 - y : y; // bottom-up unless the height is negative
            rows[y] = bmp.AsSpan(offset + srcRow * stride, rowBytes).ToArray();
        }
        return (w, rows.Length, rows);
    }

    private static List<byte[]> Cels(byte[][] rows, int bw, int bh, int cw, int ch)
    {
        var list = new List<byte[]>();
        int cb = cw / 8;
        for (int cy = 0; cy + ch <= bh; cy += ch)
            for (int cx = 0; cx + cw <= bw; cx += cw)
            {
                var cel = new byte[cb * ch];
                for (int y = 0; y < ch; y++)
                    for (int b = 0; b < cb; b++) cel[y * cb + b] = rows[cy + y][cx / 8 + b];
                list.Add(cel);
            }
        return list;
    }

    private static List<int> FindAll(byte[] hay, byte[] needle)
    {
        var hits = new List<int>();
        for (int i = hay.AsSpan().IndexOf(needle); i >= 0;)
        {
            hits.Add(i);
            int next = hay.AsSpan(i + 1).IndexOf(needle);
            i = next < 0 ? -1 : i + 1 + next;
        }
        return hits;
    }
}
