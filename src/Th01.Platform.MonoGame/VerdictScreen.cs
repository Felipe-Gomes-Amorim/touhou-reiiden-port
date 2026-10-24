using Th01.Assets.Model;
using Th01.Assets.Text;
using Th01.Core.Graphics;

namespace Th01.Platform.MonoGame;

/// <summary>
/// The verdict screen that ends the game (ENDM_A): game title and version, rank, scores, continues per scene and in total, then the
/// thank-you line. Layout and delays follow ReC98 th01/end/end.cpp verdict_animate_and_regist_menu; the texts are the user's own
/// (read from FUUIN.EXE at run time), only filled with the values of the finished run.
/// </summary>
public static class VerdictScreen
{
    const int Left = 296, LineHeight = 32, LineDelay = 30;

    /// <summary>The assessment title ("you are ...") of the original needs its title table, which is not read yet; only its heading is shown.</summary>
    public static IReadOnlyList<SlideStep> Steps(VerdictStrings v, string rankName, uint score, uint highest, IReadOnlyList<int> continues, int route, IFontSource font)
    {
        int Width(string s) => TextRenderer.Measure(font, s);
        int Y(int line) => line * LineHeight;

        string rankPrefix = PrintfLite.Format(v.Rank, "");
        int right = Left + Width(rankPrefix + "LUNATIC") - Width("  ");
        int versionLeft = right - Width(v.Version);
        int verLeft = versionLeft - Width(v.Ver + " ");
        int titleLeft = verLeft - Width(v.Title + "  ");

        int total = continues.Sum();
        var makai = route == 0;
        var s = new List<SlideStep>
        {
            new ToneTo(-15, Cutscenes.FadeTicks(10)), new LoadPics("ENDM_A.GRP"), new ShowFull(), new ToneTo(0, Cutscenes.FadeTicks(8)),
            new PutText(titleLeft, Y(1), v.Title), new PutText(verLeft, Y(1), v.Ver), new PutText(versionLeft, Y(1), v.Version), new Wait(LineDelay),
        };
        void Line(int line, string text) { s.Add(new PutText(Left, Y(line), text)); s.Add(new Wait(LineDelay)); }

        Line(2, PrintfLite.Format(v.Rank, Centered(rankName, 7)));
        // The code prints two score lines. The original's first one is the "highest" and the second the final score, but the
        // English translation put its labels the other way round, so the label decides which value goes where.
        bool firstIsHigh = IsHighLabel(v.HighScore) || !IsHighLabel(v.Score);
        Line(3, PrintfLite.Format(v.HighScore, firstIsHigh ? highest : score));
        Line(4, PrintfLite.Format(v.Score, firstIsHigh ? score : highest));
        Line(5, v.ContinuesLabel);
        Line(6, PrintfLite.Format(v.Shrine, continues[0]));
        var scenes = makai ? v.Makai : v.Jigoku;
        for (int i = 0; i < 3; i++) Line(7 + i, PrintfLite.Format(scenes[i], continues[i + 1]));
        Line(10, PrintfLite.Format(makai ? v.MakaiTotal : v.JigokuTotal, total));
        s.Add(new PutText(Left, Y(11), v.ThankYou));
        s.Add(new PutText(Left - Width("    "), Y(12), v.Assessment));
        // 50 + 100 ticks, then the original waits for OK/shot for at most 2000 ticks; a button press ends the slideshow.
        s.Add(new Wait(50 + 100 + 2000));
        return s;
    }

    /// <summary>True for a "Hi Score" / "High Score" label, or the Japanese "最高" (highest).</summary>
    static bool IsHighLabel(string format) => format.Contains("Hi", StringComparison.OrdinalIgnoreCase) || format.Contains('最') && format.Contains('高');

    static string Centered(string text, int width) => text.PadLeft((width + text.Length) / 2).PadRight(width);
}
