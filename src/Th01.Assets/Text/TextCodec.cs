using System.Text;

namespace Th01.Assets.Text;

/// <summary>
/// Conversions between Shift-JIS (code page 932, as used by the PC-98 game), Unicode and JIS X 0208
/// (row/column, "ku/ten", both 1-based). Pure functions; the game strings are decoded strictly and
/// anything invalid throws <see cref="InvalidDataException"/>.
/// </summary>
public static class TextCodec
{
    private static readonly Encoding Sjis = CreateEncoding();

    private static Encoding CreateEncoding()
    {
        Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
        return Encoding.GetEncoding(932, EncoderFallback.ExceptionFallback, DecoderFallback.ExceptionFallback);
    }

    /// <summary>True when the byte can start a two-byte Shift-JIS character.</summary>
    public static bool IsLeadByte(byte b) => b is (>= 0x81 and <= 0x9F) or (>= 0xE0 and <= 0xFC);

    public static bool IsTrailByte(byte b) => b is (>= 0x40 and <= 0x7E) or (>= 0x80 and <= 0xFC);

    /// <summary>Decodes a Shift-JIS byte string (ASCII, half-width katakana, double-byte). Throws on invalid input.</summary>
    public static string Decode(ReadOnlySpan<byte> bytes)
    {
        try { return Sjis.GetString(bytes); }
        catch (DecoderFallbackException e) { throw new InvalidDataException("Invalid Shift-JIS data.", e); }
    }

    public static bool TryDecode(ReadOnlySpan<byte> bytes, out string text)
    {
        try { text = Sjis.GetString(bytes); return true; }
        catch (DecoderFallbackException) { text = ""; return false; }
    }

    /// <summary>Encodes text to Shift-JIS; characters that do not exist in it throw <see cref="InvalidDataException"/>.</summary>
    public static byte[] Encode(string text)
    {
        ArgumentNullException.ThrowIfNull(text);
        try { return Sjis.GetBytes(text); }
        catch (EncoderFallbackException e) { throw new InvalidDataException("Text has characters that Shift-JIS cannot represent.", e); }
    }

    /// <summary>
    /// Decodes the ranking-name cells (one ushort per cell, bytes (lead &lt;&lt; 8) | trail; 0x2020 is two ASCII spaces)
    /// into a string. Cells with two ASCII/half-width bytes yield two characters.
    /// </summary>
    public static string DecodeCells(ReadOnlySpan<ushort> cells)
    {
        byte[] bytes = new byte[cells.Length * 2];
        for (int i = 0; i < cells.Length; i++)
        {
            bytes[i * 2] = (byte)(cells[i] >> 8);
            bytes[i * 2 + 1] = (byte)(cells[i] & 0xFF);
        }
        return Decode(bytes);
    }

    /// <summary>Encodes text into cells of two bytes; a text whose Shift-JIS length is odd is padded with an ASCII space.</summary>
    public static ushort[] EncodeCells(string text, int cellCount)
    {
        byte[] b = Encode(text);
        if (b.Length > cellCount * 2) throw new ArgumentException($"Text needs more than {cellCount} cells.", nameof(text));
        var cells = new ushort[cellCount];
        for (int i = 0; i < cellCount; i++)
        {
            byte hi = i * 2 < b.Length ? b[i * 2] : (byte)0x20;
            byte lo = i * 2 + 1 < b.Length ? b[i * 2 + 1] : (byte)0x20;
            cells[i] = (ushort)((hi << 8) | lo);
        }
        return cells;
    }

    /// <summary>The Unicode character of a double-byte Shift-JIS code, or null when the code is not a valid character.</summary>
    public static string? ToUnicode(ushort sjis)
    {
        byte[] b = [(byte)(sjis >> 8), (byte)(sjis & 0xFF)];
        if (!IsLeadByte(b[0]) || !IsTrailByte(b[1])) return null;
        return TryDecode(b, out string s) && s.Length >= 1 ? s : null;
    }

    /// <summary>The double-byte Shift-JIS code of a character, or false (not representable, or single-byte).</summary>
    public static bool TryFromUnicode(string character, out ushort sjis)
    {
        sjis = 0;
        try
        {
            byte[] b = Sjis.GetBytes(character);
            if (b.Length != 2 || !IsLeadByte(b[0])) return false;
            sjis = (ushort)((b[0] << 8) | b[1]);
            return true;
        }
        catch (EncoderFallbackException) { return false; }
    }

    /// <summary>Shift-JIS double-byte code to JIS X 0208 row and column (1-based). Throws for invalid codes.</summary>
    public static (int Row, int Col) ToJis(ushort sjis)
    {
        int s1 = sjis >> 8, s2 = sjis & 0xFF;
        if (!IsLeadByte((byte)s1) || !IsTrailByte((byte)s2) || s1 > 0xEF)
            throw new ArgumentOutOfRangeException(nameof(sjis), $"0x{sjis:X4} is not a JIS X 0208 Shift-JIS code.");
        int row = (s1 - (s1 <= 0x9F ? 0x81 : 0xC1)) * 2 + 1;
        int col;
        if (s2 >= 0x9F) { row++; col = s2 - 0x9E; }
        else col = s2 - (s2 >= 0x80 ? 0x40 : 0x3F);
        return (row, col);
    }

    /// <summary>JIS X 0208 row and column (1..94) to the Shift-JIS code.</summary>
    public static ushort FromJis(int row, int col)
    {
        if (row is < 1 or > 94 || col is < 1 or > 94) throw new ArgumentOutOfRangeException(nameof(row), "Row and column are 1..94.");
        int s1 = (row - 1) / 2 + (row <= 62 ? 0x81 : 0xC1);
        int s2;
        if (row % 2 == 1) s2 = col + (col <= 63 ? 0x3F : 0x40);
        else s2 = col + 0x9E;
        return (ushort)((s1 << 8) | s2);
    }

    /// <summary>Unicode character to JIS row/column, or false when it is not in JIS X 0208.</summary>
    public static bool TryUnicodeToJis(string character, out int row, out int col)
    {
        row = col = 0;
        if (!TryFromUnicode(character, out ushort sjis)) return false;
        try { (row, col) = ToJis(sjis); return true; }
        catch (ArgumentOutOfRangeException) { return false; }
    }

    /// <summary>Splits a Shift-JIS byte string into cells: a double-byte character is one cell, any other byte its own.</summary>
    public static IEnumerable<ushort> Cells(byte[] bytes)
    {
        for (int i = 0; i < bytes.Length; i++)
        {
            if (IsLeadByte(bytes[i]) && i + 1 < bytes.Length && IsTrailByte(bytes[i + 1]))
            {
                yield return (ushort)((bytes[i] << 8) | bytes[i + 1]);
                i++;
            }
            else yield return bytes[i];
        }
    }
}
