using System.Text;
using Th01.Assets.Config;
using Th01.Assets.Formats;

namespace Th01.Assets.Tests;

/// <summary>Builds small synthetic archives in memory; no game data involved.</summary>
internal static class PackBuilder
{
    public static byte[] Rle(byte[] data)
    {
        var o = new List<byte>();
        int i = 0;
        while (i < data.Length)
        {
            byte b = data[i];
            int n = 1;
            while (i + n < data.Length && data[i + n] == b) n++;
            if (n == 1) o.Add(b);
            else
            {
                o.Add(b); o.Add(b);
                int extra = n - 2;
                while (extra > 255) { o.Add(255); o.Add(b); extra -= 256; }
                o.Add((byte)extra);
            }
            i += n;
        }
        return o.ToArray();
    }

    public static byte[] Build(params (string Name, byte[] Data, bool Rle)[] files)
    {
        var header = new byte[PackFile.HeaderSize];
        var body = new List<byte>();
        for (int i = 0; i < files.Length; i++)
        {
            var (name, data, rle) = files[i];
            byte[] stored = rle ? Rle(data) : data;
            var e = header.AsSpan(i * PackFile.EntrySize, PackFile.EntrySize);
            e[0] = rle ? (byte)0x95 : (byte)0x82; e[1] = rle ? (byte)0x95 : (byte)0x00; e[2] = 3;
            for (int c = 0; c < name.Length; c++) e[3 + c] = (byte)~name[c];
            BitConverter.GetBytes(stored.Length).CopyTo(e.Slice(16));
            BitConverter.GetBytes(data.Length).CopyTo(e.Slice(20));
            BitConverter.GetBytes(PackFile.HeaderSize + body.Count).CopyTo(e.Slice(24));
            body.AddRange(stored.Select(b => (byte)(b ^ PackFile.DefaultKey)));
        }
        return header.Concat(body).ToArray();
    }
}

public class PackFileTests
{
    [Fact]
    public void Lists_And_Reads_Raw_And_Rle()
    {
        byte[] raw = [1, 2, 3, 4, 5];
        byte[] rle = [9, 9, 9, 9, 9, 7, 8, 8, 1, 2, 2, 2];
        byte[] big = Enumerable.Repeat((byte)0x55, 700).Append((byte)1).ToArray();
        var ms = new MemoryStream(PackBuilder.Build(("A.PTN", raw, false), ("B.BOS", rle, true), ("C.GRC", big, true)));

        Assert.Equal(["A.PTN", "B.BOS", "C.GRC"], PackFile.List(ms));
        Assert.Equal(raw, PackFile.Read(ms, "a.ptn"));
        Assert.Equal(rle, PackFile.Read(ms, "B.BOS"));
        Assert.Equal(big, PackFile.Read(ms, "C.GRC"));
        Assert.Throws<FileNotFoundException>(() => PackFile.Read(ms, "NOPE.X"));
    }

    [Fact]
    public void DocumentedRleExample()
    {
        // "zz\x02z\x01yy\x00x" -> "zzzzzzyy" then x (emitted here; the original drops a trailing literal)
        byte[] packed = [(byte)'z', (byte)'z', 2, (byte)'z', 1, (byte)'y', (byte)'y', 0, (byte)'x'];
        Assert.Equal(Encoding.ASCII.GetBytes("zzzzzzyyx"), PackFile.UnRle(packed, 9, "t"));
    }

    [Fact]
    public void Rle_StopsAtOrgSize_AndRejectsTruncation()
    {
        byte[] packed = [1, 1, 200]; // would produce 202 bytes
        Assert.Equal(Enumerable.Repeat((byte)1, 10).ToArray(), PackFile.UnRle(packed, 10, "t"));
        Assert.Throws<InvalidDataException>(() => PackFile.UnRle(packed, 500, "t"));
    }

    [Fact]
    public void RejectsOutOfRangeEntries()
    {
        byte[] a = PackBuilder.Build(("A.PTN", [1, 2, 3], false));
        var ms = new MemoryStream(a.Take(a.Length - 1).ToArray());
        Assert.Throws<InvalidDataException>(() => PackFile.Entries(ms));
        Assert.Throws<InvalidDataException>(() => PackFile.Entries(new MemoryStream(new byte[10])));
    }

    [SkippableFact]
    public void RealArchive_AllEntriesDecode()
    {
        string? path = GameDataConfig.Resolve();
        Skip.If(path is null || !File.Exists(Path.Combine(path, ExpectedFiles.ArchiveName)),
            $"Game data not configured or archive missing. Set {GameDataConfig.EnvVar}.");
        using var fs = File.OpenRead(Path.Combine(path!, ExpectedFiles.ArchiveName));
        foreach (var e in PackFile.Entries(fs))
            Assert.Equal(e.OrgSize, PackFile.Read(fs, e).Length);
    }
}
