using Th01.Assets.Model;

namespace Th01.Assets.Formats;

/// <summary>
/// .GRZ (up to 16 GRX images) and .GRX: 640x400, command stream + planar data stream(s).
/// Every GRX becomes one <see cref="SpriteSheet"/> whose frames are its planar streams.
/// Skipped 8-pixel units are transparent (Alpha 0). ref: th01/formats/grz.cpp
/// </summary>
public static class GrzImage
{
    public const int Width = 640, Height = 400, PlaneSize = 80 * 400;
    private const int GrzHeaderSize = 4 + 1 + 3 + 16 * 4 + 4 + 20, GrxHeaderSize = 64;

    public static IReadOnlyList<SpriteSheet> LoadGrz(ReadOnlySpan<byte> data)
    {
        if (data.Length < GrzHeaderSize || !data[..4].SequenceEqual("HGRZ"u8))
            throw new InvalidDataException("Not a GRZ file (missing HGRZ header).");
        int count = data[4];
        if (count > 16) throw new InvalidDataException("GRZ image count above 16.");
        var list = new List<SpriteSheet>(count);
        for (int i = 0; i < count; i++)
        {
            int offset = BitConverter.ToInt32(data.Slice(8 + i * 4, 4));
            if (offset < 0 || offset > data.Length) throw new InvalidDataException($"GRZ image {i} offset is outside the file.");
            list.Add(LoadGrx(data[offset..]));
        }
        return list;
    }

    /// <summary>Loads one GRX image from the start of <paramref name="data"/>; bytes after it are ignored.</summary>
    public static SpriteSheet LoadGrx(ReadOnlySpan<byte> data)
    {
        if (data.Length < GrxHeaderSize || !data[..4].SequenceEqual("HGRX"u8))
            throw new InvalidDataException("Not a GRX image (missing HGRX header).");
        int streams = data[4];
        int rleSize = BitConverter.ToUInt16(data.Slice(8, 2)), planarSize = BitConverter.ToUInt16(data.Slice(10, 2));
        if (streams < 1) throw new InvalidDataException("GRX has no planar stream.");
        if (data.Length < GrxHeaderSize + rleSize + streams * planarSize)
            throw new InvalidDataException("GRX is shorter than its header requires.");

        var pal = Planar.ReadPalette(data.Slice(16, 48));
        var rle = data.Slice(GrxHeaderSize, rleSize);
        var frames = new List<IndexedImageA>(streams);
        for (int s = 0; s < streams; s++)
        {
            var planar = data.Slice(GrxHeaderSize + rleSize + s * planarSize, planarSize).ToArray();
            frames.Add(Render(rle, planar, pal));
        }
        return new SpriteSheet(frames);
    }

    // Each step covers one byte position (8 pixels) of every plane. Command 1 = run (length, then
    // a command byte: 0 = put from the planar stream, else skip), followed by one more unit with the
    // same command; command 0 = put one unit; anything else = skip one unit.
    private static IndexedImageA Render(ReadOnlySpan<byte> rle, byte[] planar, Palette4 pal)
    {
        byte[] b = new byte[PlaneSize], r = new byte[PlaneSize], g = new byte[PlaneSize], e = new byte[PlaneSize], mask = new byte[PlaneSize];
        int rp = 0, pp = 0;

        void Put(int pos)
        {
            if (pp + 4 > planar.Length) throw new InvalidDataException("GRX planar stream ends early.");
            b[pos] = planar[pp++]; r[pos] = planar[pp++]; g[pos] = planar[pp++]; e[pos] = planar[pp++];
            mask[pos] = 0xFF;
        }

        for (int pos = 0; pos < PlaneSize; pos++)
        {
            if (rp >= rle.Length) break; // command stream ended: the rest stays transparent
            int command = rle[rp++];
            if (command == 1)
            {
                if (rp >= rle.Length) break;
                int runs = rle[rp++];
                if (runs != 0)
                {
                    if (rp >= rle.Length) break;
                    command = rle[rp++];
                    for (; runs > 0 && pos < PlaneSize; runs--, pos++)
                        if (command == 0) Put(pos);
                    if (pos >= PlaneSize) break;
                }
            }
            if (command == 0) Put(pos);
        }

        byte[] px = Planar.ToIndices(b, r, g, e, 80, 400);
        byte[] alpha = Planar.ToBits(mask, 80, 400);
        return new IndexedImageA(Width, Height, px, alpha, pal);
    }
}

/// <summary>
/// .GRF (MASK.GRF): one RLE stream per plane. Only the first plane is read, as the original does;
/// the result is monochrome (Pixels 0/1, Alpha equal to the bits). ref: th01/formats/grf.hpp, th01/main/hud/hud.cpp
/// </summary>
public static class GrfImage
{
    private const int HeaderSize = 64;

    /// <param name="widthBytes">Row size in bytes; the original hardcodes 80 (640 pixels).</param>
    /// <param name="height">Rows to decode; the HUD mask hardcodes 66.</param>
    public static IndexedImageA LoadFirstPlane(ReadOnlySpan<byte> data, int widthBytes = 80, int height = 66)
    {
        if (data.Length < HeaderSize || !data[..4].SequenceEqual("HGRF"u8))
            throw new InvalidDataException("Not a GRF file (missing HGRF header).");
        byte runByte = data[5];
        // The per-plane size fields are space-filled (0x2020) in the real MASK.GRF, so they are ignored:
        // the stream is simply decoded until the expected number of bytes is produced.
        var rle = data[HeaderSize..];

        int total = widthBytes * height;
        byte[] plane = new byte[total];
        int rp = 0;
        for (int pos = 0; pos < total; pos++)
        {
            if (rp >= rle.Length) break; // stream ended: remaining bytes are 0
            byte value = rle[rp++];
            if (value == runByte)
            {
                if (rp >= rle.Length) break;
                int runs = rle[rp++];
                if (runs != 0)
                {
                    if (rp >= rle.Length) break;
                    value = rle[rp++];
                    for (; runs > 0 && pos < total; runs--, pos++) plane[pos] = value;
                    if (pos >= total) break;
                }
            }
            plane[pos] = value; // also covers a run byte with 0 runs, which is emitted literally
        }
        byte[] bits = Planar.ToBits(plane, widthBytes, height);
        return new IndexedImageA(widthBytes * 8, height, bits, (byte[])bits.Clone());
    }
}
