namespace Th01.Assets.Model;

/// <summary>A run of Shift-JIS text found in an executable: byte offset, byte length and the decoded text.</summary>
public sealed record TextRun(int Offset, int Length, string Text);

/// <summary>
/// The dialogue/ending text of one executable (REIIDEN.EXE, OP.EXE, FUUIN.EXE) and the cutscene images
/// the story screens use. The game has no separate script files: text lives in the executables' data segment.
/// </summary>
public sealed class CutsceneText
{
    public string Source { get; }
    public IReadOnlyList<TextRun> Runs { get; }

    public CutsceneText(string source, IReadOnlyList<TextRun> runs)
    {
        Source = source;
        Runs = runs;
    }

    /// <summary>Runs with at least one non-ASCII (Japanese) character: the dialogue, as opposed to file names and format strings.</summary>
    public IEnumerable<TextRun> Japanese => Runs.Where(r => r.Text.Any(c => c > 0x7F));
}
