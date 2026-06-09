using System.Drawing;
using ZXing;
using ZXing.QrCode;
using ZXing.Rendering;

namespace CT320B.LabelDesigner.Core.Codecs;

/// <summary>
/// Renders QR codes to a <see cref="Bitmap"/> for placement on a label. Uses ZXing's
/// <see cref="BarcodeWriterPixelData"/> (no System.Drawing dependency in ZXing itself) and converts
/// the returned BGRA pixel data into a GDI+ <see cref="Bitmap"/> here — so the Core stays on
/// System.Drawing.Common 8.0.x (matching the printer library) without the Windows.Compatibility binding.
/// </summary>
public static class QrCodeRenderer
{
    /// <summary>
    /// Renders <paramref name="content"/> as a square QR <see cref="Bitmap"/>.
    /// </summary>
    /// <param name="content">The data to encode (must be non-empty).</param>
    /// <param name="pixelSize">Target width/height in pixels (the code is square).</param>
    /// <param name="margin">Quiet-zone margin in modules (QR spec recommends ≥ 4).</param>
    /// <param name="errorCorrection">QR error-correction level (default M).</param>
    public static Bitmap Render(
        string content, int pixelSize = 200, int margin = 4,
        ZXing.QrCode.Internal.ErrorCorrectionLevel? errorCorrection = null)
    {
        ArgumentException.ThrowIfNullOrEmpty(content);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(pixelSize);

        var writer = new BarcodeWriterPixelData
        {
            Format = BarcodeFormat.QR_CODE,
            Options = new QrCodeEncodingOptions
            {
                Width = pixelSize,
                Height = pixelSize,
                Margin = margin,
                ErrorCorrection = errorCorrection ?? ZXing.QrCode.Internal.ErrorCorrectionLevel.M,
            },
        };

        PixelData pixelData = writer.Write(content);
        return ZxingBitmap.From(pixelData);
    }
}
