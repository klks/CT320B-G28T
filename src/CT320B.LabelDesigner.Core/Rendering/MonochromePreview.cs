using System.Drawing;
using System.Drawing.Imaging;
using System.Runtime.InteropServices;
using CT320B.LabelDesigner.Core.Model;
using CT320B.UsbApi.Imaging;

namespace CT320B.LabelDesigner.Core.Rendering;

/// <summary>
/// Turns a printer <see cref="MonochromeRaster"/> back into a viewable black/white
/// <see cref="Bitmap"/> — exactly the dots the printer will mark. A set bit (print dot) becomes a
/// black pixel; a clear bit becomes white. This is the "print preview" image: it shows the result of
/// the 1-bpp threshold (and any clipping/scaling) that <c>PrintImageLabel</c> sends, so what the user
/// previews is byte-for-byte what prints.
/// </summary>
public static class MonochromePreview
{
    private const int Black = unchecked((int)0xFF000000);
    private const int White = unchecked((int)0xFFFFFFFF);

    /// <summary>Renders a 1-bpp raster as a 32-bpp black/white <see cref="Bitmap"/> at 1 dot = 1 pixel.</summary>
    public static Bitmap ToBitmap(MonochromeRaster raster)
    {
        ArgumentNullException.ThrowIfNull(raster);

        int w = raster.Width, h = raster.Height;
        var bmp = new Bitmap(w, h, PixelFormat.Format32bppArgb);
        bmp.SetResolution((float)Units.Dpi, (float)Units.Dpi);

        var rect = new Rectangle(0, 0, w, h);
        BitmapData bd = bmp.LockBits(rect, ImageLockMode.WriteOnly, PixelFormat.Format32bppArgb);
        try
        {
            var row = new int[w];
            for (int y = 0; y < h; y++)
            {
                int rowBase = y * raster.Stride;
                for (int x = 0; x < w; x++)
                {
                    bool dot = (raster.Data[rowBase + (x >> 3)] & (1 << (7 - (x & 7)))) != 0;
                    row[x] = dot ? Black : White;
                }
                Marshal.Copy(row, 0, bd.Scan0 + y * bd.Stride, w);
            }
        }
        finally
        {
            bmp.UnlockBits(bd);
        }
        return bmp;
    }
}
