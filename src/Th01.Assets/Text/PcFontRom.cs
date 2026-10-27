namespace Th01.Assets.Text;

/// <summary>
/// Where the glyphs of a PC-98 font ROM image are. <see cref="EmulatorImage"/> is the layout of the 288768-byte
/// FONT.ROM that emulators use, found by inspecting such a file (docs/FORMATS.md); any other image needs its own
/// numbers. Offsets are file offsets in bytes.
/// </summary>
/// <param name="KanjiOffset">Offset of the glyph for JIS row 1, column 1.</param>
/// <param name="RowStride">Bytes from one JIS row to the next (row r starts at KanjiOffset + (r-1)*RowStride).</param>
/// <param name="CellStride">Bytes from one column to the next inside a row (normally 32 for a 16x16 glyph).</param>
/// <param name="HalvesSplit">False: a 16x16 glyph is 16 rows of 2 bytes (left, right). True: 16 bytes of left halves, then 16 bytes of right halves.</param>
/// <param name="AnkOffset">Offset of the 8x16 glyph of code 0, or -1 when the ROM has no ANK table.</param>
public sealed record FontRomLayout(int KanjiOffset, int RowStride, int CellStride, bool HalvesSplit, int AnkOffset)
{
    /// <summary>
    /// The emulator image: 8x8 ANK at 0, 8x16 ANK at 0x800, then JIS rows of 0xC00 bytes from 0x1800 whose cells are
    /// indexed by the low JIS byte minus 0x20 (so column 1 is at +32); each cell is 16 bytes of left half and 16 of right half.
    /// </summary>
    public static FontRomLayout EmulatorImage { get; } = new(0x1800 + 32, 0xC00, 32, true, 0x800);
}

/// <summary>
/// Glyphs read from a font ROM image supplied by the user (optional "option C" of the font chain). Read-only;
/// nothing from the file is ever stored in the repo. Verified against the 288768-byte emulator image (<see cref="FontRomLayout.EmulatorImage"/>), with
/// real-ROM tests that skip when none is configured (<c>FontRomConfig</c>).
/// </summary>
public sealed class PcFontRom : IGlyphProvider
{
    public const int KanjiSize = 16, AnkWidth = 8, AnkHeight = 16;

    private readonly byte[] _rom;
    private readonly FontRomLayout _layout;

    public PcFontRom(byte[] rom, FontRomLayout layout)
    {
        ArgumentNullException.ThrowIfNull(rom);
        ArgumentNullException.ThrowIfNull(layout);
        if (layout.KanjiOffset < 0 || layout.RowStride <= 0 || layout.CellStride < 32)
            throw new ArgumentException("Invalid font ROM layout.", nameof(layout));
        _rom = rom;
        _layout = layout;
    }

    public const int EmulatorImageSize = 288768;

    /// <summary>True when the image has the size of the emulator FONT.ROM that <see cref="FontRomLayout.EmulatorImage"/> describes.</summary>
    public static bool LooksLikeEmulatorImage(byte[] rom) => rom.Length == EmulatorImageSize;

    /// <summary>Reads a ROM image from a user file; the layout still has to be supplied.</summary>
    public static PcFontRom Load(string path, FontRomLayout layout) => new(File.ReadAllBytes(path), layout);

    public bool TryGet(int jisRow, int jisCol, out byte[] bitmap, out int w, out int h)
    {
        bitmap = []; w = h = 0;
        if (jisRow is < 1 or > 94 || jisCol is < 1 or > 94) return false;
        long start = _layout.KanjiOffset + (long)(jisRow - 1) * _layout.RowStride + (long)(jisCol - 1) * _layout.CellStride;
        if (start + 32 > _rom.Length) return false;
        var g = _rom.AsSpan((int)start, 32);
        bitmap = new byte[KanjiSize * KanjiSize];
        for (int y = 0; y < KanjiSize; y++)
        {
            byte left = _layout.HalvesSplit ? g[y] : g[y * 2];
            byte right = _layout.HalvesSplit ? g[16 + y] : g[y * 2 + 1];
            for (int x = 0; x < 8; x++)
            {
                bitmap[y * KanjiSize + x] = (byte)((left >> (7 - x)) & 1);
                bitmap[y * KanjiSize + 8 + x] = (byte)((right >> (7 - x)) & 1);
            }
        }
        w = h = KanjiSize;
        return true;
    }

    public bool TryGetAnk(int code, out byte[] bitmap, out int w, out int h)
    {
        bitmap = []; w = h = 0;
        if (_layout.AnkOffset < 0 || code is < 0 or > 255) return false;
        long start = _layout.AnkOffset + (long)code * AnkHeight;
        if (start + AnkHeight > _rom.Length) return false;
        bitmap = new byte[AnkWidth * AnkHeight];
        for (int y = 0; y < AnkHeight; y++)
            for (int x = 0; x < AnkWidth; x++)
                bitmap[y * AnkWidth + x] = (byte)((_rom[start + y] >> (7 - x)) & 1);
        w = AnkWidth; h = AnkHeight;
        return true;
    }
}
