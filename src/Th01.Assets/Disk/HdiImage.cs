using System.Text;

namespace Th01.Assets.Disk;

public sealed record HdiEntry(string Path, long Size, bool IsDirectory, int FirstCluster);

/// <summary>
/// Read-only access to the first FAT12 partition of an Anex86 .HDI image (PC-98 partition table).
/// Names are decoded as Shift-JIS. Only used to get the user's own files out of their own image.
/// </summary>
public sealed class HdiImage
{
    readonly byte[] _buf;
    readonly int _fatOffset, _rootOffset, _dataOffset, _bytesPerSector, _sectorsPerCluster, _rootEntries;
    static readonly Encoding ShiftJis = CreateShiftJis();

    static Encoding CreateShiftJis()
    {
        Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
        return Encoding.GetEncoding(932);
    }

    public HdiImage(byte[] image)
    {
        _buf = image;
        if (image.Length < 4096 + 1024) throw new InvalidDataException("File too small to be an HDI image.");
        int headerSize = Int(8), sectorSize = Int(16), spt = Int(20), heads = Int(24);
        if (headerSize != 4096 || sectorSize is not (256 or 512 or 1024) || spt <= 0 || heads <= 0)
            throw new InvalidDataException("Not a recognised Anex86 HDI header.");

        // PC-98 partition table: LBA 1, 32-byte entries; first used entry.
        int pt = headerSize + sectorSize;
        int entry = -1;
        for (int i = 0; i < 16; i++)
            if (_buf[pt + i * 32] != 0 || _buf[pt + i * 32 + 1] != 0) { entry = pt + i * 32; break; }
        if (entry < 0 || _buf[entry] == 0xE5) throw new InvalidDataException("No partition found.");

        int sector = _buf[entry + 8], head = _buf[entry + 9], cyl = Short(entry + 10);
        long partLba = ((long)cyl * heads + head) * spt + sector;
        int vbr = checked(headerSize + (int)(partLba * sectorSize));

        _bytesPerSector = Short(vbr + 11);
        _sectorsPerCluster = _buf[vbr + 13];
        int reserved = Short(vbr + 14), fats = _buf[vbr + 16];
        _rootEntries = Short(vbr + 17);
        int fatSectors = Short(vbr + 22);
        if (_bytesPerSector == 0 || _sectorsPerCluster == 0 || fats == 0 || fatSectors == 0)
            throw new InvalidDataException("Invalid FAT parameters.");

        _fatOffset = vbr + reserved * _bytesPerSector;
        _rootOffset = _fatOffset + fats * fatSectors * _bytesPerSector;
        _dataOffset = _rootOffset + (_rootEntries * 32 + _bytesPerSector - 1) / _bytesPerSector * _bytesPerSector;
    }

    int Int(int o) => BitConverter.ToInt32(_buf, o);
    int Short(int o) => BitConverter.ToUInt16(_buf, o);

    int NextCluster(int c)
    {
        int o = _fatOffset + c * 3 / 2;
        int v = Short(o);
        return (c & 1) != 0 ? v >> 4 : v & 0xFFF;
    }

    IEnumerable<int> Chain(int c)
    {
        int guard = 0;
        while (c >= 2 && c < 0xFF0 && guard++ < 65536) { yield return c; c = NextCluster(c); }
    }

    int ClusterOffset(int c) => _dataOffset + (c - 2) * _sectorsPerCluster * _bytesPerSector;

    /// <summary>All files (and directories) under the root, with '/' separated paths.</summary>
    public IEnumerable<HdiEntry> Enumerate()
    {
        foreach (var e in ReadDir(_rootOffset, _rootEntries, "")) yield return e;
    }

    IEnumerable<HdiEntry> ReadDir(int offset, int count, string prefix)
    {
        for (int i = 0; i < count; i++)
        {
            int o = offset + i * 32;
            if (_buf[o] == 0) yield break;
            byte attr = _buf[o + 11];
            if (_buf[o] == 0xE5 || attr == 0x0F || (attr & 0x08) != 0) continue;
            string name = ShiftJis.GetString(_buf, o, 8).TrimEnd(' ');
            string ext = ShiftJis.GetString(_buf, o + 8, 3).TrimEnd(' ');
            if (name.StartsWith('.')) continue;
            string path = prefix + name + (ext.Length > 0 ? "." + ext : "");
            int cluster = Short(o + 26);
            long size = (uint)Int(o + 28);
            if ((attr & 0x10) != 0)
            {
                yield return new HdiEntry(path, 0, true, cluster);
                foreach (int c in Chain(cluster))
                    foreach (var e in ReadDir(ClusterOffset(c), _sectorsPerCluster * _bytesPerSector / 32, path + "/"))
                        yield return e;
            }
            else yield return new HdiEntry(path, size, false, cluster);
        }
    }

    public byte[] ReadFile(HdiEntry entry)
    {
        var data = new byte[entry.Size];
        int clusterBytes = _sectorsPerCluster * _bytesPerSector;
        long pos = 0;
        foreach (int c in Chain(entry.FirstCluster))
        {
            if (pos >= entry.Size) break;
            int n = (int)Math.Min(clusterBytes, entry.Size - pos);
            Buffer.BlockCopy(_buf, ClusterOffset(c), data, (int)pos, n);
            pos += n;
        }
        if (pos < entry.Size) throw new InvalidDataException($"Truncated cluster chain for {entry.Path}.");
        return data;
    }
}
