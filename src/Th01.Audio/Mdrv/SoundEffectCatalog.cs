namespace Th01.Audio.Mdrv;

/// <summary>
/// Where the Core calls <c>ISoundSink.PlaySe(id)</c> today (read from the callers; the original meaning of each
/// effect has not been confirmed by listening). id n plays effect n-1 of ZIGOKU.MDE (see <see cref="SoundEffectBank.ForSeId"/>).
/// </summary>
public static class SoundEffectCatalog
{
    public const string BankFile = "ZIGOKU.MDE";
    public const int EffectCount = 17;

    public static readonly IReadOnlyDictionary<int, string> UsedByCore = new Dictionary<int, string>
    {
        [1] = "player shot launched (Shots)",
        [2] = "stage object cleared: cards / point items spawn (StageObjects)",
        [3] = "boss hit by the orb (SinGyoku, Mima)",
        [4] = "route menu confirm (GameSession)",
        [5] = "life lost / boss phase change (GameSession, SinGyoku, Mima)",
        [6] = "missile launch / pellet volley (Mima)",
        [7] = "pellet from a corner / pillar fire / missile explode (Mima, Missiles)",
        [8] = "boss charge, bomb kuji-in (SinGyoku, Mima, Bomb)",
        [9] = "bomb pulse (Bomb)",
        [11] = "player special move / flip kick (Player)",
        [12] = "pillar circle appears (Mima)",
        [15] = "item picked up / extra life (Items, GameSession)",
        [16] = "player shot hits (Shots)",
        [17] = "stage timer ran out: pellets speed up (StageTimer)",
    };
}
