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

/// <summary>One place in the code that loads the address of a string (<c>mov r16, imm16</c> or <c>push imm16</c>).</summary>
public sealed record StringRef(int CodeOffset, int DataOffset, TextRun Run);

/// <summary>
/// A story screen: the image the code loads and the text strings the code uses after it, until the next image.
/// Strings used before the first image of a program belong to no scene and are not listed.
/// </summary>
public sealed record CutsceneScene(string Image, int CodeOffset, IReadOnlyList<TextRun> Lines);
