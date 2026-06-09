using System.Drawing;

namespace CT320B.LabelDesigner.Core.Model;

/// <summary>
/// Geometry guardrails: finds visible elements that extend outside the label's printable area, so the
/// print pipeline can warn before sending a design whose content would be clipped. Rotation is taken
/// into account (a rotated element's axis-aligned bounding box is used); flipping doesn't change the
/// bounding box.
/// </summary>
public static class LabelBounds
{
    /// <summary>The axis-aligned bounding box (mm) of an element after its rotation about centre.</summary>
    public static RectangleF RotatedBoundsMm(LabelElement element)
    {
        ArgumentNullException.ThrowIfNull(element);
        RectangleF b = element.BoundsMm;
        float rot = element.Rotation % 360f;
        if (rot == 0f) return b;

        double rad = rot * Math.PI / 180.0;
        double cos = Math.Cos(rad), sin = Math.Sin(rad);
        float cx = b.X + b.Width / 2f, cy = b.Y + b.Height / 2f;

        float minX = float.MaxValue, minY = float.MaxValue, maxX = float.MinValue, maxY = float.MinValue;
        ReadOnlySpan<(float dx, float dy)> corners =
        [
            (b.Left - cx, b.Top - cy), (b.Right - cx, b.Top - cy),
            (b.Right - cx, b.Bottom - cy), (b.Left - cx, b.Bottom - cy),
        ];
        foreach ((float dx, float dy) in corners)
        {
            float x = cx + (float)(dx * cos - dy * sin);
            float y = cy + (float)(dx * sin + dy * cos);
            minX = Math.Min(minX, x); maxX = Math.Max(maxX, x);
            minY = Math.Min(minY, y); maxY = Math.Max(maxY, y);
        }
        return RectangleF.FromLTRB(minX, minY, maxX, maxY);
    }

    /// <summary>
    /// Returns the visible elements whose (rotation-aware) bounds fall outside the label area
    /// <c>[0, WidthMm] × [0, HeightMm]</c>, beyond <paramref name="toleranceMm"/>. Empty when the whole
    /// design fits.
    /// </summary>
    public static IReadOnlyList<LabelElement> FindOutOfBounds(LabelDocument document, float toleranceMm = 0.01f)
    {
        ArgumentNullException.ThrowIfNull(document);
        var outside = new List<LabelElement>();
        foreach (LabelElement e in document.Elements)
        {
            if (!e.Visible) continue;
            RectangleF b = RotatedBoundsMm(e);
            if (b.Left < -toleranceMm || b.Top < -toleranceMm ||
                b.Right > document.WidthMm + toleranceMm || b.Bottom > document.HeightMm + toleranceMm)
                outside.Add(e);
        }
        return outside;
    }
}
