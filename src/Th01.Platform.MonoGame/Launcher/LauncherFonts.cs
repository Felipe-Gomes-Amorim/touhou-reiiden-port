using Th01.Assets.Config;
using Th01.Assets.Text;
using Th01.Core.Graphics;

namespace Th01.Platform.MonoGame.Launcher;

/// <summary>Whether the user's FONT.ROM could be used.</summary>
public enum FontRomState { NotFound, Found, Unsupported }

/// <summary>
/// Draws glyphs from the user's own font ROM (<see cref="IGlyphProvider"/>) and falls back to another font for what the ROM does not
/// have: characters outside JIS X 0208, the control codes, and the two ANK positions where the PC-98 shows a different symbol than
/// the ASCII one (0x5C is a yen sign, 0x7E an overline), which would be wrong in file paths.
/// </summary>
public sealed class FontRomBridge(IGlyphProvider rom, IFontSource fallback) : IFontSource
{
    readonly Dictionary<int, Glyph?> _cache = [];

    public bool TryGetGlyph(int codePoint, out Glyph glyph)
    {
        if (!_cache.TryGetValue(codePoint, out var cached))
        {
            cached = FromRom(codePoint);
            _cache[codePoint] = cached;
        }
        if (cached is { } g) { glyph = g; return true; }
        return fallback.TryGetGlyph(codePoint, out glyph);
    }

    Glyph? FromRom(int codePoint)
    {
        byte[] bitmap; int w, h;
        if (codePoint is >= 0x20 and <= 0x7E)
        {
            if (codePoint is 0x5C or 0x7E || !rom.TryGetAnk(codePoint, out bitmap, out w, out h)) return null;
        }
        else
        {
            if (codePoint < 0x80 || codePoint > 0x10FFFF || codePoint is >= 0xD800 and <= 0xDFFF) return null;
            if (!TextCodec.TryUnicodeToJis(char.ConvertFromUtf32(codePoint), out int row, out int col)) return null;
            if (!rom.TryGet(row, col, out bitmap, out w, out h)) return null;
        }
        return Pack(bitmap, w, h);
    }

    /// <summary>One byte per pixel (0/1, row-major) to the renderer's packed rows, MSB first.</summary>
    static Glyph? Pack(byte[] bitmap, int w, int h)
    {
        if ((w != 8 && w != 16) || h != Glyph.Height || bitmap.Length < w * h) return null;
        int bytesPerRow = w / 8;
        var rows = new byte[bytesPerRow * h];
        for (int y = 0; y < h; y++)
            for (int x = 0; x < w; x++)
                if (bitmap[y * w + x] != 0) rows[y * bytesPerRow + x / 8] |= (byte)(0x80 >> (x & 7));
        return new Glyph(w, rows);
    }
}

/// <summary>
/// The font the program draws with: the user's FONT.ROM through <see cref="FontRomBridge"/> when there is one and the player prefers it,
/// else the free fonts built in. Swapping happens at once (the launcher), and the game picks up the current one when it starts.
/// </summary>
public sealed class LauncherFonts : IFontSource
{
    readonly IFontSource _free;
    IFontSource _active;

    public LauncherFonts(IFontSource free) { _free = free; _active = free; }

    public FontRomState State { get; private set; } = FontRomState.NotFound;
    public string? RomPath { get; private set; }

    public bool TryGetGlyph(int codePoint, out Glyph glyph) => _active.TryGetGlyph(codePoint, out glyph);

    /// <summary>Finds the ROM (the player's choice, then TH01_FONT_ROM / config.local.json, then the game folder) and picks the font.</summary>
    /// <param name="userPath">The path chosen in the launcher, if any.</param>
    public void Configure(string? userPath, bool preferRom, string? gameFolder)
    {
        State = FontRomState.NotFound;
        RomPath = null;
        PcFontRom? rom = null;
        bool first = true;
        foreach (string? candidate in Candidates(userPath, gameFolder))
        {
            bool chosen = first;   // the player's own pick is final: a bad file is reported, not silently replaced by another ROM
            first = false;
            if (candidate is null || !File.Exists(candidate)) continue;
            rom = TryLoad(candidate, out var state);
            State = state;
            RomPath = candidate;
            if (rom is not null || chosen) break;
        }
        _active = preferRom && rom is not null ? new FontRomBridge(rom, _free) : _free;
    }

    static IEnumerable<string?> Candidates(string? userPath, string? gameFolder)
    {
        yield return userPath;
        string? configured = null;
        try { configured = FontRomConfig.Resolve(); } catch (IOException) { }
        yield return configured;
        if (gameFolder is not null) yield return Path.Combine(gameFolder, FontRomConfig.FileName);
    }

    static PcFontRom? TryLoad(string path, out FontRomState state)
    {
        try
        {
            byte[] bytes = File.ReadAllBytes(path);
            if (!PcFontRom.LooksLikeEmulatorImage(bytes)) { state = FontRomState.Unsupported; return null; }
            state = FontRomState.Found;
            return new PcFontRom(bytes, FontRomLayout.EmulatorImage);
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            state = FontRomState.Unsupported;
            return null;
        }
    }
}
