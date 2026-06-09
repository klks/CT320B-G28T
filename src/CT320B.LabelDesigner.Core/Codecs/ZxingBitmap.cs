using System.Drawing;
using System.Drawing.Imaging;
using System.Runtime.InteropServices;
using ZXing.Rendering;

namespace CT320B.LabelDesigner.Core.Codecs;

/// <summary>Converts ZXing's transport-neutral <see cref="PixelData"/> (tightly-packed BGRA) into a
/// GDI+ <see cref="Bitmap"/>. Kept separate so the QR and barcode renderers share one conversion and
/// the Core stays off ZXing's Windows.Compatibility binding (which would pin System.Drawing 9.x).</summary>
internal static class ZxingBitmap
{
    public static Bitmap From(PixelData pixelData)
    {
        ArgumentNullException.ThrowIfNull(pixelData);
        var bitmap = new Bitmap(pixelData.Width, pixelData.Height, PixelFormat.Format32bppRgb);
        BitmapData data = bitmap.LockBits(
            new Rectangle(0, 0, pixelData.Width, pixelData.Height),
            ImageLockMode.WriteOnly, PixelFormat.Format32bppRgb);
        try
        {
            int rowBytes = pixelData.Width * 4;
            for (int y = 0; y < pixelData.Height; y++)
                Marshal.Copy(pixelData.Pixels, y * rowBytes, data.Scan0 + y * data.Stride, rowBytes);
        }
        finally
        {
            bitmap.UnlockBits(data);
        }
        return bitmap;
    }
}
