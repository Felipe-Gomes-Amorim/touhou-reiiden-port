using Th01.Assets;
using Th01.Assets.Formats;
using Th01.Assets.Model;
using Th01.Core.Game;
using Th01.Core.Graphics;
using Th01.Core.Sim;

namespace Th01.Platform.MonoGame;

/// <summary>A running game plus the art that goes with it.</summary>
public sealed record GameStart(IGameSession Session, IGameArt Art);

/// <summary>
/// Builds a <see cref="GameSession"/> for one card stage from the user's game files. Failures carry a message meant
/// for the player (what file is missing and where we looked), not a stack trace.
/// </summary>
public sealed class SessionFactory
{
    readonly IAssetSource _assets;
    readonly int _stageId;
    readonly int _route;

    /// <param name="stageId">0-based stage across the whole game (stage_num - 1).</param>
    /// <param name="route">0 = Makai, 1 = Jigoku.</param>
    public SessionFactory(IAssetSource assets, int stageId = 0, int route = 0)
    {
        if (stageId < 0 || stageId >= 4 * StageSetup.StagesPerScene) throw new ArgumentOutOfRangeException(nameof(stageId), "Stage must be 0..19.");
        if (route is < 0 or > 1) throw new ArgumentOutOfRangeException(nameof(route), "Route must be 0 (Makai) or 1 (Jigoku).");
        _assets = assets;
        _stageId = stageId;
        _route = route;
    }

    /// <summary>
    /// STAGE?.DAT / ST?.GRP number for a stage. ref: th01/main_01.cpp (scene_id): stages 0-4 use scene 0, then
    /// one scene per 5 stages per route (1/2, 3/4, 5/6).
    /// </summary>
    public static int SceneId(int stageId, int route) => stageId < StageSetup.StagesPerScene ? 0 : (stageId / StageSetup.StagesPerScene) * 2 - 1 + route;

    /// <summary>Core's StageSetup from the Assets model (raw bytes copied, nothing normalised).</summary>
    public static StageSetup ToStageSetup(StageLayout layout, int stageId, int route)
    {
        var cards = new bool[StageSetup.Rows, StageSetup.Cols];
        var obstacles = new byte[StageSetup.Rows, StageSetup.Cols];
        Array.Copy(layout.Cards, cards, cards.Length);
        Array.Copy(layout.Obstacles, obstacles, obstacles.Length);
        return new StageSetup { StageId = stageId, Route = route, Cards = cards, Obstacles = obstacles };
    }

    public GameStart Create() => Create(setup => new Th01.Core.Game.GameOptions { Stage = setup, FrameRand = 0 });

    /// <summary>Creates the stage's session with options built from its <see cref="StageSetup"/> (so the caller can add carry-over, rank...).</summary>
    public GameStart Create(Func<StageSetup, Th01.Core.Game.GameOptions> makeOptions)
    {
        int scene = SceneId(_stageId, _route);
        var layouts = LoadStageDat(scene);
        int inScene = _stageId % StageSetup.StagesPerScene;
        if (inScene >= layouts.Count)
            throw new InvalidOperationException($"STAGE{scene}.DAT has {layouts.Count} stage(s) but stage {_stageId + 1} was asked for.");

        var setup = ToStageSetup(layouts[inScene], _stageId, _route);
        var art = new GameAssetArt(SheetLoader());
        // The original loads the boss's own picture on boss stages (its palette matches the boss sprites) and
        // ST<scene>.GRP otherwise. Only the bosses ported so far are listed; the others fall back to ST<scene>.GRP.
        string grp = _stageId == StageSetup.BossStage ? "BOSS1.GRP"
            : _stageId == StageSetup.MimaStage && _route == StageSetup.RouteJigoku ? "BOSS3.GRP"
            : $"ST{scene}.GRP";
        if (_assets.Exists(grp)) art.SetBackground(Load(grp, GrpImage.Load));
        // frame_rand is the title screen's frame counter in the original; callers pass a deterministic value.
        var session = new GameSession(makeOptions(setup), art: art);
        return new GameStart(session, art);
    }

    /// <summary>
    /// Loads .PTN/.BOS/.GRC sheets from the game archive on first use. A missing or unreadable sheet is just "not
    /// available": the renderer falls back to flat boxes for it.
    /// </summary>
    Func<string, SpriteSheet?>? SheetLoader()
    {
        GameAssets pack;
        try { pack = new GameAssets(_assets); }
        catch (Exception e) when (e is FileNotFoundException or InvalidDataException) { return null; }
        return name =>
        {
            try
            {
                return Path.GetExtension(name).ToUpperInvariant() switch
                {
                    ".PTN" => pack.GetPtn(name),
                    ".BOS" => pack.GetBos(name),
                    ".GRC" => pack.GetGrc(name),
                    ".GRF" => name.Equals("MASK.GRF", StringComparison.OrdinalIgnoreCase) ? new SpriteSheet([pack.GetHudMask()]) : null,
                    // No extension: a monochrome table embedded in REIIDEN.EXE (EmbeddedSprites.KnownNames: "pellet", "laser_s"...).
                    "" => pack.GetEmbeddedSprites() is { } emb && emb.Has(name) ? emb.Get(name) : null,
                    _ => null,
                };
            }
            catch (Exception e) when (e is FileNotFoundException or InvalidDataException or EndOfStreamException or KeyNotFoundException) { return null; }
        };
    }

    IReadOnlyList<StageLayout> LoadStageDat(int scene)
    {
        string name = $"STAGE{scene}.DAT";
        byte[] data;
        if (_assets.Exists(name)) data = Load(name, s => { using var ms = new MemoryStream(); s.CopyTo(ms); return ms.ToArray(); });
        else if (_assets.Exists(ExpectedFiles.ArchiveName))
        {
            using var s = _assets.Open(ExpectedFiles.ArchiveName);
            try { data = PackFile.Read(s, name); }
            catch (FileNotFoundException) { throw new FileNotFoundException($"'{name}' is not in '{ExpectedFiles.ArchiveName}'. Is the data folder from a different version?"); }
        }
        else throw new FileNotFoundException($"Neither {name} nor the archive '{ExpectedFiles.ArchiveName}' was found in the game data folder.");
        return StageDat.Load(data);
    }

    T Load<T>(string name, Func<Stream, T> read)
    {
        try { using var s = _assets.Open(name); return read(s); }
        catch (Exception e) when (e is InvalidDataException or EndOfStreamException)
        { throw new InvalidDataException($"{name} could not be read: {e.Message}", e); }
    }
}

/// <summary>Creates the session of any stage; the title/flow code asks for stage N with whatever options it needs.</summary>
public interface IStageProvider
{
    GameStart Create(int stageId, int route, Func<StageSetup, Th01.Core.Game.GameOptions> makeOptions);
}

public sealed class AssetStageProvider(IAssetSource assets) : IStageProvider
{
    public GameStart Create(int stageId, int route, Func<StageSetup, Th01.Core.Game.GameOptions> makeOptions) =>
        new SessionFactory(assets, stageId, route).Create(makeOptions);
}
