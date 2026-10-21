namespace Th01.Core.Audio;

/// <summary>
/// Where gameplay sends audio requests. Implemented by the platform (silent by default).
/// Ids are the original track/effect indices; the mapping lives in docs/RECON.md once T2/T3 document it.
/// STABLE: change only via T1 (docs/TASKS.md).
/// </summary>
public interface ISoundSink
{
    void PlayBgm(string trackFileName);
    void StopBgm();
    void FadeOutBgm();
    void PlaySe(int effectId);
}

public sealed class NullSoundSink : ISoundSink
{
    public static readonly NullSoundSink Instance = new();
    public void PlayBgm(string trackFileName) { }
    public void StopBgm() { }
    public void FadeOutBgm() { }
    public void PlaySe(int effectId) { }
}
