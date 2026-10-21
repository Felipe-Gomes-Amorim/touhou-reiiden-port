namespace Th01.Core.Game;

/// <summary>Route column of a high score entry. The file stores a Shift-JIS kanji; the adapter maps it.</summary>
public enum ScoreRoute : byte { None, Shrine, Makai, Jigoku, Clear }

/// <summary>One line of the REYHI*.DAT table (names are the 8 kanji codes, in screen order).</summary>
public sealed class HiScoreEntry
{
    public ushort[] Name = new ushort[HiScoreTable.NameKanji];
    public int Score;
    /// <summary>Stage reached (1..), or a SCOREDAT_CLEARED* constant (40+).</summary>
    public int Stage;
    public ScoreRoute Route;
}

/// <summary>
/// The per-difficulty high score list and its insertion rule. Pure data: loading and saving the file belongs to the
/// asset layer, the name entry screen to the platform. ref: th01/hiscore/regist.cpp, th01/formats/scoredat.hpp
/// </summary>
public sealed class HiScoreTable
{
    public const int Places = 10;
    public const int NameKanji = 8;
    public const int Cleared = 40;
    public const int ClearedMakai = Cleared + 10;
    public const int ClearedJigoku = Cleared + 20;

    public readonly HiScoreEntry[] Entries = new HiScoreEntry[Places];

    public HiScoreTable()
    {
        for (int i = 0; i < Places; i++) Entries[i] = new HiScoreEntry();
    }

    /// <summary>regist_menu(): the stage argument is "cleared" if it is at least SCOREDAT_CLEARED - 10.</summary>
    public static bool IsClearedStage(int stageOrConstant) => stageOrConstant >= Cleared - 10;

    /// <summary>First place whose score the new one equals or beats (ties go above the old entry); Places if none.</summary>
    public int FindPlace(int score)
    {
        for (int place = 0; place < Places; place++)
            if (score >= Entries[place].Score) return place;
        return Places;
    }

    /// <summary>Shifts the lower entries down (the last one drops off) and writes the new one with the given name.</summary>
    public void Insert(int place, ushort[] name, int score, int stage, ScoreRoute route)
    {
        if (place < 0 || place >= Places) throw new ArgumentOutOfRangeException(nameof(place));
        for (int i = Places - 1; i > place; i--)
        {
            Entries[i].Name = (ushort[])Entries[i - 1].Name.Clone();
            Entries[i].Score = Entries[i - 1].Score;
            Entries[i].Stage = Entries[i - 1].Stage;
            Entries[i].Route = Entries[i - 1].Route;
        }
        var e = Entries[place];
        e.Name = (ushort[])name.Clone();
        e.Score = score;
        e.Stage = stage;
        e.Route = route;
    }
}

/// <summary>
/// State of the name entry screen (cursor and name), without the on-screen alphabet grid. In the Game Over variant
/// the original force-confirms after more than 1000 handled inputs.
/// </summary>
public sealed class NameEntry
{
    public const int TimeoutInputs = 1000;
    /// <summary>Blank name: the original fills the buffer with ASCII spaces.</summary>
    public const ushort Blank = 0x2020;

    public readonly ushort[] Name = new ushort[HiScoreTable.NameKanji];
    public int Cursor { get; private set; }
    public int HandledInputs { get; private set; }
    public bool Confirmed { get; private set; }
    readonly bool _cleared;

    public NameEntry(bool cleared)
    {
        _cleared = cleared;
        Array.Fill(Name, Blank);
    }

    /// <summary>True once the Game Over variant has seen more than 1000 inputs (never for a cleared game).</summary>
    public bool TimedOut => !_cleared && HandledInputs > TimeoutInputs;

    /// <summary>Types a kanji at the cursor. Returns true when the cursor was already on the last cell.</summary>
    public bool Put(ushort kanji)
    {
        HandledInputs++;
        Name[Cursor] = kanji;
        if (Cursor < HiScoreTable.NameKanji - 1)
        {
            Cursor++;
            return false;
        }
        return true;
    }

    public void CursorLeft()
    {
        HandledInputs++;
        if (Cursor > 0) Cursor--;
    }

    public void CursorRight()
    {
        HandledInputs++;
        if (Cursor < HiScoreTable.NameKanji - 1) Cursor++;
    }

    public void Confirm()
    {
        HandledInputs++;
        Confirmed = true;
    }
}

/// <summary>What the Game Over / ranking screens need. ref: th01/main_01.cpp (after the stage loop)</summary>
public sealed record GameOverResult(uint Score, int StageNum, ScoreRoute Route, Rank Rank, int PelletSpeed, uint FrameRand)
{
    /// <summary>Stage argument for regist_menu(): the stage the player died on.</summary>
    public int RegistStage => StageNum;

    /// <summary>
    /// Options of the game the continue menu restarts, on the same stage: score back to 0, lives
    /// credit_lives_extra + 2, one bomb, point value 0, pellet speed lowered with (max -2, delta -5).
    /// The restart is a fresh process in the original, so the card flip cycle is drawn again.
    /// </summary>
    public GameOptions ContinueOptions(StageSetup stage, int creditLivesExtra, bool entrance = false)
    {
        int speed = PelletSpeed > -2 ? -2 : PelletSpeed - 5;
        if (speed < Pellets.SpeedLowerMin) speed = Pellets.SpeedLowerMin;
        return new GameOptions
        {
            FrameRand = FrameRand, Rank = Rank, Lives = creditLivesExtra + 2, Bombs = 1, Score = 0, PointValue = 0,
            PelletSpeed = speed, Stage = stage, Entrance = entrance,
        };
    }
}
