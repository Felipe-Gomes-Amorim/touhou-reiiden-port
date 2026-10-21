using Th01.Assets.Disk;

namespace Th01.Tools.Tests;

public class HdiImageTests
{
    // Builds a tiny synthetic Anex86 HDI: 512 B sectors, 1 sector/cluster, FAT12, partition at LBA 68.
    static byte[] BuildImage(out byte[] fileA)
    {
        const int sec = 512, partLba = 68;
        var img = new byte[4096 + (partLba + 40) * sec];
        void W32(int o, int v) => BitConverter.GetBytes(v).CopyTo(img, o);
        void W16(int o, int v) => BitConverter.GetBytes((ushort)v).CopyTo(img, o);

        W32(8, 4096); W32(16, sec); W32(20, 17); W32(24, 4); W32(28, 10);

        int pt = 4096 + sec;
        img[pt] = 0xA1; img[pt + 1] = 0x81; img[pt + 8] = 0; img[pt + 9] = 0; W16(pt + 10, 1); // LBA 68

        int vbr = 4096 + partLba * sec;
        W16(vbr + 11, sec); img[vbr + 13] = 1; W16(vbr + 14, 1); img[vbr + 16] = 1;
        W16(vbr + 17, 16); W16(vbr + 22, 1);
        int fat = vbr + sec, root = fat + sec, data = root + sec;

        void Fat(int c, int v)
        {
            int o = fat + c * 3 / 2;
            int cur = img[o] | (img[o + 1] << 8);
            cur = (c & 1) != 0 ? (cur & 0x000F) | (v << 4) : (cur & 0xF000) | v;
            img[o] = (byte)cur; img[o + 1] = (byte)(cur >> 8);
        }
        Fat(0, 0xFF8); Fat(1, 0xFFF);

        void Entry(int index, string name, string ext, byte attr, int cluster, int size, int dirBase)
        {
            int o = dirBase + index * 32;
            System.Text.Encoding.ASCII.GetBytes(name.PadRight(8)).CopyTo(img, o);
            System.Text.Encoding.ASCII.GetBytes(ext.PadRight(3)).CopyTo(img, o + 8);
            img[o + 11] = attr; W16(o + 26, cluster); W32(o + 28, size);
        }

        // A.BIN: 700 bytes over clusters 2 -> 3.
        fileA = new byte[700];
        for (int i = 0; i < fileA.Length; i++) fileA[i] = (byte)(i * 7 + 1);
        Array.Copy(fileA, 0, img, data + 0 * sec, 512);
        Array.Copy(fileA, 512, img, data + 1 * sec, 188);
        Fat(2, 3); Fat(3, 0xFFF);
        Entry(0, "A", "BIN", 0x20, 2, 700, root);

        // SUB/B.TXT: directory in cluster 4, file in cluster 5.
        Entry(1, "SUB", "", 0x10, 4, 0, root);
        Fat(4, 0xFFF); Fat(5, 0xFFF);
        Entry(0, "B", "TXT", 0x20, 5, 3, data + 2 * sec);
        "hey"u8.CopyTo(img.AsSpan(data + 3 * sec));
        return img;
    }

    [Fact]
    public void ListsAndReadsFilesAcrossClusters()
    {
        var hdi = new HdiImage(BuildImage(out var expected));
        var entries = hdi.Enumerate().ToList();
        Assert.Contains(entries, e => e.Path == "A.BIN" && e.Size == 700);
        Assert.Contains(entries, e => e.Path == "SUB" && e.IsDirectory);
        Assert.Equal(expected, hdi.ReadFile(entries.Single(e => e.Path == "A.BIN")));
        Assert.Equal("hey"u8.ToArray(), hdi.ReadFile(entries.Single(e => e.Path == "SUB/B.TXT")));
    }

    [Fact]
    public void RejectsGarbage() =>
        Assert.Throws<InvalidDataException>(() => new HdiImage(new byte[20000]));
}
