using System.Drawing;
using System.Drawing.Imaging;
using System.Runtime.InteropServices;

namespace CT320B.LabelDesigner.Core.Rendering;

/// <summary>How a colour/greyscale image is reduced to pure black &amp; white for the 1-bpp printer.</summary>
public enum ImageDither
{
    /// <summary>No reduction — draw the image as-is (the global print threshold still applies).</summary>
    None,
    /// <summary>Hard threshold at <c>Threshold</c>: each pixel is black or white.</summary>
    Threshold,
    /// <summary>Floyd–Steinberg error diffusion (best for photos/gradients).</summary>
    FloydSteinberg,
    /// <summary>Ordered 4×4 Bayer dithering (regular cross-hatch pattern).</summary>
    Ordered,
}

/// <summary>
/// Reduces a 32-bpp bitmap in place to pure black/white pixels using the chosen <see cref="ImageDither"/>.
/// Run at the output's dot resolution so the dots map 1:1 to printer dots; the result survives the
/// global monochrome threshold unchanged (pixels are already 0 or 255).
/// </summary>
public static class Dithering
{
    // 4×4 Bayer matrix, normalised to 0–255 thresholds.
    private static readonly int[,] Bayer4 =
    {
        {  0,  8,  2, 10 },
        { 12,  4, 14,  6 },
        {  3, 11,  1,  9 },
        { 15,  7, 13,  5 },
    };

    /// <summary>Applies <paramref name="mode"/> to <paramref name="bmp"/> in place. <paramref name="threshold"/>
    /// (0–255) is the black/white cut. <see cref="ImageDither.None"/> is a no-op.</summary>
    public static void Apply(Bitmap bmp, ImageDither mode, int threshold)
    {
        if (mode == ImageDither.None) return;
        threshold = Math.Clamp(threshold, 1, 254);

        int w = bmp.Width, h = bmp.Height;
        var rect = new Rectangle(0, 0, w, h);
        BitmapData data = bmp.LockBits(rect, ImageLockMode.ReadWrite, PixelFormat.Format32bppArgb);
        try
        {
            int stride = data.Stride;
            var px = new byte[stride * h];
            Marshal.Copy(data.Scan0, px, 0, px.Length);

            // Greyscale luminance per pixel (float so error diffusion can carry fractional error).
            var lum = new float[w * h];
            for (int y = 0; y < h; y++)
                for (int x = 0; x < w; x++)
                {
                    int i = y * stride + x * 4;
                    lum[y * w + x] = 0.299f * px[i + 2] + 0.587f * px[i + 1] + 0.114f * px[i];   // BGRA
                }

            for (int y = 0; y < h; y++)
                for (int x = 0; x < w; x++)
                {
                    float old = lum[y * w + x];
                    int cut = mode == ImageDither.Ordered ? OrderedCut(x, y, threshold) : threshold;
                    bool white = old >= cut;

                    if (mode == ImageDither.FloydSteinberg)
                    {
                        float err = old - (white ? 255f : 0f);
                        Diffuse(lum, w, h, x + 1, y, err * 7f / 16f);
                        Diffuse(lum, w, h, x - 1, y + 1, err * 3f / 16f);
                        Diffuse(lum, w, h, x, y + 1, err * 5f / 16f);
                        Diffuse(lum, w, h, x + 1, y + 1, err * 1f / 16f);
                    }

                    byte v = white ? (byte)255 : (byte)0;
                    int idx = y * stride + x * 4;
                    px[idx] = px[idx + 1] = px[idx + 2] = v;
                    px[idx + 3] = 255;   // opaque
                }

            Marshal.Copy(px, 0, data.Scan0, px.Length);
        }
        finally
        {
            bmp.UnlockBits(data);
        }
    }

    private static void Diffuse(float[] lum, int w, int h, int x, int y, float err)
    {
        if (x < 0 || x >= w || y < 0 || y >= h) return;
        lum[y * w + x] += err;
    }

    // Maps the Bayer cell (0–15) to a comparison cut centred on the threshold.
    private static int OrderedCut(int x, int y, int threshold)
    {
        // Spread the 16 Bayer levels across ±~ a band around the threshold.
        int level = Bayer4[y & 3, x & 3];                 // 0..15
        return Math.Clamp(threshold - 120 + level * 16, 1, 254);
    }
}
