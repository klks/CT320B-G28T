using System.Drawing;
using System.Drawing.Imaging;
using System.Runtime.InteropServices;

namespace CT320B.UsbApi.Imaging;

/// <summary>A 1-bpp monochrome raster: packed bits plus the dimensions of the source image.</summary>
/// <param name="Width">Source pixel width.</param>
/// <param name="Height">Source pixel height (== row count).</param>
/// <param name="Stride">Bytes per row (row width in the packed data).</param>
/// <param name="Data">Packed bits, <c>Height * Stride</c> bytes, row-major, MSB-first.</param>
public sealed record MonochromeRaster(int Width, int Height, int Stride, byte[] Data)
{
    /// <summary>Row width in bytes — the value emitted in the TSPL <c>BITMAP</c> width field.</summary>
    public int WidthBytes => Stride;
}

/// <summary>
/// Converts an image to the printer's 1-bpp monochrome raster, a faithful port of the DLL's
/// <c>bmp2Bytes</c> / <c>USBDeviceService::Bmp2Bytes</c> (and the inline packing in
/// <c>TscPrintBitmap</c>):
/// <list type="bullet">
/// <item>grayscale = (R + G + B) / 3 (integer);</item>
/// <item>a pixel is "dark" when gray &lt; 128 and sets its bit (1 = print dot);</item>
/// <item>bits are packed MSB-first: pixel x → byte <c>x/8</c>, bit <c>7 - (x%8)</c>;</item>
/// <item>a fully-zero ARGB pixel (transparent black) is skipped (treated as light).</item>
/// </list>
/// </summary>
public static class MonochromeRasterizer
{
    /// <summary>Grayscale threshold: pixels with gray &lt; 128 are dark (bit set). Confirmed in asm.</summary>
    public const int Threshold = 128;

    /// <summary>Minimal row stride in bytes: <c>ceil(width/8)</c>.</summary>
    public static int StrideBytes(int width) => (width + 7) >> 3;

    /// <summary>
    /// DWORD-aligned row stride <c>((width + 31) &gt;&gt; 3) &amp; ~3</c> — what <c>TscPrintBitmap</c>
    /// uses (ceil(width/8) rounded up to a 4-byte boundary).
    /// </summary>
    public static int StrideDwordAligned(int width) => ((width + 31) >> 3) & ~3;

    /// <summary>
    /// Packs row-major ARGB pixels (0xAARRGGBB) into 1-bpp MSB-first bytes. Pure and allocation-
    /// only-for-output, so it can be unit-tested against hand-computed bytes.
    /// </summary>
    /// <param name="argb">Row-major pixels, length ≥ width*height.</param>
    public static byte[] Pack(ReadOnlySpan<int> argb, int width, int height, int stride)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(width);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(height);
        ArgumentOutOfRangeException.ThrowIfLessThan(stride, StrideBytes(width));
        if (argb.Length < width * height)
            throw new ArgumentException("argb is smaller than width*height.", nameof(argb));

        var data = new byte[height * stride];
        for (int y = 0; y < height; y++)
        {
            int rowBase = y * stride;
            int rowStart = y * width;
            for (int x = 0; x < width; x++)
            {
                int color = argb[rowStart + x];
                if (color == 0) continue;                          // transparent → light
                int gray = (((color >> 16) & 0xFF) + ((color >> 8) & 0xFF) + (color & 0xFF)) / 3;
                if (gray >= Threshold) continue;                   // light → bit stays 0
                data[rowBase + (x >> 3)] |= (byte)(1 << (7 - (x & 7)));   // dark → set (MSB-first)
            }
        }
        return data;
    }

    /// <summary>
    /// Rasterizes a bitmap. Stride defaults to <see cref="StrideDwordAligned"/> (the
    /// <c>TscPrintBitmap</c> convention); pass <paramref name="stride"/> to override per command.
    /// </summary>
    public static MonochromeRaster Rasterize(Bitmap bitmap, int? stride = null)
    {
        ArgumentNullException.ThrowIfNull(bitmap);
        int width = bitmap.Width, height = bitmap.Height;
        int rowStride = stride ?? StrideDwordAligned(width);
        int[] argb = ExtractArgb(bitmap, width, height);
        return new MonochromeRaster(width, height, rowStride, Pack(argb, width, height, rowStride));
    }

    /// <summary>Loads a bitmap from a file and rasterizes it.</summary>
    public static MonochromeRaster RasterizeFile(string path, int? stride = null)
    {
        using var bmp = new Bitmap(path);
        return Rasterize(bmp, stride);
    }

    /// <summary>Extracts pixels as row-major 0xAARRGGBB ints (matches GdipBitmapGetPixel).</summary>
    private static int[] ExtractArgb(Bitmap bitmap, int width, int height)
    {
        var rect = new Rectangle(0, 0, width, height);
        BitmapData bd = bitmap.LockBits(rect, ImageLockMode.ReadOnly, PixelFormat.Format32bppArgb);
        try
        {
            var argb = new int[width * height];
            // Format32bppArgb in memory is BGRA; an int read little-endian is 0xAARRGGBB.
            for (int y = 0; y < height; y++)
                Marshal.Copy(bd.Scan0 + y * bd.Stride, argb, y * width, width);
            return argb;
        }
        finally
        {
            bitmap.UnlockBits(bd);
        }
    }
}
