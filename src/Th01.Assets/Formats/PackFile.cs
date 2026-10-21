using System.Text;

namespace Th01.Assets.Formats;

/// <summary>One entry of the archive's header table.</summary>
public sealed record PackEntry(string Name, bool Compressed, int PackSize, int OrgSize, int Offset);

/// <summary>
/// The 東方靈異.伝 archive (docs/FORMATS.md, "Archive"). Reads only from the given stream;
/// output is always limited to the entry's orgsize. ref: th01/formats/pf.cpp
/// </summary>
public static class PackFile
{
    public const byte DefaultKey = 0x76; // ref: th01/shiftjis/fns.hpp ARC_KEY
    public const int MaxEntries = 64;
    public const int EntrySize = 32;
    public const int HeaderSize = MaxEntries * EntrySize;
    public const int NameLength = 13;

    public static IReadOnlyList<string> List(Stream archive) => Entries(archive).Select(e => e.Name).ToList();

    public static IReadOnlyList<PackEntry> Entries(Stream archive)
    {
        ArgumentNullException.ThrowIfNull(archive);
        byte[] header = new byte[HeaderSize];
        archive.Position = 0;
        if (archive.ReadAtLeast(header, HeaderSize, throwOnEndOfStream: false) != HeaderSize)
            throw new InvalidDataException("Archive is shorter than its 2048-byte header table.");

        var list = new List<PackEntry>();
        for (int i = 0; i < MaxEntries; i++)
        {
            var e = header.AsSpan(i * EntrySize, EntrySize);
            if (e[0] == 0) break; // first byte of the type field is 0: end of table

            bool compressed = e[0] == 0x95 && e[1] == 0x95;
            // Name bytes are stored bitwise-inverted up to the first raw 0 byte.
            var name = new StringBuilder();
            for (int c = 0; c < NameLength; c++)
            {
                byte raw = e[3 + c];
                if (raw == 0) break;
                name.Append((char)(byte)~raw);
            }
            int packSize = BitConverter.ToInt32(e.Slice(16, 4));
            int orgSize = BitConverter.ToInt32(e.Slice(20, 4));
            int offset = BitConverter.ToInt32(e.Slice(24, 4));
            if (packSize < 0 || orgSize < 0 || offset < 0 || (long)offset + packSize > archive.Length)
                throw new InvalidDataException($"Entry {i} ('{name}') points outside the archive.");
            list.Add(new PackEntry(name.ToString(), compressed, packSize, orgSize, offset));
        }
        return list;
    }

    /// <summary>Extracts a file by name (case-insensitive). Throws FileNotFoundException if absent.</summary>
    public static byte[] Read(Stream archive, string name, byte key = DefaultKey)
    {
        var entry = Entries(archive).FirstOrDefault(e => string.Equals(e.Name, name, StringComparison.OrdinalIgnoreCase))
            ?? throw new FileNotFoundException($"'{name}' is not in the archive.");
        return Read(archive, entry, key);
    }

    public static byte[] Read(Stream archive, PackEntry entry, byte key = DefaultKey)
    {
        byte[] packed = new byte[entry.PackSize];
        archive.Position = entry.Offset;
        archive.ReadExactly(packed);
        for (int i = 0; i < packed.Length; i++) packed[i] ^= key;

        if (!entry.Compressed)
        {
            // The original reads packsize bytes into an orgsize buffer; we keep only orgsize.
            if (packed.Length < entry.OrgSize)
                throw new InvalidDataException($"'{entry.Name}': stored size is smaller than the original size.");
            return packed.AsSpan(0, entry.OrgSize).ToArray();
        }
        return UnRle(packed, entry.OrgSize, entry.Name);
    }

    /// <summary>
    /// Decodes the run-length scheme: after two equal bytes a count byte follows (number of extra
    /// copies); if the next byte equals the run byte again, the run continues with another count.
    /// Stops at orgSize or when input ends.
    /// </summary>
    public static byte[] UnRle(byte[] input, int orgSize, string name)
    {
        byte[] output = new byte[orgSize];
        int ip = 0, op = 0;

        bool Next(out byte b)
        {
            if (ip < input.Length) { b = input[ip++]; return true; }
            b = 0; return false;
        }
        bool Put(byte b)
        {
            if (op >= output.Length) return false;
            output[op++] = b; return true;
        }

        if (Next(out byte lit2))
        {
            while (true)
            {
                byte lit1;
                do
                {
                    lit1 = lit2;
                    if (!Put(lit1)) goto done;
                    if (!Next(out lit2)) goto done;
                } while (lit1 != lit2);
                if (!Put(lit2)) goto done;

                while (true)
                {
                    if (!Next(out byte runs)) goto done;
                    for (; runs > 0; runs--) if (!Put(lit1)) goto done;
                    if (!Next(out lit2)) goto done;
                    if (lit2 != lit1) break;
                    if (!Put(lit1)) goto done;
                }
                // Difference from the original: a literal that follows a run and is the very last
                // input byte is emitted here; the original drops it.
            }
        }
    done:
        if (op != orgSize) throw new InvalidDataException($"'{name}': compressed data ends after {op} of {orgSize} bytes.");
        return output;
    }
}
