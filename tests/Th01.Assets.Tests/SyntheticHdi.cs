using System.Text;

namespace Th01.Assets.Tests;

/// <summary>Builds a small synthetic Anex86 HDI (FAT12, 512 B sectors, 1 sector/cluster) holding the given root files.</summary>
public static class SyntheticHdi
{
    public static byte[] Build(IReadOnlyDictionary<string, byte[]> rootFiles)
    {
        Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
        var sjis = Encoding.GetEncoding(932);
        const int sec = 512, partLba = 68, fatSectors = 8, rootEntries = 64;
        int clusters = rootFiles.Values.Sum(v => Math.Max(1, (v.Length + sec - 1) / sec));
        var img = new byte[4096 + (partLba + 1 + fatSectors + rootEntries * 32 / sec + clusters + 2) * sec];
        void W32(int o, int v) => BitConverter.GetBytes(v).CopyTo(img, o);
        void W16(int o, int v) => BitConverter.GetBytes((ushort)v).CopyTo(img, o);

        W32(8, 4096); W32(16, sec); W32(20, 17); W32(24, 4); W32(28, 10);
        int pt = 4096 + sec;
        img[pt] = 0xA1; img[pt + 1] = 0x81; W16(pt + 10, 1); // partition starts at LBA 68

        int vbr = 4096 + partLba * sec;
        W16(vbr + 11, sec); img[vbr + 13] = 1; W16(vbr + 14, 1); img[vbr + 16] = 1;
        W16(vbr + 17, rootEntries); W16(vbr + 22, fatSectors);
        int fat = vbr + sec, root = fat + fatSectors * sec, data = root + rootEntries * 32;

        void Fat(int c, int v)
        {
            int o = fat + c * 3 / 2;
            int cur = img[o] | (img[o + 1] << 8);
            cur = (c & 1) != 0 ? (cur & 0x000F) | (v << 4) : (cur & 0xF000) | v;
            img[o] = (byte)cur; img[o + 1] = (byte)(cur >> 8);
        }
        Fat(0, 0xFF8); Fat(1, 0xFFF);

        int next = 2, entry = 0;
        foreach (var (name, bytes) in rootFiles)
        {
            int dot = name.LastIndexOf('.');
            string stem = dot < 0 ? name : name[..dot], ext = dot < 0 ? "" : name[(dot + 1)..];
            int o = root + entry++ * 32;
            Array.Fill(img, (byte)' ', o, 11);
            sjis.GetBytes(stem).CopyTo(img, o);
            sjis.GetBytes(ext).CopyTo(img, o + 8);
            img[o + 11] = 0x20; W16(o + 26, next); W32(o + 28, bytes.Length);
            int count = Math.Max(1, (bytes.Length + sec - 1) / sec);
            for (int i = 0; i < count; i++)
            {
                int c = next + i;
                Array.Copy(bytes, i * sec, img, data + (c - 2) * sec, Math.Min(sec, bytes.Length - i * sec));
                Fat(c, i == count - 1 ? 0xFFF : c + 1);
            }
            next += count;
        }
        return img;
    }

    /// <summary>The required game files (see ExpectedFiles) plus DOS boot files, with tiny dummy contents.</summary>
    /// <param name="complete">false leaves out OP.EXE, so the result must be rejected as "not the game".</param>
    public static Dictionary<string, byte[]> FakeGameFiles(bool complete = true)
    {
        var files = new Dictionary<string, byte[]>
        {
            ["IO.SYS"] = [1, 2, 3],
            ["COMMAND.COM"] = [4, 5, 6],
        };
        foreach (var f in ExpectedFiles.All.Where(f => f.Required))
            files[f.Name] = Encoding.ASCII.GetBytes("fake " + f.Name);
        if (!complete) files.Remove("OP.EXE");
        return files;
    }
}
