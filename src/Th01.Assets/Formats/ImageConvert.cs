using Th01.Assets.Model;

namespace Th01.Assets.Formats;

/// <summary>Debug/tool helpers; the core never uses these.</summary>
public static class ImageConvert
{
    /// <summary>
    /// Converts to RGBA8888 (4 bytes per pixel). Uses the image palette, else <paramref name="fallback"/>, else grayscale.
    /// Alpha comes from <see cref="IndexedImageA.Alpha"/> when present, otherwise opaque.
    /// </summary>
    public static byte[] ToRgba(IndexedImage image, Palette4? fallback = null)
    {
        var pal = image.Palette ?? fallback;
        var alpha = (image as IndexedImageA)?.Alpha;
        byte[] rgba = new byte[image.Pixels.Length * 4];
        for (int i = 0; i < image.Pixels.Length; i++)
        {
            int idx = image.Pixels[i] & 0x0F;
            (byte r, byte g, byte b) = pal is null ? ((byte)(idx * 17), (byte)(idx * 17), (byte)(idx * 17)) : pal.ToRgb888(idx);
            rgba[i * 4] = r; rgba[i * 4 + 1] = g; rgba[i * 4 + 2] = b;
            rgba[i * 4 + 3] = alpha is null || alpha[i] != 0 ? (byte)255 : (byte)0;
        }
        return rgba;
    }

    /// <summary>Lays frames out left to right in a grid with <paramref name="columns"/> columns and a 1-pixel gap.</summary>
    public static (int Width, int Height, byte[] Rgba) Contact(SpriteSheet sheet, int columns, Palette4? fallback = null)
    {
        int n = sheet.Frames.Count, cols = Math.Min(columns, n), rows = (n + cols - 1) / cols;
        int w = cols * (sheet.FrameWidth + 1) + 1, h = rows * (sheet.FrameHeight + 1) + 1;
        byte[] sheetRgba = new byte[w * h * 4];
        for (int p = 0; p < w * h; p++) { sheetRgba[p * 4] = 96; sheetRgba[p * 4 + 1] = 0; sheetRgba[p * 4 + 2] = 96; sheetRgba[p * 4 + 3] = 255; }
        for (int f = 0; f < n; f++)
        {
            byte[] px = ToRgba(sheet.Frames[f], fallback);
            int ox = 1 + (f % cols) * (sheet.FrameWidth + 1), oy = 1 + (f / cols) * (sheet.FrameHeight + 1);
            for (int y = 0; y < sheet.FrameHeight; y++)
                for (int x = 0; x < sheet.FrameWidth; x++)
                {
                    int s = (y * sheet.FrameWidth + x) * 4, d = ((oy + y) * w + ox + x) * 4;
                    if (px[s + 3] == 0) continue; // transparent stays magenta-ish background
                    Array.Copy(px, s, sheetRgba, d, 4);
                }
        }
        return (w, h, sheetRgba);
    }
}
