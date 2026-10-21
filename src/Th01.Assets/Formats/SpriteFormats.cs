using Th01.Assets.Model;

namespace Th01.Assets.Formats;

/// <summary>.PTN: 32x32 16-colour sprites, colour #15 transparent. docs/FORMATS.md. ref: th01/formats/ptn.cpp</summary>
public static class PtnSprites
{
    public const int Size = 32;
    private const int HeaderSize = 6, PaletteSize = 48, PlaneBytes = 128, ImageSize = 1 + 4 * PlaneBytes;

    public static SpriteSheet Load(ReadOnlySpan<byte> data)
    {
        if (data.Length < HeaderSize + PaletteSize || !data[..4].SequenceEqual("HPTN"u8))
            throw new InvalidDataException("Not a PTN file (missing HPTN header).");
        int count = (sbyte)data[5];
        if (count <= 0) throw new InvalidDataException($"PTN image count {count} is invalid.");
        if (data.Length < HeaderSize + PaletteSize + count * ImageSize)
            throw new InvalidDataException("PTN file is shorter than its image count requires.");

        var pal = Planar.ReadPalette(data.Slice(HeaderSize, PaletteSize));
        var frames = new List<IndexedImageA>(count);
        for (int i = 0; i < count; i++)
        {
            // skip the unused zero byte that starts every image
            var img = data.Slice(HeaderSize + PaletteSize + i * ImageSize + 1, 4 * PlaneBytes);
            byte[] px = Planar.ToIndices(img[..PlaneBytes], img.Slice(PlaneBytes, PlaneBytes),
                img.Slice(2 * PlaneBytes, PlaneBytes), img.Slice(3 * PlaneBytes, PlaneBytes), 4, Size);
            byte[] alpha = new byte[px.Length];
            for (int p = 0; p < px.Length; p++) alpha[p] = (byte)(px[p] == 15 ? 0 : 1);
            frames.Add(new IndexedImageA(Size, Size, px, alpha, pal));
        }
        return new SpriteSheet(frames);
    }
}

/// <summary>.BOS: arbitrary-size sprites with 5 planes (alpha + BRGE). ref: th01/formats/bos.hpp</summary>
public static class BosSprites
{
    private const int DataStart = 64;

    public static SpriteSheet Load(ReadOnlySpan<byte> data)
    {
        if (data.Length < DataStart || !data[..4].SequenceEqual("BOSS"u8))
            throw new InvalidDataException("Not a BOS file (missing BOSS header).");
        int vramW = data[4], h = data[6], count = data[8];
        if (vramW == 0 || h == 0 || count == 0) throw new InvalidDataException("BOS header has a zero dimension or image count.");
        int plane = vramW * h;
        if (data.Length < DataStart + count * 5 * plane) throw new InvalidDataException("BOS file is shorter than its header requires.");

        var pal = Planar.ReadPalette(data.Slice(16, 48));
        var frames = new List<IndexedImageA>(count);
        for (int i = 0; i < count; i++)
        {
            var f = data.Slice(DataStart + i * 5 * plane, 5 * plane);
            // In the file, a set alpha bit means TRANSPARENT (the original draws where alpha is 0).
            byte[] alpha = Planar.ToBits(f[..plane], vramW, h, invert: true);
            byte[] px = Planar.ToIndices(f.Slice(plane, plane), f.Slice(2 * plane, plane),
                f.Slice(3 * plane, plane), f.Slice(4 * plane, plane), vramW, h);
            frames.Add(new IndexedImageA(vramW * 8, h, px, alpha, pal));
        }
        return new SpriteSheet(frames);
    }
}

/// <summary>.GRC: monochrome sprites in 8-pixel-wide units. Pixels are 0/1 and Alpha equals the bits. ref: th01/formats/grc.cpp</summary>
public static class GrcSprites
{
    private const int DataStart = 64;

    public static SpriteSheet Load(ReadOnlySpan<byte> data)
    {
        if (data.Length < DataStart || !data[..4].SequenceEqual("GRCG"u8))
            throw new InvalidDataException("Not a GRC file (missing GRCG header).");
        int vramW = BitConverter.ToInt16(data.Slice(4, 2)), h = BitConverter.ToInt16(data.Slice(6, 2)), count = data[8];
        if (vramW <= 0 || h <= 0 || count == 0) throw new InvalidDataException("GRC header has a zero dimension or image count.");
        int size = vramW * h;
        if (data.Length < DataStart + count * size) throw new InvalidDataException("GRC file is shorter than its header requires.");

        var frames = new List<IndexedImageA>(count);
        for (int i = 0; i < count; i++)
        {
            byte[] bits = Planar.ToBits(data.Slice(DataStart + i * size, size), vramW, h);
            frames.Add(new IndexedImageA(vramW * 8, h, bits, (byte[])bits.Clone()));
        }
        return new SpriteSheet(frames);
    }
}
