using Th01.Assets.Model;

namespace Th01.Assets.Formats;

/// <summary>
/// .GRP: the PC-98 "Pi" image format with the signature "ZN" and 4-bit palette components, 16 colours.
/// Implemented from the single source listed in docs/FORMATS.md. ref: th01/formats/grp.h
/// </summary>
public static class GrpImage
{
    private const int MaxDimension = 4096, MaxPadding = 4;

    public static IndexedImage Load(Stream s)
    {
        ArgumentNullException.ThrowIfNull(s);
        using var ms = new MemoryStream();
        s.CopyTo(ms);
        return Load(ms.ToArray());
    }

    public static IndexedImage Load(ReadOnlySpan<byte> data)
    {
        int p = 0;
        if (data.Length < 2 || !((data[0] == 'Z' && data[1] == 'N') || (data[0] == 'P' && data[1] == 'i')))
            throw new InvalidDataException("Not a GRP file (missing ZN signature).");
        p = 2;
        while (p < data.Length && data[p] != 0x1A) p++; // comment, ends with 0x1A
        p++;
        // start byte, mode, aspect x, aspect y, bit depth, 4-byte compressor id, 2-byte size of its extra data
        if (p + 5 + 4 + 2 > data.Length) throw new InvalidDataException("GRP header is truncated.");
        p += 1 + 1 + 2; // start, mode, aspect
        int depth = data[p++];
        if (depth != 4) throw new InvalidDataException($"GRP bit depth {depth} is not supported (expected 4).");
        p += 4;
        int extra = (data[p] << 8) | data[p + 1];
        p += 2 + extra;
        if (p + 4 + 48 > data.Length) throw new InvalidDataException("GRP header is truncated.");
        int w = (data[p] << 8) | data[p + 1], h = (data[p + 2] << 8) | data[p + 3];
        p += 4;
        if (w <= 0 || h <= 0 || w > MaxDimension || h > MaxDimension) throw new InvalidDataException($"GRP size {w}x{h} is invalid.");
        var pal = Planar.ReadPalette(data.Slice(p, 48));
        p += 48;

        byte[] pixels = Decode(data[p..], w, h);
        return new IndexedImage(w, h, pixels, pal);
    }

    private sealed class Bits
    {
        private readonly byte[] _d;
        private int _pos; // in bits

        public Bits(byte[] d) { _d = d; _pos = 0; }
        public int BytesUsed => (_pos + 7) / 8;
        public int Length => _d.Length;

        public int Bit()
        {
            int byteIndex = _pos >> 3;
            if (byteIndex >= _d.Length) throw new InvalidDataException("GRP data ends before the image is complete.");
            int b = (_d[byteIndex] >> (7 - (_pos & 7))) & 1;
            _pos++;
            return b;
        }

        public int Read(int n)
        {
            int v = 0;
            for (int i = 0; i < n; i++) v = (v << 1) | Bit();
            return v;
        }
    }

    private static byte[] Decode(ReadOnlySpan<byte> data, int w, int h)
    {
        const int colors = 16;
        int total = w * h;
        byte[] o = new byte[total];

        // delta[a, b]: colour that follows colour a for code b; move-to-front per row.
        var delta = new byte[colors, colors];
        for (int a = 0; a < colors; a++)
            for (int b = 0; b < colors; b++) delta[a, b] = (byte)((colors + a - b) % colors);

        var bits = new Bits(data.ToArray());
        int pos = 0;
        int prev = 0;       // previous output colour (0 at the start)
        bool firstRepeat = true;

        int DeltaCode()
        {
            if (bits.Bit() == 1) return bits.Bit();                // 1x      -> 0..1
            if (bits.Bit() == 0) return 2 + bits.Bit();            // 00x     -> 2..3
            if (bits.Bit() == 0) return 4 + bits.Read(2);          // 010xx   -> 4..7
            return 8 + bits.Read(3);                               // 011xxx  -> 8..15
        }

        void Pixel()
        {
            int code = DeltaCode();
            byte v = delta[prev, code];
            for (int i = code; i > 0; i--) delta[prev, i] = delta[prev, i - 1];
            delta[prev, 0] = v;
            o[pos++] = v;
            prev = v;
        }

        int RunLength() // in pairs of pixels: n ones, a zero, then n bits
        {
            int n = 0;
            while (bits.Bit() == 1)
            {
                n++;
                if (n > 24) throw new InvalidDataException("GRP run length is invalid.");
            }
            return (1 << n) + bits.Read(n);
        }

        int Location()
        {
            if (bits.Bit() == 0) return bits.Bit() == 0 ? 0 : 1;   // 00, 01
            if (bits.Bit() == 0) return 2;                         // 10
            return bits.Bit() == 0 ? 3 : 4;                        // 110, 111
        }

        // Repetition sequence: location code and run length; the chain goes on until a location code
        // equals the one just used. The first repeat of the image is two bytes shorter.
        void Repeat()
        {
            int location = Location();
            while (true)
            {
                int count = RunLength() * 2;
                if (firstRepeat) { count -= 2; firstRepeat = false; }
                count = Math.Min(count, total - pos);
                // location 0 decides once, when the command starts, between a 4- and a 2-byte pattern
                int back = (pos >= 4 && o[pos - 1] != o[pos - 2]) ? 4 : 2;
                for (int i = 0; i < count; i++, pos++)
                    o[pos] = CopySource(o, pos, w, location, back);
                prev = pos > 0 ? o[pos - 1] : 0;
                if (pos >= total) return;
                int next = Location();
                if (next == location) return;
                location = next;
            }
        }

        for (int k = 0; k < 2; k++) Pixel();
        Repeat();
        while (pos < total)
        {
            for (int k = 0; k < 2 && pos < total; k++) Pixel();
            if (pos >= total) break;
            if (bits.Bit() == 1) continue; // another pair of delta codes
            Repeat();
        }

        // The real files end with up to 4 bytes of zero padding after the last code; anything else is an error.
        for (int i = bits.BytesUsed; i < bits.Length; i++)
            if (i - bits.BytesUsed >= MaxPadding || data[i] != 0)
                throw new InvalidDataException($"GRP has {bits.Length - bits.BytesUsed} unexpected byte(s) after the image.");
        return o;
    }

    private static byte CopySource(byte[] o, int pos, int w, int location, int back)
    {
        int src = location switch
        {
            0 => pos - back,
            1 => pos - w,
            2 => pos - 2 * w,
            3 => pos - w + 1,
            _ => pos - w - 1,
        };
        // Before the start of the image the first two bytes repeat.
        if (src >= 0) return o[src];
        int phase = pos & 1;
        return pos >= 2 ? o[location >= 3 ? 1 - phase : phase] : (byte)0;
    }
}
