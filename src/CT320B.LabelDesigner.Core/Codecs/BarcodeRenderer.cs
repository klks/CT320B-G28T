using System.Drawing;
using ZXing;
using ZXing.Common;

namespace CT320B.LabelDesigner.Core.Codecs;

/// <summary>
/// Renders 1-D barcodes to a <see cref="Bitmap"/> using ZXing's <see cref="BarcodeWriterPixelData"/>
/// (bars only — human-readable text is drawn by the element if wanted). Like
/// <see cref="QrCodeRenderer"/>, converts ZXing's pixel data to GDI+ here to avoid the
/// Windows.Compatibility binding.
/// </summary>
public static class BarcodeRenderer
{
    /// <summary>Renders <paramref name="content"/> as a barcode of the given symbology, filling the
    /// requested pixel size. Throws if the content is invalid for the symbology.</summary>
    public static Bitmap Render(string content, BarcodeFormat format, int width, int height, int margin = 0)
    {
        ArgumentException.ThrowIfNullOrEmpty(content);
        var writer = new BarcodeWriterPixelData
        {
            Format = format,
            Options = new EncodingOptions
            {
                Width = Math.Max(1, width),
                Height = Math.Max(1, height),
                Margin = margin,
                PureBarcode = true,   // no text; the element draws human-readable text itself
            },
        };
        return ZxingBitmap.From(writer.Write(content));
    }
}
