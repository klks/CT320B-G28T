using System.Drawing;
using CT320B.LabelDesigner.Core.Model;
using CT320B.LabelDesigner.Core.Rendering;
using CT320B.UsbApi;
using CT320B.UsbApi.Imaging;

namespace CT320B.LabelDesigner.Core.Printing;

/// <summary>
/// Renders a <see cref="LabelDocument"/> at the printer's dot pitch (no antialiasing) and prints it
/// through the validated <c>CT320BPrinter.PrintImageLabel</c> path — the WYSIWYG strategy (Decision
/// D1): the whole label becomes one bitmap, keeping us on the firmware's one known-good sequence
/// and making preview == print. The rendered bitmap is exactly
/// <c>WidthMm*8 × HeightMm*8</c> device dots.
/// </summary>
public static class LabelPrintJob
{
    /// <summary>Renders the document to the exact bitmap that will be sent to the printer
    /// (203 dpi / 8 dots-per-mm, antialiasing off), applying the document's print-offset calibration.
    /// Useful for the print-preview window too.</summary>
    public static Bitmap RenderForPrint(LabelDocument document)
    {
        ArgumentNullException.ThrowIfNull(document);
        var offset = new PointF(document.PrintOffsetXMm, document.PrintOffsetYMm);
        // printableOnly: pre-printed backgrounds (Printable=false) are on the stock already, not sent.
        return LabelRenderer.Render(document, RenderContext.ForPrint(), background: null,
            contentOffsetMm: offset, printableOnly: true);
    }

    /// <summary>
    /// Produces the exact 1-bpp raster that <see cref="Print"/> sends to the printer: renders the
    /// document at the dot pitch, then packs it with <see cref="MonochromeRasterizer"/> at the same
    /// <c>ceil(width/8)</c> stride <c>PrintImageLabel</c> uses. The bytes are byte-for-byte what the
    /// printer receives, so the preview is a true WYSIWYG check.
    /// </summary>
    public static MonochromeRaster RasterizeForPrint(LabelDocument document)
    {
        ArgumentNullException.ThrowIfNull(document);
        using Bitmap bmp = RenderForPrint(document);
        return MonochromeRasterizer.Rasterize(bmp, MonochromeRasterizer.StrideBytes(bmp.Width));
    }

    /// <summary>Renders the print preview: the actual 1-bpp dots the printer will mark, as a viewable
    /// black/white <see cref="Bitmap"/> (a set bit → black). Use this in the "Print preview" window.</summary>
    public static Bitmap RenderMonochromePreview(LabelDocument document) =>
        MonochromePreview.ToBitmap(RasterizeForPrint(document));

    /// <summary>
    /// Renders <paramref name="document"/> and prints it via <paramref name="printer"/>. Page
    /// parameters (gap/speed/density) come from the document; <paramref name="copies"/> and the
    /// (x,y) print offset are caller-supplied.
    /// </summary>
    public static void Print(
        CT320BPrinter printer, LabelDocument document, float x = 0f, float y = 0f, uint copies = 1)
    {
        ArgumentNullException.ThrowIfNull(printer);
        ArgumentNullException.ThrowIfNull(document);

        using Bitmap bmp = RenderForPrint(document);
        printer.PrintImageLabel(
            bmp, document.WidthMm, document.HeightMm, x, y,
            document.GapMm, document.Speed, document.Density, copies);
    }
}
