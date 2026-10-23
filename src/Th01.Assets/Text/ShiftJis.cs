using System.Text;

namespace Th01.Assets.Text;

/// <summary>
/// Shift-JIS (code page 932) helpers. The game stores text, ranking names and file names as Shift-JIS;
/// the port draws Unicode code points, so everything goes through here.
/// </summary>
public static class ShiftJis
{
    private static readonly Encoding Sjis = Make();

    private static Encoding Make()
    {
        Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
        // Throw on invalid bytes so callers can tell text from data.
        return Encoding.GetEncoding(932, EncoderFallback.ExceptionFallback, DecoderFallback.ExceptionFallback);
    }

    /// <summary>First byte of a two-byte character (0x81..0x9F, 0xE0..0xFC).</summary>
    public static bool IsLead(byte b) => b is >= 0x81 and <= 0x9F or >= 0xE0 and <= 0xFC;

    /// <summary>Second byte of a two-byte character (0x40..0x7E, 0x80..0xFC).</summary>
    public static bool IsTrail(byte b) => b is >= 0x40 and <= 0x7E or >= 0x80 and <= 0xFC;

    /// <summary>Half-width katakana (single byte 0xA1..0xDF).</summary>
    public static bool IsHalfKana(byte b) => b is >= 0xA1 and <= 0xDF;

    /// <summary>Decodes bytes that must be valid Shift-JIS; returns null if they are not.</summary>
    public static string? TryDecode(ReadOnlySpan<byte> bytes)
    {
        try { return Sjis.GetString(bytes); }
        catch (DecoderFallbackException) { return null; }
    }

    /// <summary>Decodes leniently: invalid bytes become U+FFFD.</summary>
    public static string Decode(ReadOnlySpan<byte> bytes) => Encoding.GetEncoding(932).GetString(bytes);

    /// <summary>
    /// One character stored as a 16-bit cell (single byte in the low half, or lead byte high / trail byte low;
    /// this is how the ranking name table keeps kanji). Returns null for an invalid code.
    /// </summary>
    public static string? DecodeCell(ushort code)
    {
        if (code <= 0xFF) return code == 0 ? null : TryDecode([(byte)code]);
        return TryDecode([(byte)(code >> 8), (byte)code]);
    }

    /// <summary>Code point for a 16-bit cell, or null if it does not decode to exactly one character.</summary>
    public static int? CodePointOf(ushort code)
    {
        string? s = DecodeCell(code);
        if (s is null) return null;
        var e = s.EnumerateRunes();
        using var it = e.GetEnumerator();
        if (!it.MoveNext()) return null;
        int cp = it.Current.Value;
        return it.MoveNext() ? null : cp;
    }
}
