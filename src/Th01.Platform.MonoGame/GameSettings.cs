using System.Text.Json;
using System.Text.Json.Serialization;
using Th01.Core.Game;

namespace Th01.Platform.MonoGame;

/// <summary>
/// The title screen's options (rank, music, starting lives). The original keeps them in REIIDEN.CFG, but our CFG reader is
/// read-only and a bad write would damage the user's file, so they live in settings.local.json (gitignored) instead.
/// ref: th01/formats/cfg.hpp, th01/op_01.cpp
/// </summary>
public sealed class GameSettings
{
    public const string FileName = "settings.local.json";
    public const int LivesExtraMax = 4; // the menu cycles 0..CFG_CREDIT_LIVES_EXTRA_MAX-1

    public Rank Rank { get; set; } = Rank.Normal;
    /// <summary>Credit lives on top of the base 2 (default 2 => 4 lives).</summary>
    public int LivesExtra { get; set; } = 2;
    /// <summary>Starting bombs (credit_bombs). The original menu has no control for it; edit the file to change it.</summary>
    public int Bombs { get; set; } = 1;
    /// <summary>BGM on/off. Stored now; there is no sound output yet.</summary>
    public bool Music { get; set; } = true;

    sealed record Model(
        [property: JsonPropertyName("rank")] int Rank,
        [property: JsonPropertyName("livesExtra")] int LivesExtra,
        [property: JsonPropertyName("bombs")] int Bombs,
        [property: JsonPropertyName("music")] bool Music);

    /// <summary>
    /// The player's settings. Without a settings.local.json yet they come from the player's own REIIDEN.CFG in the game folder (read only,
    /// never written), so the launcher and the title menu start from what the original game had; after that the title menu keeps them here.
    /// </summary>
    public static GameSettings Load(string? path = null, string? gameFolder = null)
    {
        path ??= Path.Combine(Directory.GetCurrentDirectory(), FileName);
        var s = new GameSettings();
        try
        {
            if (!File.Exists(path)) { SeedFromCfg(s, gameFolder); return s; }
            var m = JsonSerializer.Deserialize<Model>(File.ReadAllText(path));
            if (m is null) return s;
            s.Rank = (Rank)Math.Clamp(m.Rank, 0, 3);
            s.LivesExtra = Math.Clamp(m.LivesExtra, 0, LivesExtraMax);
            s.Bombs = Math.Clamp(m.Bombs, 0, 9);
            s.Music = m.Music;
        }
        catch (Exception e) when (e is JsonException or IOException or UnauthorizedAccessException) { /* keep defaults */ }
        return s;
    }

    static void SeedFromCfg(GameSettings s, string? gameFolder)
    {
        if (gameFolder is null) return;
        try
        {
            string cfg = Path.Combine(gameFolder, "REIIDEN.CFG");
            if (!File.Exists(cfg)) return;
            var o = Th01.Assets.Formats.ReiidenCfg.Load(File.ReadAllBytes(cfg));
            s.Rank = (Rank)Math.Clamp((int)o.Rank, 0, 3);
            s.Music = o.BgmMode == 1;
            s.Bombs = Math.Clamp((int)o.CreditBombs, 0, 9);
            s.LivesExtra = Math.Clamp((int)o.CreditLivesExtra, 0, LivesExtraMax);
        }
        catch (Exception e) when (e is InvalidDataException or IOException or UnauthorizedAccessException) { /* keep the defaults */ }
    }

    public void Save(string? path = null)
    {
        path ??= Path.Combine(Directory.GetCurrentDirectory(), FileName);
        File.WriteAllText(path, JsonSerializer.Serialize(new Model((int)Rank, LivesExtra, Bombs, Music)));
    }
}
