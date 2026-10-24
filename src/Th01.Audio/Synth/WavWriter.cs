namespace Th01.Audio.Synth;

/// <summary>Minimal RIFF/WAVE writer for 16-bit stereo PCM (used to listen to the offline renders).</summary>
public static class WavWriter
{
    public static byte[] ToBytes(short[] interleavedStereo, int sampleRate = NoteSynth.SampleRate)
    {
        using var ms = new MemoryStream();
        using var w = new BinaryWriter(ms);
        int dataBytes = interleavedStereo.Length * 2;
        w.Write("RIFF"u8); w.Write(36 + dataBytes); w.Write("WAVE"u8);
        w.Write("fmt "u8); w.Write(16); w.Write((short)1); w.Write((short)2);
        w.Write(sampleRate); w.Write(sampleRate * 4); w.Write((short)4); w.Write((short)16);
        w.Write("data"u8); w.Write(dataBytes);
        foreach (short s in interleavedStereo) w.Write(s);
        w.Flush();
        return ms.ToArray();
    }

    public static void Save(string path, short[] interleavedStereo) => File.WriteAllBytes(path, ToBytes(interleavedStereo));
}
