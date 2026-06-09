using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.Drawing.Text;
using CT320B.LabelDesigner.Core.Model;

namespace CT320B.LabelDesigner.Core.Rendering;

/// <summary>
/// Turns a <see cref="LabelDocument"/> into a <see cref="Bitmap"/> at a requested scale. This is the
/// one rendering path with two consumers: the canvas (antialiased, screen scale) and the print job
/// (no antialias, exactly <see cref="Units.DotsPerMm"/> px/mm). Elements are drawn back-to-front by
/// <see cref="LabelElement.ZOrder"/> onto a white page, each with its rotation applied about its centre.
/// </summary>
public static class LabelRenderer
{
    /// <summary>
    /// Renders the document at the given context's scale. <paramref name="background"/> fills the
    /// page (defaults to white, as the printer expects); pass <see cref="Color.Transparent"/> to get
    /// just the elements on a transparent layer (used by the editing canvas, which paints its own page).
    /// </summary>
    /// <param name="printableOnly">When true, elements with <see cref="LabelElement.Printable"/> = false
    /// are skipped — used for the print/raster path so pre-printed backgrounds aren't sent to the printer.</param>
    /// <param name="outputSize">Optional explicit bitmap size in pixels. When set, the bitmap is this size
    /// instead of the document's — used by the editing canvas (with <paramref name="contentOffsetMm"/>) to
    /// render a region that may extend beyond the page, so off-page content stays visible.</param>
    public static Bitmap Render(
        LabelDocument doc, RenderContext ctx, Color? background = null, PointF contentOffsetMm = default,
        bool printableOnly = false, Size? outputSize = null)
    {
        ArgumentNullException.ThrowIfNull(doc);
        ArgumentNullException.ThrowIfNull(ctx);

        int width = outputSize?.Width ?? Math.Max(1, (int)Math.Round(doc.WidthMm * ctx.PixelsPerMm, MidpointRounding.AwayFromZero));
        int height = outputSize?.Height ?? Math.Max(1, (int)Math.Round(doc.HeightMm * ctx.PixelsPerMm, MidpointRounding.AwayFromZero));
        width = Math.Max(1, width);
        height = Math.Max(1, height);

        var bmp = new Bitmap(width, height, PixelFormat.Format32bppArgb);
        // Set the bitmap's DPI so point-sized fonts (GraphicsUnit.Point) map to physical size at the
        // render scale; geometry drawn in pixels is unaffected.
        bmp.SetResolution((float)ctx.Dpi, (float)ctx.Dpi);

        using var g = Graphics.FromImage(bmp);
        g.Clear(background ?? Color.White);
        ApplyQuality(g, ctx.AntiAlias);

        // Print calibration: shift all content by this amount (mm) to compensate for a printer that
        // lands the image off-origin. Zero for the editing canvas.
        if (contentOffsetMm != default)
            g.TranslateTransform(ctx.MmToPx(contentOffsetMm.X), ctx.MmToPx(contentOffsetMm.Y));

        foreach (LabelElement element in doc.ElementsByZOrder)
        {
            if (!element.Visible) continue;
            if (printableOnly && !element.Printable) continue;

            GraphicsState state = g.Save();
            try
            {
                if (element.Rotation != 0f || element.FlipH || element.FlipV)
                {
                    RectangleF b = element.BoundsMm;
                    float cx = ctx.MmToPx(b.X + b.Width / 2f);
                    float cy = ctx.MmToPx(b.Y + b.Height / 2f);
                    g.TranslateTransform(cx, cy);
                    if (element.Rotation != 0f) g.RotateTransform(element.Rotation);
                    if (element.FlipH || element.FlipV)
                        g.ScaleTransform(element.FlipH ? -1f : 1f, element.FlipV ? -1f : 1f);
                    g.TranslateTransform(-cx, -cy);
                }
                element.Render(g, ctx);
            }
            finally
            {
                g.Restore(state);
            }
        }
        return bmp;
    }

    /// <summary>Convenience overload that renders at a display <paramref name="dpi"/>.</summary>
    public static Bitmap Render(LabelDocument doc, double dpi, bool antiAlias) =>
        Render(doc, new RenderContext(Units.PixelsPerMmAt(dpi), antiAlias));

    private static void ApplyQuality(Graphics g, bool antiAlias)
    {
        if (antiAlias)
        {
            g.SmoothingMode = SmoothingMode.AntiAlias;
            g.TextRenderingHint = TextRenderingHint.AntiAliasGridFit;
            g.InterpolationMode = InterpolationMode.HighQualityBicubic;
            g.PixelOffsetMode = PixelOffsetMode.HighQuality;
        }
        else
        {
            // Crisp 1-bpp-friendly output: no smoothing, hard edges (matches the print raster).
            g.SmoothingMode = SmoothingMode.None;
            g.TextRenderingHint = TextRenderingHint.SingleBitPerPixelGridFit;
            g.InterpolationMode = InterpolationMode.NearestNeighbor;
            g.PixelOffsetMode = PixelOffsetMode.Half;
        }
    }
}
