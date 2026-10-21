using System.IO.Compression;

namespace Th01.Tools.Commands;

/// <summary>Minimal RGBA8 PNG encoder (debug output only).</summary>
internal static class PngWriter
{
    public static void Write(string path, int width, int height, byte[] rgba)
    {
        using var fs = File.Create(path);
        fs.Write([0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A]);
        byte[] ihdr = new byte[13];
        BigEndian(width, ihdr, 0); BigEndian(height, ihdr, 4);
        ihdr[8] = 8; ihdr[9] = 6;
        Chunk(fs, "IHDR", ihdr);
        var raw = new MemoryStream();
        using (var z = new ZLibStream(raw, CompressionLevel.Optimal, leaveOpen: true))
            for (int y = 0; y < height; y++)
            {
                z.WriteByte(0);
                z.Write(rgba, y * width * 4, width * 4);
            }
        Chunk(fs, "IDAT", raw.ToArray());
        Chunk(fs, "IEND", []);
    }

    private static void BigEndian(int v, byte[] b, int o) { b[o] = (byte)(v >> 24); b[o + 1] = (byte)(v >> 16); b[o + 2] = (byte)(v >> 8); b[o + 3] = (byte)v; }

    private static void Chunk(Stream s, string type, byte[] data)
    {
        byte[] len = new byte[4]; BigEndian(data.Length, len, 0);
        s.Write(len);
        byte[] t = System.Text.Encoding.ASCII.GetBytes(type);
        s.Write(t); s.Write(data);
        uint crc = 0xFFFFFFFF;
        foreach (byte b in t.Concat(data)) { crc ^= b; for (int k = 0; k < 8; k++) crc = (crc & 1) != 0 ? (crc >> 1) ^ 0xEDB88320 : crc >> 1; }
        byte[] c = new byte[4]; BigEndian((int)~crc, c, 0);
        s.Write(c);
    }
}
