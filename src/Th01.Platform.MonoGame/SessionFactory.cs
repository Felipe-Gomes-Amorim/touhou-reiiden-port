using Th01.Assets;
using Th01.Assets.Formats;
using Th01.Assets.Model;
using Th01.Core.Audio;
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
    readonly IFontSource? _font;
    readonly MissingArtLog? _log;
    readonly ISoundSink? _sound;

    /// <param name="stageId">0-based stage across the whole game (stage_num - 1).</param>
    /// <param name="route">0 = Makai, 1 = Jigoku.</param>
    public SessionFactory(IAssetSource assets, int stageId = 0, int route = 0, IFontSource? font = null, MissingArtLog? log = null, ISoundSink? sound = null)
    {
        if (stageId < 0 || stageId >= 4 * StageSetup.StagesPerScene) throw new ArgumentOutOfRangeException(nameof(stageId), "Stage must be 0..19.");
        if (route is < 0 or > 1) throw new ArgumentOutOfRangeException(nameof(route), "Route must be 0 (Makai) or 1 (Jigoku).");
        _assets = assets;
        _font = font;
        _sound = sound;
        _log = log;
        _stageId = stageId;
        _route = route;
    }

    /// <summary>
    /// STAGE?.DAT / ST?.GRP number for a stage. ref: th01/main_01.cpp (scene_id): stages 0-4 use scene 0, then
    /// one scene per 5 stages per route (1/2, 3/4, 5/6).
    /// </summary>
    public static int SceneId(int stageId, int route) => stageId < StageSetup.StagesPerScene ? 0 : (stageId / StageSetup.StagesPerScene) * 2 - 1 + route;

    /// <summary>
    /// Background picture of a stage. Boss stages have their own: stage 5 BOSS1, stage 10 BOSS2 (Makai) / BOSS3 (Jigoku, Mima),
    /// stage 15 BOSS4 (Jigoku) / BOSS5 (Makai). The stage 20 bosses draw several pictures themselves and are not ported, so they
    /// keep the scene's ST background. ref: th01/main_01.cpp (boss stage switch)
    /// </summary>
    public static string BackgroundFile(int stageId, int route, int scene) => stageId switch
    {
        StageSetup.BossStage => "BOSS1.GRP",
        2 * StageSetup.StagesPerScene - 1 => route == 0 ? "BOSS2.GRP" : "BOSS3.GRP",
        3 * StageSetup.StagesPerScene - 1 => route == 1 ? "BOSS4.GRP" : "BOSS5.GRP",
        _ => $"ST{scene}.GRP",
    };

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
        var art = new GameAssetArt(_log);
        RegisterSheets(art);
        RegisterEmbedded(art);
        RegisterHudMask(art);
        string grp = BackgroundFile(_stageId, _route, scene);
        if (_assets.Exists(grp)) art.SetBackground(Load(grp, GrpImage.Load));
        // frame_rand is the title screen's frame counter in the original; callers pass a deterministic value.
        var session = new GameSession(makeOptions(setup), sound: _sound, art: art, font: _font);
        return new GameStart(session, art);
    }

    /// <summary>
    /// Every .PTN / .BOS / .GRC in the archive, loaded on first use under its exact file name (the names GameRenderer asks for:
    /// STG.PTN, MIKO.PTN, NUMB.PTN, MIKO_AC.BOS, MIKO_AC2.BOS, BOSS1*.BOS ...). docs/FORMATS.md has the role of each.
    /// </summary>
    void RegisterSheets(GameAssetArt art)
    {
        if (!_assets.Exists(ExpectedFiles.ArchiveName)) return;
        var games = new GameAssets(_assets);
        foreach (string name in games.PackNames)
        {
            if (name.EndsWith(".PTN", StringComparison.OrdinalIgnoreCase)) art.AddLazySheet(name, () => games.GetPtn(name));
            else if (name.EndsWith(".BOS", StringComparison.OrdinalIgnoreCase)) art.AddLazySheet(name, () => games.GetBos(name));
            else if (name.EndsWith(".GRC", StringComparison.OrdinalIgnoreCase)) art.AddLazySheet(name, () => games.GetGrc(name));
        }
    }

    /// <summary>Names of the monochrome sheets compiled into REIIDEN.EXE that the art exposes (see <see cref="IGameArt"/>).</summary>
    public static readonly string[] EmbeddedSheetNames = ["pellet", "pellet_c", "laser_s", "bonusbox", "pillar", "shape8x8", "leaf_s", "leaf_l", "leaf_r", "ileave_m", "mousecur"];

    /// <summary>
    /// The sprites the original keeps inside its executable are found in the user's REIIDEN.EXE the first time they are asked for.
    /// Without the executable they stay missing and the renderer keeps its plain shapes.
    /// </summary>
    /// <summary>The HUD frame and captions (MASK.GRF, 640x66, monochrome) as the one-frame sheet "mask".</summary>
    void RegisterHudMask(GameAssetArt art)
    {
        if (!_assets.Exists("MASK.GRF")) return;
        var games = new GameAssets(_assets);
        art.AddLazySheet("mask", () => new SpriteSheet([games.GetHudMask()]));
    }

    void RegisterEmbedded(GameAssetArt art)
    {
        if (!_assets.Exists("REIIDEN.EXE")) return;
        var games = new GameAssets(_assets);
        foreach (string name in EmbeddedSheetNames)
        {
            art.AddLazySheet(name, () =>
            {
                var e = games.GetEmbeddedSprites();
                if (!e.Has(name)) throw new FileNotFoundException($"{name} was not found inside REIIDEN.EXE.");
                var sheet = e.Get(name);
                return name == "pellet" ? PelletCells(sheet) : sheet;
            });
        }
    }

    /// <summary>
    /// The pellet table is 3 cells x 8 horizontal pre-shifts of 16x8 (cell-major) because the original draws on byte boundaries.
    /// We position per pixel, so keep only shift 0 of each cell: the 8x8 picture sits in the left 8 columns.
    /// </summary>
    public static SpriteSheet PelletCells(SpriteSheet raw)
    {
        const int Cells = 3, Shifts = 8;
        if (raw.Frames.Count != Cells * Shifts || raw.FrameWidth != 16 || raw.FrameHeight != 8)
            throw new InvalidDataException("The pellet table does not have the expected 3 cells x 8 shifts of 16x8.");
        var frames = new List<IndexedImageA>(Cells);
        for (int c = 0; c < Cells; c++)
        {
            var src = raw.Frames[c * Shifts];
            var px = new byte[8 * 8];
            for (int y = 0; y < 8; y++) Array.Copy(src.Pixels, y * 16, px, y * 8, 8);
            frames.Add(new IndexedImageA(8, 8, px, (byte[])px.Clone()));
        }
        return new SpriteSheet(frames);
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

public sealed class AssetStageProvider(IAssetSource assets, IFontSource? font = null, MissingArtLog? log = null, ISoundSink? sound = null) : IStageProvider
{
    public GameStart Create(int stageId, int route, Func<StageSetup, Th01.Core.Game.GameOptions> makeOptions) =>
        new SessionFactory(assets, stageId, route, font, log, sound).Create(makeOptions);
}
