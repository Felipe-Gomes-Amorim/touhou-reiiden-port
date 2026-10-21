using Th01.Assets.Config;
using Th01.Assets.Formats;

namespace Th01.Assets.Tests;

public class MdrvFilesTests
{
    private static byte[] SyntheticMdt()
    {
        var d = new byte[0x40 + 4 + 5 + 5 + 1 + 64 + 3];
        BitConverter.GetBytes((ushort)0x0302).CopyTo(d, 0);
        BitConverter.GetBytes((ushort)0x000D).CopyTo(d, 2);
        int text = 0x40, t1 = 0x44, t2 = 0x49, end = 0x4E, voices = 0x4F;
        d[0x40] = (byte)'$'; // text (1 byte, shifted below)
        // table: (0x80 -> t1), (0x81 -> t2), (0x10 -> end)
        BitConverter.GetBytes((ushort)0x80).CopyTo(d, 4); BitConverter.GetBytes((ushort)t1).CopyTo(d, 6);
        BitConverter.GetBytes((ushort)0x81).CopyTo(d, 8); BitConverter.GetBytes((ushort)t2).CopyTo(d, 10);
        BitConverter.GetBytes((ushort)0x10).CopyTo(d, 12); BitConverter.GetBytes((ushort)end).CopyTo(d, 14);
        BitConverter.GetBytes((ushort)voices).CopyTo(d, 0x3A); BitConverter.GetBytes((ushort)(voices + 64)).CopyTo(d, 0x3C);
        d[t1 + 4] = 0xFF; d[t2 + 4] = 0xFF; d[end] = 0xFF;
        _ = text;
        return d;
    }

    [Fact]
    public void Mdt_SplitsTracksVoicesAndTrailer()
    {
        var m = MdtFile.Parse(SyntheticMdt());
        Assert.Equal(0x0302, m.Magic);
        Assert.Equal([0x80, 0x81, 0x10], m.Tracks.Select(t => t.Id));
        Assert.Equal([5, 5, 1], m.Tracks.Select(t => t.Length));
        Assert.Equal(2, m.VoiceCount);
        Assert.Equal(3, m.Trailer.Length);
        Assert.Equal(4, m.Text.Length);
    }

    [Fact]
    public void Mdt_RejectsBrokenFiles()
    {
        Assert.Throws<InvalidDataException>(() => MdtFile.Parse(new byte[10]));
        var d = SyntheticMdt();
        d[12] = 0x11; // no end marker id
        Assert.Throws<InvalidDataException>(() => MdtFile.Parse(d));
        d = SyntheticMdt();
        d[0x3C] = 0x51; // voice range not a multiple of 32
        Assert.Throws<InvalidDataException>(() => MdtFile.Parse(d));
    }

    [Fact]
    public void Mde_ReadsTheEffectTable()
    {
        var d = new byte[0x10 + 8 + 3 + 3 + 2];
        BitConverter.GetBytes((ushort)1).CopyTo(d, 0);
        BitConverter.GetBytes((ushort)2).CopyTo(d, 2);
        BitConverter.GetBytes((ushort)(0x18 + 6)).CopyTo(d, 4);
        BitConverter.GetBytes((ushort)0x4001).CopyTo(d, 0x10); BitConverter.GetBytes((ushort)0x18).CopyTo(d, 0x12);
        BitConverter.GetBytes((ushort)0x4001).CopyTo(d, 0x14); BitConverter.GetBytes((ushort)0x1B).CopyTo(d, 0x16);
        var e = MdeFile.Parse(d);
        Assert.Equal(2, e.Effects.Count);
        Assert.Equal([3, 3], e.Effects.Select(x => x.Length));
        Assert.Equal(2, e.Trailer.Length);
        Assert.Throws<InvalidDataException>(() => MdeFile.Parse(new byte[4]));
    }

    [SkippableFact]
    public void RealMusicFiles_FollowTheObservedStructure()
    {
        string? folder = GameDataConfig.Resolve();
        Skip.If(folder is null || !Directory.Exists(folder), $"Game data not configured. Set {GameDataConfig.EnvVar}.");
        var files = Directory.GetFiles(folder!, "*.MDT");
        Skip.If(files.Length == 0, "No .MDT files in the game folder.");
        foreach (string f in files)
        {
            var m = MdtFile.Parse(File.ReadAllBytes(f));
            Assert.Equal(0x0302, m.Magic);
            Assert.Equal(0x10, m.Tracks[^1].Id);
            Assert.Equal(1, m.Tracks[^1].Length);
            Assert.True(m.VoiceCount > 0);
        }
        string mde = Path.Combine(folder!, "ZIGOKU.MDE");
        if (File.Exists(mde)) Assert.Equal(17, MdeFile.Parse(File.ReadAllBytes(mde)).Effects.Count);
    }
}
