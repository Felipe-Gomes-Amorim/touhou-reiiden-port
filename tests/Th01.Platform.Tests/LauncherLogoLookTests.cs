using Th01.Assets;
using Th01.Assets.Formats;
using Th01.Platform.MonoGame.Launcher;

namespace Th01.Platform.Tests;

/// <summary>Looking at the real title picture (needs the player's files and LAUNCHER_DUMP; writes only to that local folder).</summary>
public class LauncherLogoLookTests
{
    [SkippableFact]
    public void Dump_RealTitlePicture_ForLooking()
    {
        string? dir = Environment.GetEnvironmentVariable("LAUNCHER_DUMP");
        string? data = Environment.GetEnvironmentVariable("TH01_DATA_PATH");
        Skip.If(string.IsNullOrEmpty(dir) || string.IsNullOrEmpty(data), "no LAUNCHER_DUMP / TH01_DATA_PATH");
        var assets = new FolderAssetSource(data!);
        Skip.IfNot(assets.Exists("REIIDEN3.GRP") && assets.Exists("REIIDEN2.GRP"), "no title pictures");
        using var s3 = assets.Open("REIIDEN3.GRP");
        using var s2 = assets.Open("REIIDEN2.GRP");
        var top = GrpImage.Load(s3);
        var baseImg = GrpImage.Load(s2);
        Directory.CreateDirectory(dir!);
        foreach (var (img, pal, name) in new[] { (top, top.Palette, "own"), (top, baseImg.Palette, "base") })
        {
            var c = new LauncherCanvas();
            c.Pixels.AsSpan().Fill(0xFF303030);
            for (int y = 0; y < img.Height; y++)
                for (int x = 0; x < img.Width; x++)
                {
                    var (r, g, b) = (pal ?? baseImg.Palette!).ToRgb888(img.Pixels[y * img.Width + x]);
                    c.FillRect(x * 2, y * 2 - 40, 2, 2, 0xFF000000u | (uint)(r << 16) | (uint)(g << 8) | b);
                }
            LauncherTests.WritePng(Path.Combine(dir!, $"p6-reiiden3-{name}.png"), c);
        }
        // Colour counts, to know which index is the kanji and which the spiral.
        var counts = new int[16];
        foreach (byte p in top.Pixels) counts[p]++;
        Console.WriteLine("REIIDEN3 index counts: " + string.Join(" ", counts.Select((n, i) => $"{i}:{n}")));
        Console.WriteLine($"REIIDEN3 {top.Width}x{top.Height}, palette own: {top.Palette is not null}");
    }
}
