using System.Drawing;

namespace CT320B.LabelDesigner.Core.Editing;

/// <summary>A transient alignment guide line shown while dragging (in millimetres).</summary>
public readonly record struct GuideLine(bool Vertical, float PositionMm);

/// <summary>The offset to nudge a moving selection into alignment, plus the guides that matched.</summary>
public readonly record struct AlignmentSnap(float OffsetXMm, float OffsetYMm, IReadOnlyList<GuideLine> Guides);

/// <summary>An equal-spacing indicator: the gap segment to highlight while distributing. When
/// <see cref="Horizontal"/> the gap runs along X between <see cref="StartMm"/>..<see cref="EndMm"/> at the
/// <see cref="CrossMm"/> Y; otherwise it runs along Y at the <see cref="CrossMm"/> X.</summary>
public readonly record struct SpacingSpan(bool Horizontal, float StartMm, float EndMm, float CrossMm);

/// <summary>The offset to nudge a moving box so its gaps to its two neighbours become equal, per axis,
/// plus the gap segments to draw.</summary>
public readonly record struct DistributionSnap(
    float OffsetXMm, bool FoundX, float OffsetYMm, bool FoundY, IReadOnlyList<SpacingSpan> Spans);

/// <summary>
/// Computes "smart" alignment snapping: given a moving bounding box and the stationary elements + the
/// page, it finds the nearest edge/centre alignment within a tolerance on each axis independently and
/// returns the offset to apply plus the guide lines to draw. Pure geometry (no UI), so it's unit-tested.
/// </summary>
public static class AlignmentGuides
{
    /// <param name="moving">Bounding box of the element(s) being dragged (mm).</param>
    /// <param name="targets">Bounds of the stationary candidate elements (mm).</param>
    /// <param name="page">The label size (mm); its edges + centre are also snap candidates.</param>
    /// <param name="toleranceMm">Max distance to snap (typically a few screen px converted to mm).</param>
    public static AlignmentSnap Snap(RectangleF moving, IEnumerable<RectangleF> targets, SizeF page, float toleranceMm)
    {
        var vx = new List<float>();   // candidate vertical lines (x positions)
        var hy = new List<float>();   // candidate horizontal lines (y positions)
        foreach (RectangleF t in targets)
        {
            vx.Add(t.Left); vx.Add(t.Left + t.Width / 2f); vx.Add(t.Right);
            hy.Add(t.Top); hy.Add(t.Top + t.Height / 2f); hy.Add(t.Bottom);
        }
        vx.Add(0); vx.Add(page.Width / 2f); vx.Add(page.Width);
        hy.Add(0); hy.Add(page.Height / 2f); hy.Add(page.Height);

        float[] movX = [moving.Left, moving.Left + moving.Width / 2f, moving.Right];
        float[] movY = [moving.Top, moving.Top + moving.Height / 2f, moving.Bottom];

        (float off, bool found) sx = Best(movX, vx, toleranceMm);
        (float off, bool found) sy = Best(movY, hy, toleranceMm);

        var guides = new List<GuideLine>();
        if (sx.found) AddGuides(guides, vx, movX, sx.off, vertical: true);
        if (sy.found) AddGuides(guides, hy, movY, sy.off, vertical: false);

        return new AlignmentSnap(sx.found ? sx.off : 0f, sy.found ? sy.off : 0f, guides);
    }

    /// <summary>
    /// Equal-spacing snap: when the <paramref name="moving"/> box sits between two stationary
    /// <paramref name="targets"/> that overlap it on the perpendicular axis, finds the offset that makes
    /// its gap to each neighbour equal (within <paramref name="toleranceMm"/>), per axis independently,
    /// and returns the two gap segments to draw. Pure geometry; unit-tested.
    /// </summary>
    public static DistributionSnap Distribute(RectangleF moving, IEnumerable<RectangleF> targets, float toleranceMm)
    {
        List<RectangleF> list = targets as List<RectangleF> ?? targets.ToList();
        var spans = new List<SpacingSpan>();
        (float off, bool found) x = DistributeAxis(moving, list, toleranceMm, horizontal: true, spans);
        // Re-evaluate Y with the moving box already nudged on X, so both axes can resolve together.
        RectangleF movedX = new(moving.X + (x.found ? x.off : 0f), moving.Y, moving.Width, moving.Height);
        (float off, bool found) y = DistributeAxis(movedX, list, toleranceMm, horizontal: false, spans);
        return new DistributionSnap(x.off, x.found, y.off, y.found, spans);
    }

    // One axis of equal-spacing: find the nearest left/right (or top/bottom) neighbour that overlaps the
    // moving box on the other axis, then offset so the two gaps match.
    private static (float off, bool found) DistributeAxis(
        RectangleF moving, List<RectangleF> targets, float tol, bool horizontal, List<SpacingSpan> spans)
    {
        float mLo = horizontal ? moving.Left : moving.Top;
        float mHi = horizontal ? moving.Right : moving.Bottom;

        RectangleF? lo = null, hi = null;
        foreach (RectangleF t in targets)
        {
            if (!OverlapsCross(moving, t, horizontal)) continue;
            float tLo = horizontal ? t.Left : t.Top;
            float tHi = horizontal ? t.Right : t.Bottom;
            if (tHi <= mLo + tol && (lo is null || tHi > (horizontal ? lo.Value.Right : lo.Value.Bottom))) lo = t;
            if (tLo >= mHi - tol && (hi is null || tLo < (horizontal ? hi.Value.Left : hi.Value.Top))) hi = t;
        }
        if (lo is null || hi is null) return (0f, false);

        float loHi = horizontal ? lo.Value.Right : lo.Value.Bottom;
        float hiLo = horizontal ? hi.Value.Left : hi.Value.Top;
        float gapLo = mLo - loHi;
        float gapHi = hiLo - mHi;
        const float minGap = 0.3f;
        if (gapLo < minGap || gapHi < minGap) return (0f, false);

        float offset = (gapHi - gapLo) / 2f;   // move toward the larger gap to equalise
        if (MathF.Abs(offset) > tol) return (0f, false);

        float cross = (MathF.Max(horizontal ? moving.Top : moving.Left, horizontal ? lo.Value.Top : lo.Value.Left)
                       + MathF.Min(horizontal ? moving.Bottom : moving.Right, horizontal ? lo.Value.Bottom : lo.Value.Right)) / 2f;
        float newLo = mLo + offset, newHi = mHi + offset;
        spans.Add(new SpacingSpan(horizontal, loHi, newLo, cross));
        spans.Add(new SpacingSpan(horizontal, newHi, hiLo, cross));
        return (offset, true);
    }

    // True when two boxes overlap on the axis perpendicular to the one being distributed.
    private static bool OverlapsCross(RectangleF a, RectangleF b, bool horizontal) => horizontal
        ? a.Top < b.Bottom && b.Top < a.Bottom
        : a.Left < b.Right && b.Left < a.Right;

    // The smallest signed offset that brings any moving line within tolerance of any target line.
    private static (float off, bool found) Best(float[] moving, List<float> targets, float tol)
    {
        float best = tol, bestOff = 0f;
        bool found = false;
        foreach (float m in moving)
            foreach (float t in targets)
            {
                float d = t - m;
                if (MathF.Abs(d) <= best) { best = MathF.Abs(d); bestOff = d; found = true; }
            }
        return (bestOff, found);
    }

    // Every target line a moving line lands on (after the chosen offset) becomes a guide to draw.
    private static void AddGuides(List<GuideLine> guides, List<float> targets, float[] moving, float off, bool vertical)
    {
        foreach (float t in targets)
        {
            if (!moving.Any(m => MathF.Abs(t - (m + off)) <= 0.02f)) continue;
            if (!guides.Any(gd => gd.Vertical == vertical && MathF.Abs(gd.PositionMm - t) <= 0.02f))
                guides.Add(new GuideLine(vertical, t));
        }
    }
}
