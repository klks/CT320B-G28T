using System.Drawing;
using CT320B.LabelDesigner.Core.Model;
using CT320B.LabelDesigner.Core.Serialization;

namespace CT320B.LabelDesigner.Core.Rendering;

/// <summary>
/// Pixel-accurate hit testing: does an element actually <i>paint</i> at (or near) a point? Used by the
/// editor so clicks pick "what you see" — an unfilled shape's empty interior, or a transparent area of
/// an image, lets the click fall through to whatever is painted behind it.
/// </summary>
public static class ElementHitTest
{
    /// <summary>True if <paramref name="element"/> renders a (near-)opaque pixel within
    /// <paramref name="toleranceMm"/> of <paramref name="mm"/>. Renders the element alone and samples its
    /// alpha. <paramref name="pixelsPerMm"/> is the sampling resolution (clamped); falls back to a bounding
    /// box test for elements too large to raster cheaply.</summary>
    public static bool PaintsAt(LabelElement element, PointF mm, float toleranceMm, double pixelsPerMm)
    {
        ArgumentNullException.ThrowIfNull(element);
        RectangleF rb = RectangleF.Inflate(LabelBounds.RotatedBoundsMm(element), toleranceMm, toleranceMm);
        if (rb.Width <= 0 || rb.Height <= 0) return false;

        double ppm = Math.Clamp(pixelsPerMm, 2.0, 24.0);   // resolve thin strokes while capping bitmap size
        int w = (int)Math.Ceiling(rb.Width * ppm), h = (int)Math.Ceiling(rb.Height * ppm);
        if (w < 1 || h < 1) return false;
        if ((long)w * h > 6_000_000) return element.BoundsMm.Contains(mm);   // too big → bounding-box fallback

        // Deep-copy the element (via a throwaway document) and shift it to the buffer's origin so the
        // whole rotated extent renders inside the bitmap.
        var doc = new LabelDocument { WidthMm = (float)(w / ppm), HeightMm = (float)(h / ppm) };
        doc.Elements.Add(element);
        LabelElement el = LabelJson.Clone(doc).Elements[0];
        el.XMm -= rb.X;
        el.YMm -= rb.Y;
        el.Visible = true;
        el.Printable = true;
        var probe = new LabelDocument { WidthMm = doc.WidthMm, HeightMm = doc.HeightMm };
        probe.Elements.Add(el);

        using Bitmap bmp = LabelRenderer.Render(probe, RenderContext.ForScreen(ppm), Color.Transparent);
        int cx = (int)Math.Round((mm.X - rb.X) * ppm), cy = (int)Math.Round((mm.Y - rb.Y) * ppm);
        int r = (int)Math.Ceiling(toleranceMm * ppm);
        for (int dy = -r; dy <= r; dy++)
            for (int dx = -r; dx <= r; dx++)
            {
                int sx = cx + dx, sy = cy + dy;
                if (sx >= 0 && sy >= 0 && sx < bmp.Width && sy < bmp.Height && bmp.GetPixel(sx, sy).A > 16)
                    return true;
            }
        return false;
    }
}
